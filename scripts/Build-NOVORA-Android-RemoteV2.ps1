#requires -Version 5.1

& {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest

    # =====================================================================
    # NOVORA-LINK
    # Android Remote V2.2 - Build / Test / Deploy
    #
    # Compatible con Windows PowerShell 5.1.
    # Usa un proyecto de tests Remote V2 aislado para no arrastrar tests legacy;
    # valida el parche ya aplicado, limpia sólo
    # outputs Release Android, ejecuta tests, compila Windows+Android,
    # verifica manifest generado, actualiza Tools y despliega por ADB cuando
    # hay exactamente un dispositivo.
    # =====================================================================

    $RepoPath =
        'C:\Users\Aroon\Desktop\NOVORA-LINK'

    $PackageName =
        'com.novora.linkengine'

    $ExpectedVersionName =
        '1.4.A3'

    $ExpectedVersionCode =
        '2'

    function Section {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Text
        )

        Write-Host ''
        Write-Host '============================================================' -ForegroundColor Cyan
        Write-Host " $Text" -ForegroundColor Cyan
        Write-Host '============================================================' -ForegroundColor Cyan
    }

    function Require-File {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Path
        )

        if (-not (
            Test-Path `
                -LiteralPath $Path `
                -PathType Leaf
        )) {
            throw "No existe: $Path"
        }
    }

    function Require-Directory {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Path
        )

        if (-not (
            Test-Path `
                -LiteralPath $Path `
                -PathType Container
        )) {
            throw "No existe: $Path"
        }
    }

    function Require-Contains {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Path,

            [Parameter(Mandatory = $true)]
            [string]$Token,

            [Parameter(Mandatory = $true)]
            [string]$Description
        )

        Require-File $Path

        $text =
            [IO.File]::ReadAllText(
                $Path)

        if (-not $text.Contains($Token)) {
            throw "Precheck falló: $Description. Token ausente: $Token"
        }

        Write-Host "[OK] $Description" -ForegroundColor Green
    }

    function Run-Dotnet {
        param(
            [Parameter(Mandatory = $true)]
            [string[]]$Arguments
        )

        Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray

        & dotnet @Arguments

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') falló con código $LASTEXITCODE."
        }
    }

    function Get-ConnectedAdbSerials {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Adb
        )

        $output =
            & $Adb devices 2>&1

        if ($LASTEXITCODE -ne 0) {
            throw "adb devices falló con código $LASTEXITCODE.`n$($output | Out-String)"
        }

        return @(
            $output |
            Select-Object -Skip 1 |
            ForEach-Object {
                if ($_ -match '^([^\s]+)\s+device$') {
                    $Matches[1]
                }
            }
        )
    }

    # =====================================================================
    # PATHS
    # =====================================================================

    Section 'VALIDANDO NOVORA-LINK REMOTE V2'

    $RepoPath =
        [IO.Path]::GetFullPath(
            $RepoPath)

    Require-Directory $RepoPath

    if ($null -eq (
        Get-Command `
            dotnet `
            -ErrorAction SilentlyContinue
    )) {
        throw 'dotnet no está disponible en PATH. Abre Developer PowerShell/terminal con el SDK .NET instalado.'
    }

    $Solution =
        Join-Path `
            $RepoPath `
            'NOVORA.sln'

    $AndroidProject =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj'

    $PcProtocol =
        Join-Path `
            $RepoPath `
            'src\NOVORA\Remote\ProtocolRemoteNV.cs'

    $PcCommands =
        Join-Path `
            $RepoPath `
            'src\NOVORA\Remote\CommandRemoteNV.cs'

    $PcRouter =
        Join-Path `
            $RepoPath `
            'src\NOVORA\MainWindow.RemoteAndroidNV.cs'

    $AndroidProtocol =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\Remote\ProtocolRemoteNV.cs'

    $AndroidCommands =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\Remote\CommandRemoteNV.cs'

    $MainActivity =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\MainActivity.cs'

    $SettingsActivity =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\SettingsActivityNV.cs'

    $Manifest =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\Properties\AndroidManifest.xml'

    $AppIcon =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\Resources\mipmap-xxxhdpi\appicon.png'

    $TestsProject =
        Join-Path `
            $RepoPath `
            'tests\NOVORA.RemoteV2.Tests\NOVORA.RemoteV2.Tests.csproj'

    $ProtocolTests =
        Join-Path `
            $RepoPath `
            'tests\NOVORA.RemoteV2.Tests\RemoteProtocolV2Tests.cs'

    $SettingsTests =
        Join-Path `
            $RepoPath `
            'tests\NOVORA.RemoteV2.Tests\RemoteSettingsV2Tests.cs'

    Require-File $Solution
    Require-File $AndroidProject
    Require-File $PcProtocol
    Require-File $PcCommands
    Require-File $PcRouter
    Require-File $AndroidProtocol
    Require-File $AndroidCommands
    Require-File $MainActivity
    Require-File $SettingsActivity
    Require-File $Manifest
    Require-File $AppIcon
    Require-File $TestsProject
    Require-File $ProtocolTests
    Require-File $SettingsTests

    # =====================================================================
    # STATIC PRECHECKS
    # =====================================================================

    Section 'PRECHECK FUENTES V2'

    Require-Contains `
        $PcProtocol `
        'ProtocolVersionNV =' `
        'ProtocolRemoteNV PC presente'

    Require-Contains `
        $PcProtocol `
        '        2;' `
        'NOVORA-REMOTE/2 PC'

    Require-Contains `
        $AndroidProtocol `
        '        2;' `
        'NOVORA-REMOTE/2 Android'

    Require-Contains `
        $PcCommands `
        'PrepareLink = 7' `
        'PrepareLink PC'

    Require-Contains `
        $PcCommands `
        'GetSettings = 8' `
        'GetSettings PC'

    Require-Contains `
        $PcCommands `
        'SaveSettings = 9' `
        'SaveSettings PC'

    Require-Contains `
        $AndroidCommands `
        'PrepareLink = 7' `
        'PrepareLink Android'

    Require-Contains `
        $PcRouter `
        'PrepareLinkFromRemoteNVAsync' `
        'MainWindow conectado a PREPARE_LINK'

    Require-Contains `
        $PcRouter `
        'GetSettingsFromRemoteNV' `
        'MainWindow conectado a lectura de SettingsWindow'

    Require-Contains `
        $PcRouter `
        'SaveSettingsFromRemoteNV' `
        'MainWindow conectado a escritura de SettingsWindow'

    Require-Contains `
        $MainActivity `
        'CommandRemoteNV.PrepareLink' `
        'Internet desde PC espera PREPARE_LINK'

    Require-Contains `
        $SettingsActivity `
        'CommandRemoteNV.GetSettings' `
        'Settings Android lee ajustes Windows'

    Require-Contains `
        $SettingsActivity `
        'CommandRemoteNV.SaveSettings' `
        'Settings Android guarda ajustes Windows'

    Require-Contains `
        $SettingsActivity `
        'global::Android.Resource.Layout.SimpleSpinnerItem' `
        'Spinner usa Android.Resource global'

    Require-Contains `
        $Manifest `
        'android:icon="@mipmap/appicon"' `
        'Manifest usa icono NOVORA'

    Require-Contains `
        $Manifest `
        'android:roundIcon="@mipmap/appicon_round"' `
        'Manifest usa adaptive round icon'

    Require-Contains `
        $AndroidProject `
        '<ApplicationVersion>2</ApplicationVersion>' `
        'versionCode 2'

    Require-Contains `
        $AndroidProject `
        '<ApplicationDisplayVersion>1.4.A3</ApplicationDisplayVersion>' `
        'versionName 1.4.A3'

    $mainText =
        [IO.File]::ReadAllText(
            $MainActivity)

    $settingsText =
        [IO.File]::ReadAllText(
            $SettingsActivity)

    if ($mainText.Contains('Gravity = GravityFlags.CenterVertical')) {
        throw 'MainActivity todavía contiene LinearLayout.Gravity de solo lectura.'
    }

    if ($settingsText -match '(?<!global::)Android\.Resource\.Layout\.SimpleSpinner') {
        throw 'SettingsActivity todavía contiene Android.Resource ambiguo.'
    }

    if ($mainText -match '(?<!global::)Android\.Graphics\.' -or
        $settingsText -match '(?<!global::)Android\.Graphics\.') {
        throw 'Quedó una referencia Android.Graphics ambigua en Android UI.'
    }

    Write-Host '[OK] Precheck estático completo.' -ForegroundColor Green

    # =====================================================================
    # CLOSE WINDOWS APP
    # =====================================================================

    Section 'CERRANDO NOVORA PARA BUILD'

    Get-Process `
        -Name 'NOVORA' `
        -ErrorAction SilentlyContinue |
    ForEach-Object {
        try {
            Stop-Process `
                -Id $_.Id `
                -Force `
                -ErrorAction Stop
        }
        catch {
        }
    }

    Start-Sleep `
        -Milliseconds 400

    # =====================================================================
    # FORCE REGEN ANDROID RESOURCES / MANIFEST
    # =====================================================================

    Section 'LIMPIANDO OUTPUT RELEASE ANDROID'

    $AndroidBinRelease =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\bin\Release'

    $AndroidObjRelease =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\obj\Release'

    Remove-Item `
        -LiteralPath $AndroidBinRelease `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue

    Remove-Item `
        -LiteralPath $AndroidObjRelease `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue

    Write-Host '[OK] Android Release limpio; manifest/iconos se regenerarán.' -ForegroundColor Green

    # =====================================================================
    # RESTORE / TEST / BUILD
    # =====================================================================

    Push-Location $RepoPath

    try {
        Section 'DOTNET INFO'

        & dotnet --info

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet --info falló con código $LASTEXITCODE."
        }

        Section 'RESTORE'

        Run-Dotnet @(
            'restore',
            $Solution
        )

        Run-Dotnet @(
            'restore',
            $TestsProject
        )

        Section 'TESTS REMOTE V2'

        Run-Dotnet @(
            'test',
            $TestsProject,
            '-c',
            'Release',
            '--no-restore'
        )

        Section 'BUILD RELEASE WINDOWS + ANDROID'

        Run-Dotnet @(
            'build',
            $Solution,
            '-c',
            'Release',
            '--no-restore'
        )
    }
    finally {
        Pop-Location
    }

    # =====================================================================
    # GENERATED MANIFEST VERIFICATION
    # =====================================================================

    Section 'VERIFICANDO MANIFEST GENERADO'

    $GeneratedManifest =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\obj\Release\net10.0-android\android\AndroidManifest.xml'

    Require-File $GeneratedManifest

    $generatedText =
        [IO.File]::ReadAllText(
            $GeneratedManifest)

    if (-not $generatedText.Contains('android:versionCode="2"')) {
        throw 'El manifest generado no contiene versionCode=2.'
    }

    if (-not $generatedText.Contains('android:versionName="1.4.A3')) {
        throw 'El manifest generado no contiene versionName=1.4.A3.'
    }

    if (-not $generatedText.Contains('android:icon="@mipmap/appicon"')) {
        throw 'El manifest generado no usa @mipmap/appicon.'
    }

    if (-not $generatedText.Contains('android:roundIcon="@mipmap/appicon_round"')) {
        throw 'El manifest generado no usa @mipmap/appicon_round.'
    }

    Write-Host '[OK] Manifest generado: versionCode 2 / versionName 1.4.A3.' -ForegroundColor Green
    Write-Host '[OK] Manifest generado usa icono NOVORA.' -ForegroundColor Green

    # =====================================================================
    # APK
    # =====================================================================

    Section 'LOCALIZANDO APK RELEASE'

    $AndroidBin =
        Join-Path `
            $RepoPath `
            'NOVORA.linkEngine.Android\bin\Release\net10.0-android'

    Require-Directory $AndroidBin

    $SignedApk =
        Get-ChildItem `
            -LiteralPath $AndroidBin `
            -File `
            -Filter 'com.novora.linkengine-Signed.apk' `
            -Recurse `
            -ErrorAction SilentlyContinue |
        Sort-Object `
            LastWriteTime `
            -Descending |
        Select-Object `
            -First 1

    if ($null -eq $SignedApk) {
        throw @"
No encontré el APK firmado esperado:

com.novora.linkengine-Signed.apk

en:

$AndroidBin
"@
    }

    $ToolsApk =
        Join-Path `
            $RepoPath `
            'src\NOVORA\Tools\com.novora.linkengine-Signed.apk'

    Copy-Item `
        -LiteralPath $SignedApk.FullName `
        -Destination $ToolsApk `
        -Force

    $Hash =
        (
            Get-FileHash `
                -LiteralPath $ToolsApk `
                -Algorithm SHA256
        ).Hash

    $ApkSize =
        (
            Get-Item `
                -LiteralPath $ToolsApk
        ).Length

    Write-Host "[OK] APK: $ToolsApk" -ForegroundColor Green
    Write-Host "[OK] SHA256: $Hash" -ForegroundColor Green
    Write-Host "[OK] Size: $ApkSize bytes" -ForegroundColor Green

    # =====================================================================
    # ADB DEPLOY
    # =====================================================================

    Section 'INSTALANDO EN ANDROID'

    $Adb =
        Join-Path `
            $RepoPath `
            'src\NOVORA\Tools\adb.exe'

    Require-File $Adb

    try {
        & $Adb start-server | Out-Null

        if ($LASTEXITCODE -ne 0) {
            throw "adb start-server falló con código $LASTEXITCODE."
        }

        $Serials =
            @(
                Get-ConnectedAdbSerials `
                    -Adb $Adb
            )

        if ($Serials.Count -eq 1) {
            $Serial =
                $Serials[0]

            Write-Host "Android: $Serial" -ForegroundColor Cyan

            & $Adb `
                -s $Serial `
                install `
                -r `
                $ToolsApk

            if ($LASTEXITCODE -ne 0) {
                throw "adb install falló con código $LASTEXITCODE."
            }

            $PackagePath =
                & $Adb `
                    -s $Serial `
                    shell `
                    pm `
                    path `
                    $PackageName

            if (-not (
                ($PackagePath | Out-String).Contains('package:')
            )) {
                throw "PackageManager no confirmó $PackageName."
            }

            $PackageDump =
                & $Adb `
                    -s $Serial `
                    shell `
                    dumpsys `
                    package `
                    $PackageName 2>&1 |
                Out-String

            if ($LASTEXITCODE -ne 0) {
                throw "dumpsys package $PackageName falló con código $LASTEXITCODE."
            }

            if ($PackageDump -notmatch ('versionCode=' + [regex]::Escape($ExpectedVersionCode) + '\b')) {
                throw "Android no reporta versionCode=$ExpectedVersionCode después de instalar."
            }

            if ($PackageDump -notmatch ('versionName=' + [regex]::Escape($ExpectedVersionName) + '\b')) {
                throw "Android no reporta versionName=$ExpectedVersionName después de instalar."
            }

            Write-Host "[OK] Android reporta $ExpectedVersionName / code $ExpectedVersionCode." -ForegroundColor Green

            & $Adb `
                -s $Serial `
                logcat `
                -c |
            Out-Null

            & $Adb `
                -s $Serial `
                shell `
                monkey `
                -p $PackageName `
                -c android.intent.category.LAUNCHER `
                1 |
            Out-Null

            if ($LASTEXITCODE -ne 0) {
                throw 'No pude abrir NOVORA-LINK después de instalar.'
            }

            Start-Sleep `
                -Milliseconds 800

            $AppPidText =
                (
                    & $Adb `
                        -s $Serial `
                        shell `
                        pidof `
                        -s `
                        $PackageName 2>&1 |
                    Out-String
                ).Trim()

            if ([string]::IsNullOrWhiteSpace($AppPidText)) {
                throw 'NOVORA-LINK no tiene PID activo después del launch.'
            }

            $AppPid =
                ($AppPidText -split '\s+')[0]

            $Logcat =
                & $Adb `
                    -s $Serial `
                    logcat `
                    -d `
                    --pid $AppPid 2>&1 |
                Out-String

            if ($Logcat -match '(?im)FATAL EXCEPTION|AndroidRuntime.*FATAL|ANR in com\.novora\.linkengine') {
                throw 'NOVORA-LINK abrió pero logcat detectó FATAL/ANR.'
            }

            Write-Host '[OK] APK instalada, abierta y sin FATAL/ANR inmediato.' -ForegroundColor Green
        }
        elseif ($Serials.Count -eq 0) {
            Write-Host '[WARN] No hay Android ADB conectado. APK generada pero no instalada.' -ForegroundColor Yellow
        }
        else {
            Write-Host '[WARN] Hay varios Android ADB conectados. APK generada pero no se instaló automáticamente.' -ForegroundColor Yellow

            $Serials |
            ForEach-Object {
                Write-Host "  $_" -ForegroundColor Yellow
            }
        }
    }
    catch {
        # El APK ya fue compilado y verificado. Un problema de cable/ADB no
        # invalida el build ni debe provocar rollback de las fuentes.
        Write-Host (
            '[WARN] APK compilada correctamente, pero el despliegue ADB falló: ' +
            $_.Exception.Message
        ) -ForegroundColor Yellow
    }

    # =====================================================================
    # RESTART NOVORA PC
    # =====================================================================

    Section 'REINICIANDO NOVORA PC'

    $NovoraExe =
        Join-Path `
            $RepoPath `
            'src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\NOVORA.exe'

    if (
        Test-Path `
            -LiteralPath $NovoraExe `
            -PathType Leaf
    ) {
        Start-Process `
            -FilePath $NovoraExe `
            -WorkingDirectory (
                Split-Path `
                    -Parent `
                    $NovoraExe)

        Write-Host '[OK] NOVORA PC iniciado.' -ForegroundColor Green
    }
    else {
        Write-Host '[WARN] Build correcto, pero no encontré NOVORA.exe para reiniciarlo.' -ForegroundColor Yellow
    }

    # =====================================================================
    # FINAL
    # =====================================================================

    Section 'FINAL'

    Write-Host 'NOVORA-LINK Android Remote V2 listo.' -ForegroundColor Green
    Write-Host "Version Android: $ExpectedVersionName / versionCode $ExpectedVersionCode" -ForegroundColor Green
    Write-Host "APK: $ToolsApk" -ForegroundColor Green
    Write-Host "SHA256: $Hash" -ForegroundColor Green
}
