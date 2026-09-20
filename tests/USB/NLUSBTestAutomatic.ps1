[CmdletBinding()]
param(
    [string]$RepoPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$Serial = 'R5CY3118MEW',
    [switch]$LaunchPc,
    [switch]$InteractiveLifecycle,
    [switch]$InteractiveEngines,
    [ValidateRange(5,180)][int]$TimeoutSeconds = 45
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$RepoPath = [IO.Path]::GetFullPath($RepoPath)
$Adb = Join-Path $RepoPath 'src\NOVORA\Tools\adb.exe'
$Results = New-Object 'System.Collections.Generic.List[object]'
$TestStarted = [DateTimeOffset]::UtcNow
$LastObservation = $null
$RuntimePassed = $false
$LifecyclePassed = $false
$BinaryMatched = $false
$Failure = $null
$OutputDir = Join-Path $PSScriptRoot 'results'
[void][IO.Directory]::CreateDirectory($OutputDir)

function Record-UsbTest([string]$Name, [string]$Status, [string]$Evidence) {
    $Results.Add([pscustomobject]@{ Name=$Name; Status=$Status; Evidence=$Evidence })
    $color = 'Yellow'
    if ($Status -eq 'PASS') { $color='Green' }
    if ($Status -eq 'FAIL') { $color='Red' }
    Write-Host ("{0,-12} {1}: {2}" -f $Status,$Name,$Evidence) -ForegroundColor $color
}
function Invoke-UsbAdb([string[]]$Arguments) {
    # Capture stderr without treating a normal disconnected transport as a PowerShell exception.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $Adb @Arguments 2>&1)
        $code = $LASTEXITCODE
        return [pscustomobject]@{ ExitCode=$code; Text=($output | Out-String).Trim() }
    } finally { $ErrorActionPreference=$previous }
}
function Get-UsbObservation {
    $pidResult = Invoke-UsbAdb @('-s',$Serial,'shell','pidof','com.novora.appcontrol')
    if ($pidResult.ExitCode -ne 0 -or $pidResult.Text -notmatch '^\d+(?:\s+\d+)*$') { return $null }
    $androidPid = ($pidResult.Text -split '\s+')[0]
    $logs = Invoke-UsbAdb @('-s',$Serial,'logcat','-d','--pid',$androidPid,'-v','raw','-s','NOVORA-USB:I','*:S')
    if ($logs.ExitCode -ne 0) { return $null }
    $lines = @($logs.Text -split '\r?\n')
    for ($i=$lines.Count-1; $i -ge 0; $i--) {
        if (-not $lines[$i].StartsWith('{') -or $lines[$i] -notmatch 'NOVORA_USB_EVIDENCE_V1') { continue }
        try {
            $e = $lines[$i] | ConvertFrom-Json
            if ($e.Schema -eq 'NOVORA_USB_EVIDENCE_V1' -and $e.State.Build -eq 'NOVORA_AUTOUSB_V1' -and
                [string]$e.State.Pid -eq $androidPid) { return $e }
        } catch { continue }
    }
    return $null
}
function Get-UsbPcConnections([int]$Owner) {
    @(Get-NetTCPConnection -LocalPort 27214 -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -eq $Owner })
}
function Wait-UsbSession([int]$Owner, [DateTimeOffset]$After) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $obs = Get-UsbObservation
        if ($null -ne $obs) { $script:LastObservation=$obs }
        $established = @(Get-UsbPcConnections $Owner | Where-Object { $_.State -eq 'Established' })
        if ($null -ne $obs -and ([DateTimeOffset]$obs.Utc) -ge $After -and
            $obs.State.Origin -eq 'AutomaticUsb' -and $obs.State.Transport -eq 'USB' -and
            $obs.State.Phase -eq 'Connected' -and $obs.State.Confirmed -eq $true -and
            $established.Count -gt 0) { return $obs }
        # Bounded sampling belongs to this diagnostic script, NOT the NOVORA runtime.
        Start-Sleep -Milliseconds 600
    } while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    return $null
}
try {
    if ($env:OS -ne 'Windows_NT') { throw 'La prueba fisica requiere Windows y el telefono real.' }
    if (-not (Test-Path -LiteralPath $Adb -PathType Leaf)) { throw "Falta ADB: $Adb" }
    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) { throw 'No esta disponible Get-NetTCPConnection.' }
    & (Join-Path $PSScriptRoot 'NLUSBTestPolicy.ps1') -RepoPath $RepoPath
    Record-UsbTest 'POLICY' 'PASS' 'Se ejecuto la clase de produccion con casos de conexion, duplicados, pausa y reconexion.'
    & (Join-Path $PSScriptRoot 'NLUSBTestAutomaticSource.ps1') -RepoPath $RepoPath
    Record-UsbTest 'SOURCE_CONTRACTS' 'PASS' 'Wiring, seguridad y capacidades; no es una prueba de motores.'

    $buildPath = Join-Path $OutputDir 'build-latest.json'
    if (-not (Test-Path -LiteralPath $buildPath)) { throw 'Falta build-latest.json. Ejecuta primero el parche sin SkipBuild.' }
    $build = [IO.File]::ReadAllText($buildPath) | ConvertFrom-Json
    if ($build.PcBuild -ne 'PASS' -or $build.AndroidBuild -ne 'PASS') { throw 'La evidencia de build no contiene ambas compilaciones correctas.' }
    foreach ($item in @($build.SourceHashes)) {
        $path = Join-Path $RepoPath $item.Relative
        if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.Hash) {
            throw "El codigo cambio despues del build: $($item.Relative)"
        }
    }
    $Exe = [string]$build.PcExe
    $Apk = [string]$build.Apk
    $pcAssembly = [string]$build.PcAssembly
    if ((Get-FileHash -LiteralPath $pcAssembly -Algorithm SHA256).Hash -ne $build.PcAssemblySha256 -or
        (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash -ne $build.PcExeSha256 -or
        (Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash -ne $build.ApkSha256) { throw 'Los binarios no coinciden con el build registrado.' }
    Record-UsbTest 'BUILDS' 'PASS' 'PC y Android, binarios y fuentes coinciden con build-latest.json.'

    $device = Invoke-UsbAdb @('-s',$Serial,'get-state')
    if ($device.ExitCode -ne 0 -or $device.Text -ne 'device') { throw 'El A56 no esta autorizado en ADB.' }
    Record-UsbTest 'ADB_DEVICE' 'PASS' 'Dispositivo solicitado autorizado.'
    $package = Invoke-UsbAdb @('-s',$Serial,'shell','pm','path','--user','0','com.novora.appcontrol')
    if ($package.ExitCode -ne 0 -or $package.Text -notmatch '(?m)^package:(.+/base\.apk)\s*$') { throw 'No se localizo com.novora.appcontrol en el usuario 0.' }
    $remoteApk = $Matches[1].Trim()
    $apkHash = Invoke-UsbAdb @('-s',$Serial,'shell','sha256sum',$remoteApk)
    if ($apkHash.ExitCode -eq 0 -and $apkHash.Text -match '^([a-fA-F0-9]{64})\s') {
        if ($Matches[1] -ne $build.ApkSha256) { throw 'La APK instalada no es la que acaba de compilarse. No se probara una APK vieja.' }
        $BinaryMatched=$true
        Record-UsbTest 'INSTALLED_APK' 'PASS' 'SHA256 del base.apk instalado coincide con la APK firmada.'
    } else { Record-UsbTest 'INSTALLED_APK' 'NOT_TESTED' 'Android no permitio comprobar SHA256; no se afirmara verificacion total.' }

    $running = @(Get-Process -Name NOVORA -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
    if ($LaunchPc -and $running.Count -eq 0) {
        $pcProcess = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
        $pcOwner = $pcProcess.Id
        $after = $TestStarted
    } elseif ($running.Count -eq 1) {
        $pcOwner = $running[0].Id
        if (([DateTimeOffset]$running[0].StartTime) -lt ([DateTimeOffset]$build.CompletedUtc).AddSeconds(-2)) {
            throw 'NOVORA PC se inicio antes del ultimo build. Cierra y abre el binario nuevo para no probar una DLL anterior.'
        }
        $after = [DateTimeOffset]$running[0].StartTime
    } else { throw 'Abre solo el NOVORA.exe del build registrado, o ejecuta con -LaunchPc.' }
    Write-Host 'Observando arranque N-N. No pulses Preparar USB ni Conectar por USB.' -ForegroundColor Cyan
    $obs = Wait-UsbSession $pcOwner $after
    if ($null -eq $obs) { throw 'No hubo sesion USB automatica confirmada dentro del limite. Revisa el estado de PC/Android; el test no marco PASS.' }
    $reverse = Invoke-UsbAdb @('-s',$Serial,'reverse','--list')
    if ($reverse.ExitCode -ne 0 -or $reverse.Text -notmatch '(?m)\btcp:27214\s+tcp:27214\s*$') { throw 'Falta la ruta USB 27214 confirmada.' }
    $RuntimePassed=$true
    Record-UsbTest 'USB_AUTOCONNECT' 'PASS' 'Android confirma sesion AutomaticUsb y existe TCP Established propiedad de NOVORA PC.'
    Record-UsbTest 'USB_REVERSE' 'PASS' '27214 -> 27214; el test no crea ni corrige esa ruta.'
    $s=$obs.State
    if ($s.LinkCanStart -or $s.LinkCanStop -or $s.LinkRunning) { Record-UsbTest 'LINKENGINE_READY' 'PASS' 'Capacidad reportada por la sesion real.' }
    else { Record-UsbTest 'LINKENGINE_READY' 'FAIL' 'La sesion existe, pero PC no habilito LinkEngine. No se fuerzan los botones.' }
    if ($s.VideoCanStart -or $s.VideoCanStop -or $s.VideoRunning) { Record-UsbTest 'VISIONENGINE_READY' 'PASS' 'Capacidad reportada por la sesion real.' }
    else { Record-UsbTest 'VISIONENGINE_READY' 'FAIL' 'Revisa el monitor seleccionado y el estado de VE en PC.' }
    if ($s.FileSharing) { Record-UsbTest 'FILE_SHARING_READY' 'PASS' 'La sesion anuncia intercambio de archivos.' }
    else { Record-UsbTest 'FILE_SHARING_READY' 'FAIL' 'La sesion no anuncia intercambio de archivos.' }

    if ($InteractiveEngines) {
        [void](Read-Host 'Inicia LinkEngine en Android, autoriza VPN si se solicita y espera su estado activo. Pulsa Enter despues')
        $engineObs=Get-UsbObservation
        if ($null -ne $engineObs -and $engineObs.State.Confirmed -and $engineObs.State.LinkRunning -and
            $engineObs.State.VpnRunning -and $engineObs.State.LinkState -eq 'Running') {
            Record-UsbTest 'LINKENGINE_START' 'PASS' 'PC Running y servicio VPN Android activo. No demuestra trafico de Internet.'
        } else { Record-UsbTest 'LINKENGINE_START' 'FAIL' 'No se confirmaron simultaneamente PC Running y VPN Android.' }
        [void](Read-Host 'Inicia VisionEngine y espera la imagen en PC. Pulsa Enter despues')
        $engineObs=Get-UsbObservation
        if ($null -ne $engineObs -and $engineObs.State.Confirmed -and $engineObs.State.VideoRunning) {
            Record-UsbTest 'VISIONENGINE_START' 'PASS' 'La sesion reporta VideoRunning; calidad, audio e input se prueban aparte.'
        } else { Record-UsbTest 'VISIONENGINE_START' 'FAIL' 'La sesion no reporto VideoRunning.' }
    } else {
        Record-UsbTest 'LINKENGINE_START' 'NOT_TESTED' 'No se inicio VPN automaticamente.'
        Record-UsbTest 'VISIONENGINE_START' 'NOT_TESTED' 'No se inicio video automaticamente.'
    }

    if ($InteractiveLifecycle) {
        [void](Read-Host 'Desconecta fisicamente el USB y pulsa Enter')
        $offline=Invoke-UsbAdb @('-s',$Serial,'get-state')
        if ($offline.ExitCode -eq 0 -and $offline.Text -eq 'device') { throw 'El dispositivo sigue online; no se simula una desconexion.' }
        $clock=[Diagnostics.Stopwatch]::StartNew()
        do {
            $remaining=@(Get-UsbPcConnections $pcOwner | Where-Object { $_.State -eq 'Listen' -or $_.State -eq 'Established' })
            if ($remaining.Count -eq 0) { break }
            Start-Sleep -Milliseconds 500
        } while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds)
        if ($remaining.Count -ne 0) { throw 'PC no limpio el canal 27214 despues de desconectar.' }
        Record-UsbTest 'USB_DISCONNECT_CLEANUP' 'PASS' 'Desconexion ADB real y canal PC 27214 retirado.'
        $reconnectAfter=[DateTimeOffset]::UtcNow
        [void](Read-Host 'Reconecta el USB sin pulsar los botones de conexion y pulsa Enter')
        $again=Wait-UsbSession $pcOwner $reconnectAfter
        if ($null -eq $again) { throw 'No se confirmo una nueva sesion automatica despues de reconectar.' }
        $LifecyclePassed=$true
        Record-UsbTest 'USB_RECONNECT' 'PASS' 'Nueva evidencia posterior a la desconexion, con AutomaticUsb y TCP activo.'
    } else {
        Record-UsbTest 'USB_DISCONNECT_CLEANUP' 'NOT_TESTED' 'Requiere -InteractiveLifecycle y desconexion fisica.'
        Record-UsbTest 'USB_RECONNECT' 'NOT_TESTED' 'Requiere -InteractiveLifecycle.'
    }
    Record-UsbTest 'INTERNET_TRAFFIC_AUDIO_INPUT_FILES' 'NOT_TESTED' 'No se deduce funcionamiento de todas las funciones por tener sesion o motor activo.'
} catch {
    $Failure=$_.Exception.Message
    Record-UsbTest 'EXECUTION' 'FAIL' $Failure
} finally {
    $failed=@($Results | Where-Object { $_.Status -eq 'FAIL' }).Count -gt 0
    $overall='PARTIAL'
    if ($failed) { $overall='FAIL' }
    elseif ($RuntimePassed -and $LifecyclePassed -and $BinaryMatched) { $overall='VERIFIED_USB_LIFECYCLE' }
    $report=[pscustomobject]@{
        Schema='NOVORA_USB_TEST_V1'; StartedUtc=$TestStarted.ToString('o'); FinishedUtc=[DateTimeOffset]::UtcNow.ToString('o')
        Overall=$overall; Scope='USB automatico y ciclo de conexion; NO certifica todas las funciones ni calidad de motores.'
        Checks=@($Results.ToArray()); LastObservation=$LastObservation
    }
    $stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $file=Join-Path $OutputDir "USB-$stamp.json"
    [IO.File]::WriteAllText($file,($report | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    $text=($Results | Format-Table Name,Status,Evidence -Wrap | Out-String -Width 180)
    [IO.File]::WriteAllText((Join-Path $OutputDir "USB-$stamp.txt"),"RESULTADO: $overall`r`n$text",[Text.UTF8Encoding]::new($false))
    Write-Host "Resultado: $overall" -ForegroundColor Cyan
    Write-Host "Evidencia: $file" -ForegroundColor Cyan
}
if ($null -ne $Failure) { throw $Failure }
if (@($Results | Where-Object { $_.Status -eq 'FAIL' }).Count -gt 0) { throw 'Una o mas verificaciones USB fallaron. Consulta el reporte.' }