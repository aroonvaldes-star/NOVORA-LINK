<#
NOVORA V1 launcher.  Run from a PowerShell window:
  .\Start.ps1 status
  .\Start.ps1 mirror -Profile low
  .\Start.ps1 tether
#>
[CmdletBinding()]
param(
    [ValidateSet('status', 'devices', 'mirror', 'tether', 'start', 'stop', 'help')]
    [string] $Command = 'status',
    [ValidateSet('low', 'balanced', 'quality')]
    [string] $Profile = 'low',
    [string] $Serial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:NovoraRoot = $PSScriptRoot
$script:StatePath = Join-Path $script:NovoraRoot 'temp\runtime-state.json'
$script:LogPath = Join-Path $script:NovoraRoot 'logs\novora-v1.log'
$script:AdbPath = Join-Path $script:NovoraRoot 'tools\adb\adb.exe'
$script:ScrcpyPath = Join-Path $script:NovoraRoot 'tools\scrcpy\scrcpy.exe'
$script:GnirehtetPath = Join-Path $script:NovoraRoot 'tools\gnirehtet\gnirehtet.exe'
$script:IntegrityPath = Join-Path $script:NovoraRoot 'integrity.json'

function Write-NovoraLog {
    param([ValidateSet('INFO', 'WARN', 'ERROR')][string] $Level, [string] $Message)
    $line = '[{0}] [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    Add-Content -LiteralPath $script:LogPath -Value $line -Encoding utf8
    $color = if ($Level -eq 'ERROR') { 'Red' } elseif ($Level -eq 'WARN') { 'Yellow' } else { 'Cyan' }
    Write-Host $line -ForegroundColor $color
}

function Initialize-NovoraRuntime {
    foreach ($path in @((Split-Path $script:StatePath), (Split-Path $script:LogPath))) {
        if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
    }
    # ADB must have a writable home; this avoids a global machine dependency for NOVORA.
    if ([string]::IsNullOrWhiteSpace($env:ANDROID_USER_HOME)) {
        $env:ANDROID_USER_HOME = Join-Path $script:NovoraRoot 'temp\android-home'
    }
    if (-not (Test-Path -LiteralPath $env:ANDROID_USER_HOME)) { New-Item -ItemType Directory -Path $env:ANDROID_USER_HOME -Force | Out-Null }
    # The bundled Windows ADB falls back to USERPROFILE/HOME on some hosts.
    # Keep its keys inside NOVORA instead of failing when that profile is unavailable.
    $env:USERPROFILE = $env:ANDROID_USER_HOME
    $env:HOME = $env:ANDROID_USER_HOME
    $env:HOMEDRIVE = [System.IO.Path]::GetPathRoot($env:ANDROID_USER_HOME).TrimEnd('\')
    $env:HOMEPATH = $env:ANDROID_USER_HOME.Substring($env:HOMEDRIVE.Length)
}

function Test-NovoraTool { param([string] $Path, [string] $Name)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name no se encontró en: $Path" }
}

function Test-NovoraIntegrity {
    if (-not (Test-Path -LiteralPath $script:IntegrityPath -PathType Leaf)) { throw 'Falta integrity.json; no se ejecutarán binarios empaquetados.' }
    foreach ($entry in (Get-Content -Raw -LiteralPath $script:IntegrityPath | ConvertFrom-Json)) {
        $path = Join-Path $script:NovoraRoot $entry.path
        Test-NovoraTool $path $entry.path
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actual -ne $entry.sha256) { throw "Integridad inválida: $($entry.path). Descarga el paquete V1 nuevamente." }
    }
}

function Invoke-NovoraAdb {
    param([Parameter(Mandatory)][string[]] $Arguments)
    Test-NovoraTool $script:AdbPath 'ADB'
    $result = & $script:AdbPath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ADB falló: $($result -join ' ')" }
    return @($result | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ })
}

function Get-NovoraDevices {
    $lines = Invoke-NovoraAdb @('devices', '-l')
    $devices = foreach ($line in $lines) {
        if ($line -match '^(?<serial>\S+)\s+(?<state>device|offline|unauthorized)(?<details>.*)$') {
            [pscustomobject]@{ Serial = $Matches.serial; State = $Matches.state; Details = $Matches.details.Trim() }
        }
    }
    return @($devices)
}

function Get-NovoraTarget {
    param([string] $RequestedSerial)
    $devices = Get-NovoraDevices
    $ready = @($devices | Where-Object State -eq 'device')
    if ($RequestedSerial) {
        $target = @($ready | Where-Object Serial -eq $RequestedSerial)
        if ($target.Count -ne 1) { throw "El dispositivo '$RequestedSerial' no está autorizado y listo." }
        return $target[0]
    }
    if ($ready.Count -eq 0) { throw 'No hay ningún dispositivo autorizado. Conecta el teléfono y acepta la depuración USB.' }
    if ($ready.Count -gt 1) { throw 'Hay varios teléfonos conectados. Indica -Serial <id>.' }
    return $ready[0]
}

function Save-NovoraState { param([hashtable] $State)
    $State | ConvertTo-Json | Set-Content -LiteralPath $script:StatePath -Encoding utf8
}

function Read-NovoraState {
    if (-not (Test-Path -LiteralPath $script:StatePath)) { return @{} }
    try { return (Get-Content -Raw -LiteralPath $script:StatePath | ConvertFrom-Json -AsHashtable) } catch { return @{} }
}

function Wait-NovoraCondition {
    param([Parameter(Mandatory)][scriptblock] $Condition, [int] $TimeoutSeconds = 10)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Test-NovoraGnirehtetClient {
    param([Parameter(Mandatory)][string] $DeviceSerial)
    try {
        $pid = Invoke-NovoraAdb @('-s', $DeviceSerial, 'shell', 'pidof', 'com.genymobile.gnirehtet')
        return -not [string]::IsNullOrWhiteSpace(($pid -join ''))
    } catch { return $false }
}

function Start-NovoraMirror {
    param([string] $RequestedSerial, [string] $RequestedProfile)
    Test-NovoraTool $script:ScrcpyPath 'scrcpy'
    $currentState = Read-NovoraState
    if ($currentState.MirrorPid -and (Get-Process -Id $currentState.MirrorPid -ErrorAction SilentlyContinue)) { throw 'Ya hay una sesión de mirroring de NOVORA activa. Ejecuta stop antes de iniciar otra.' }
    $target = Get-NovoraTarget $RequestedSerial
    $profiles = @{
        low      = @('--max-size=1024', '--max-fps=30', '--video-bit-rate=4M', '--no-audio')
        balanced = @('--max-size=1280', '--max-fps=45', '--video-bit-rate=8M', '--no-audio')
        quality  = @('--max-size=1920', '--max-fps=60', '--video-bit-rate=12M')
    }
    $arguments = @('--serial', $target.Serial) + $profiles[$RequestedProfile]
    $process = Start-Process -FilePath $script:ScrcpyPath -ArgumentList $arguments -WorkingDirectory (Split-Path $script:ScrcpyPath) -PassThru
    Start-Sleep -Milliseconds 800
    if ($process.HasExited) { throw "scrcpy terminó al iniciar (código $($process.ExitCode)). Revisa $script:LogPath" }
    Save-NovoraState @{ MirrorPid = $process.Id; MirrorSerial = $target.Serial; MirrorProfile = $RequestedProfile }
    Write-NovoraLog INFO "Mirroring activo en $($target.Serial), perfil $RequestedProfile (bajo consumo)."
}

function Start-NovoraTether {
    param([string] $RequestedSerial)
    Test-NovoraTool $script:GnirehtetPath 'gnirehtet'
    $target = Get-NovoraTarget $RequestedSerial
    # Gnirehtet is reverse tethering through a local Android VPN, not an anonymity VPN.
    $process = Start-Process -FilePath $script:GnirehtetPath -ArgumentList @('run', $target.Serial) -WorkingDirectory (Split-Path $script:GnirehtetPath) -PassThru
    Start-Sleep -Seconds 2
    if (-not (Wait-NovoraCondition -TimeoutSeconds 8 -Condition { Test-NovoraGnirehtetClient -DeviceSerial $target.Serial })) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw 'La VPN local de gnirehtet no se confirmó en el teléfono. Revisa la autorización ADB y la notificación VPN.'
    }
    if ($process.HasExited) { throw "Gnirehtet terminó al iniciar (código $($process.ExitCode))." }
    $state = Read-NovoraState; $state.TetherPid = $process.Id; $state.TetherSerial = $target.Serial; Save-NovoraState $state
    Write-NovoraLog INFO "Tethering activo en $($target.Serial). Confirma la notificación VPN de gnirehtet en el teléfono."
}

function Start-NovoraSession {
    param([string] $RequestedSerial, [string] $RequestedProfile)
    try {
        Start-NovoraTether -RequestedSerial $RequestedSerial
        Start-NovoraMirror -RequestedSerial $RequestedSerial -RequestedProfile $RequestedProfile
    } catch {
        Stop-NovoraSession
        throw
    }
}

function Stop-NovoraSession {
    $state = Read-NovoraState
    # State is local data, not authority: only terminate a process that is still
    # the NOVORA-packaged executable recorded for this session.
    if ($state.MirrorPid) {
        $mirror = Get-Process -Id $state.MirrorPid -ErrorAction SilentlyContinue
        if ($mirror -and $mirror.Path -eq $script:ScrcpyPath) { Stop-Process -Id $state.MirrorPid -ErrorAction SilentlyContinue }
        elseif ($mirror) { Write-NovoraLog WARN 'El PID de mirroring no pertenece a scrcpy de NOVORA; no se cerró.' }
    }
    if ($state.TetherPid) {
        try { & $script:GnirehtetPath stop $state.TetherSerial 2>$null | Out-Null } catch { }
        $tether = Get-Process -Id $state.TetherPid -ErrorAction SilentlyContinue
        if ($tether -and $tether.Path -eq $script:GnirehtetPath) { Stop-Process -Id $state.TetherPid -ErrorAction SilentlyContinue }
        elseif ($tether) { Write-NovoraLog WARN 'El PID de tethering no pertenece a gnirehtet de NOVORA; no se cerró.' }
    }
    Remove-Item -LiteralPath $script:StatePath -Force -ErrorAction SilentlyContinue
    Write-NovoraLog INFO 'Sesión NOVORA detenida.'
}

function Show-NovoraStatus {
    foreach ($tool in @(@($script:AdbPath, 'ADB'), @($script:ScrcpyPath, 'scrcpy'), @($script:GnirehtetPath, 'gnirehtet'))) {
        Write-NovoraLog INFO ('{0}: {1}' -f $tool[1], (Test-Path -LiteralPath $tool[0]))
    }
    $devices = Get-NovoraDevices
    if ($devices.Count -eq 0) { Write-NovoraLog WARN 'No hay teléfonos detectados.' }
    $devices | ForEach-Object { Write-NovoraLog INFO ('{0}: {1} {2}' -f $_.Serial, $_.State, $_.Details) }
    $state = Read-NovoraState
    if ($state.Count) { Write-NovoraLog INFO ('Sesión registrada: ' + ($state | ConvertTo-Json -Compress)) }
}

Initialize-NovoraRuntime
try {
    Test-NovoraIntegrity
    switch ($Command) {
        'status'  { Show-NovoraStatus }
        'devices' { Get-NovoraDevices | Format-Table -AutoSize }
        'mirror'  { Start-NovoraMirror -RequestedSerial $Serial -RequestedProfile $Profile }
        'tether'  { Start-NovoraTether -RequestedSerial $Serial }
        'start'   { Start-NovoraSession -RequestedSerial $Serial -RequestedProfile $Profile }
        'stop'    { Stop-NovoraSession }
        'help'    { Get-Help $PSCommandPath -Detailed }
    }
} catch {
    Write-NovoraLog ERROR $_.Exception.Message
    exit 1
}
