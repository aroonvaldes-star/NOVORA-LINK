$ErrorActionPreference = "Stop"

# ============================================================
# NOVORA-LINK
# LINKENGINE GAMEPLAY TRACE V4.1
# ============================================================
#
# ORDEN:
#
# 1. Este script abre NOVORA.
# 2. Tú seleccionas el A56.
# 3. Tú activas LINKENGINE.
# 4. Tú abres VIDEO / scrcpy.
# 5. Cuando aparece scrcpy.exe:
#
#       CRONOMETRO = 00:00.000
#       TRACE START
#
# 6. Juegas.
#
# CONTROLES:
#
#       SPACE = MARCAR TIRON
#       ENTER = TERMINAR Y GUARDAR
#
# MUESTREO:
#
#       Teclado .......... 50 ms
#       Windows .......... 1 s
#       Android / ADB .... 10 s
#
# NO:
#
# - modifica LinkEngine
# - crea adb reverse
# - inicia Gnirehtet
# - modifica NOVORA
#
# ============================================================


# ============================================================
# CONFIGURACION
# ============================================================

$RepoPath =
    "C:\Users\Aroon\Desktop\Novora LinkEngine"

$NovoraBinRoot =
    Join-Path `
        $RepoPath `
        "src\NOVORA\bin"

$AdbPath =
    Join-Path `
        $RepoPath `
        "src\NOVORA\Tools\adb.exe"

$PackageName =
    "com.novora.linkengine"

$ControlPort =
    27183

$DataPort =
    27184

$MetricSampleMilliseconds =
    1000

$KeyboardPollMilliseconds =
    50

$AndroidSampleSeconds =
    10

$MaximumTestMinutes =
    120


# ============================================================
# OUTPUT
# ============================================================

$Timestamp =
    Get-Date -Format "yyyyMMdd-HHmmss"

$DiagnosticRoot =
    Join-Path `
        $RepoPath `
        "Diagnostics\Gameplay"

$SessionPath =
    Join-Path `
        $DiagnosticRoot `
        "Gameplay-$Timestamp"

$GameplayCsv =
    Join-Path `
        $SessionPath `
        "Gameplay.csv"

$EventsCsv =
    Join-Path `
        $SessionPath `
        "Events.csv"

$SummaryPath =
    Join-Path `
        $SessionPath `
        "Summary.txt"

$ConsoleLog =
    Join-Path `
        $SessionPath `
        "Console.log"

New-Item `
    -ItemType Directory `
    -Path $SessionPath `
    -Force |
    Out-Null


# ============================================================
# TRANSCRIPT
# ============================================================

$TranscriptStarted =
    $false

try
{
    Start-Transcript `
        -Path $ConsoleLog `
        -Force |
        Out-Null

    $TranscriptStarted =
        $true
}
catch
{
    $TranscriptStarted =
        $false
}


# ============================================================
# GLOBAL KEYBOARD
# ============================================================

try
{
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NovoraGameplayKeyboard
{
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}
"@ -ErrorAction Stop
}
catch
{
    if (
        $_.Exception.Message -notmatch
        "already exists"
    )
    {
        throw
    }
}

$VK_SPACE =
    0x20

$VK_RETURN =
    0x0D


# ============================================================
# HELPERS
# ============================================================

function Write-Section
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Title
    )

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " $Title" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""
}


function YesNo
{
    param(
        [bool]$Value
    )

    if ($Value)
    {
        return "YES"
    }

    return "NO"
}


function Safe-Adb
{
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    try
    {
        $Output =
            & $AdbPath @Arguments 2>&1

        $ExitCode =
            $LASTEXITCODE

        return [PSCustomObject]@{
            ExitCode = $ExitCode
            Text     = ($Output -join "`n")
            Output   = @($Output)
        }
    }
    catch
    {
        return [PSCustomObject]@{
            ExitCode = -1
            Text     = $_.Exception.Message
            Output   = @()
        }
    }
}


# ============================================================
# NOVORA
# ============================================================

function Get-NovoraProcesses
{
    return @(
        Get-Process `
            -Name "NOVORA" `
            -ErrorAction SilentlyContinue
    )
}


function Find-NovoraExe
{
    $PreferredPaths =
        @(
            (
                Join-Path `
                    $RepoPath `
                    "src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\NOVORA.exe"
            ),

            (
                Join-Path `
                    $RepoPath `
                    "src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\publish\NOVORA.exe"
            ),

            (
                Join-Path `
                    $RepoPath `
                    "src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\publish\win-x64\NOVORA.exe"
            )
        )

    foreach ($Candidate in $PreferredPaths)
    {
        if (
            Test-Path `
                -LiteralPath $Candidate
        )
        {
            return $Candidate
        }
    }

    if (
        -not (
            Test-Path `
                -LiteralPath $NovoraBinRoot
        )
    )
    {
        return $null
    }

    $Candidates =
        @(
            Get-ChildItem `
                -Path $NovoraBinRoot `
                -Filter "NOVORA.exe" `
                -File `
                -Recurse `
                -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -notmatch "\\obj\\"
            } |
            Sort-Object `
                LastWriteTime `
                -Descending
        )

    $ReleaseCandidates =
        @(
            $Candidates |
            Where-Object {
                $_.FullName -match "\\Release\\"
            }
        )

    if ($ReleaseCandidates.Count -gt 0)
    {
        return $ReleaseCandidates[0].FullName
    }

    if ($Candidates.Count -gt 0)
    {
        return $Candidates[0].FullName
    }

    return $null
}


function Start-Novora
{
    $Existing =
        Get-NovoraProcesses

    if ($Existing.Count -gt 0)
    {
        Write-Host "[INFO] NOVORA ya estaba abierto." -ForegroundColor Yellow

        return $Existing[0]
    }

    $NovoraExe =
        Find-NovoraExe

    if (
        [string]::IsNullOrWhiteSpace(
            $NovoraExe
        )
    )
    {
        throw @"
No encontré NOVORA.exe.

Compila NOVORA Release primero.

Ruta buscada:
$NovoraBinRoot
"@
    }

    Write-Host "NOVORA.exe:" -ForegroundColor Green
    Write-Host $NovoraExe
    Write-Host ""

    $WorkingDirectory =
        Split-Path `
            -Parent `
            $NovoraExe

    Start-Process `
        -FilePath $NovoraExe `
        -WorkingDirectory $WorkingDirectory |
        Out-Null

    $Timeout =
        (Get-Date).AddSeconds(
            30
        )

    while (
        (Get-Date) -lt
        $Timeout
    )
    {
        $Current =
            Get-NovoraProcesses

        if ($Current.Count -gt 0)
        {
            return $Current[0]
        }

        Start-Sleep `
            -Milliseconds 250
    }

    throw "NOVORA.exe no apareció después de 30 segundos."
}


# ============================================================
# DEVICE
# ============================================================

function Get-AdbDevices
{
    if (
        -not (
            Test-Path `
                -LiteralPath $AdbPath
        )
    )
    {
        return @()
    }

    $Result =
        Safe-Adb `
            -Arguments @(
                "devices"
            )

    $Devices =
        @()

    foreach ($Line in $Result.Output)
    {
        $Text =
            "$Line".Trim()

        if (
            $Text -match
            "^([^\s]+)\s+device$"
        )
        {
            $Serial =
                $Matches[1]

            $Transport =
                "USB"

            if ($Serial.Contains(":"))
            {
                $Transport =
                    "WIFI"
            }

            $Devices +=
                [PSCustomObject]@{
                    Serial    = $Serial
                    Transport = $Transport
                }
        }
    }

    return @($Devices)
}


function Select-PreferredDevice
{
    $Devices =
        @(Get-AdbDevices)

    if ($Devices.Count -eq 0)
    {
        return $null
    }

    $Usb =
        @(
            $Devices |
            Where-Object {
                $_.Transport -eq "USB"
            }
        )

    if ($Usb.Count -gt 0)
    {
        return $Usb[0]
    }

    return $Devices[0]
}


function Get-AdbProperty
{
    param(
        [string]$Serial,

        [string]$Property
    )

    if (
        [string]::IsNullOrWhiteSpace(
            $Serial
        )
    )
    {
        return ""
    }

    $Result =
        Safe-Adb `
            -Arguments @(
                "-s",
                $Serial,
                "shell",
                "getprop",
                $Property
            )

    return $Result.Text.Trim()
}


function Get-VpnRunning
{
    param(
        [string]$Serial
    )

    if (
        [string]::IsNullOrWhiteSpace(
            $Serial
        )
    )
    {
        return $false
    }

    $Result =
        Safe-Adb `
            -Arguments @(
                "-s",
                $Serial,
                "shell",
                "dumpsys",
                "activity",
                "services",
                $PackageName
            )

    if (
        $Result.Text -match
        "VpnNetworkLE"
    )
    {
        return $true
    }

    return $false
}


function Get-AndroidAppAlive
{
    param(
        [string]$Serial
    )

    if (
        [string]::IsNullOrWhiteSpace(
            $Serial
        )
    )
    {
        return $false
    }

    $Result =
        Safe-Adb `
            -Arguments @(
                "-s",
                $Serial,
                "shell",
                "pidof",
                $PackageName
            )

    if (
        [string]::IsNullOrWhiteSpace(
            $Result.Text.Trim()
        )
    )
    {
        return $false
    }

    return $true
}


# ============================================================
# PORTS
# ============================================================

function Test-PortListening
{
    param(
        [int]$Port
    )

    try
    {
        $Connections =
            @(
                Get-NetTCPConnection `
                    -LocalPort $Port `
                    -State Listen `
                    -ErrorAction SilentlyContinue
            )

        return (
            $Connections.Count -gt 0
        )
    }
    catch
    {
        return $false
    }
}


function Test-PortEstablished
{
    param(
        [int]$Port
    )

    try
    {
        $Connections =
            @(
                Get-NetTCPConnection `
                    -LocalPort $Port `
                    -State Established `
                    -ErrorAction SilentlyContinue
            )

        return (
            $Connections.Count -gt 0
        )
    }
    catch
    {
        return $false
    }
}


# ============================================================
# PROCESSES
# ============================================================

function Get-RelayProcesses
{
    return @(
        Get-Process `
            -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessName -like "*LinkEngine*Relay*" -or
            $_.ProcessName -like "*NOVORA.LinkEngine.Relay*"
        }
    )
}


function Get-ScrcpyProcesses
{
    return @(
        Get-Process `
            -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessName -like "scrcpy*"
        }
    )
}


function Get-AdbProcesses
{
    return @(
        Get-Process `
            -Name "adb" `
            -ErrorAction SilentlyContinue
    )
}


# ============================================================
# PROCESS METRICS
# ============================================================

$LogicalProcessorCount =
    [Environment]::ProcessorCount

$PreviousProcessCpu =
    @{}

$PreviousProcessCpuTime =
    @{}


function Get-ProcessMetrics
{
    param(
        [object[]]$Processes,

        [string]$GroupName
    )

    $CpuTotal =
        0.0

    $MemoryTotal =
        0.0

    $ThreadTotal =
        0

    $Now =
        Get-Date

    foreach ($Process in @($Processes))
    {
        if ($null -eq $Process)
        {
            continue
        }

        try
        {
            $Process.Refresh()

            $Key =
                "$GroupName-$($Process.Id)"

            $CurrentCpuSeconds =
                $Process.TotalProcessorTime.TotalSeconds

            if (
                $PreviousProcessCpu.ContainsKey($Key) -and
                $PreviousProcessCpuTime.ContainsKey($Key)
            )
            {
                $PreviousCpuSeconds =
                    [double]$PreviousProcessCpu[$Key]

                $PreviousTimestamp =
                    [DateTime]$PreviousProcessCpuTime[$Key]

                $CpuDelta =
                    $CurrentCpuSeconds -
                    $PreviousCpuSeconds

                $TimeDelta =
                    (
                        $Now -
                        $PreviousTimestamp
                    ).TotalSeconds

                if ($TimeDelta -gt 0)
                {
                    $CpuPercent =
                        (
                            $CpuDelta /
                            $TimeDelta /
                            $LogicalProcessorCount
                        ) *
                        100.0

                    if ($CpuPercent -lt 0)
                    {
                        $CpuPercent =
                            0.0
                    }

                    $CpuTotal +=
                        $CpuPercent
                }
            }

            $PreviousProcessCpu[$Key] =
                $CurrentCpuSeconds

            $PreviousProcessCpuTime[$Key] =
                $Now

            $MemoryTotal +=
                (
                    $Process.WorkingSet64 /
                    1MB
                )

            $ThreadTotal +=
                $Process.Threads.Count
        }
        catch
        {
            # El proceso pudo terminar entre Get-Process y Refresh.
        }
    }

    return [PSCustomObject]@{
        Cpu =
            [Math]::Round(
                $CpuTotal,
                2
            )

        Memory =
            [Math]::Round(
                $MemoryTotal,
                2
            )

        Threads =
            $ThreadTotal
    }
}


# ============================================================
# SYSTEM CPU / DPC
# ============================================================

$SystemCpuCounter =
    $null

$DpcCounter =
    $null

try
{
    $SystemCpuCounter =
        New-Object `
            System.Diagnostics.PerformanceCounter(
                "Processor",
                "% Processor Time",
                "_Total"
            )

    [void]$SystemCpuCounter.NextValue()
}
catch
{
    $SystemCpuCounter =
        $null
}


try
{
    $DpcCounter =
        New-Object `
            System.Diagnostics.PerformanceCounter(
                "Processor",
                "% DPC Time",
                "_Total"
            )

    [void]$DpcCounter.NextValue()
}
catch
{
    $DpcCounter =
        $null
}


function Get-SystemCpu
{
    if ($null -eq $SystemCpuCounter)
    {
        return 0.0
    }

    try
    {
        return [Math]::Round(
            $SystemCpuCounter.NextValue(),
            2
        )
    }
    catch
    {
        return 0.0
    }
}


function Get-Dpc
{
    if ($null -eq $DpcCounter)
    {
        return 0.0
    }

    try
    {
        return [Math]::Round(
            $DpcCounter.NextValue(),
            2
        )
    }
    catch
    {
        return 0.0
    }
}


# ============================================================
# NETWORK ADAPTER
# ============================================================

function Get-DefaultAdapter
{
    try
    {
        $Routes =
            @(
                Get-NetRoute `
                    -DestinationPrefix "0.0.0.0/0" `
                    -ErrorAction Stop |
                Sort-Object `
                    RouteMetric,
                    InterfaceMetric
            )

        if ($Routes.Count -eq 0)
        {
            return $null
        }

        $Route =
            $Routes[0]

        $Adapter =
            Get-NetAdapter `
                -InterfaceIndex $Route.InterfaceIndex `
                -ErrorAction SilentlyContinue

        return $Adapter
    }
    catch
    {
        return $null
    }
}


function Get-NetworkBytes
{
    param(
        [int]$InterfaceIndex
    )

    try
    {
        $Stats =
            Get-NetAdapterStatistics `
                -InterfaceIndex $InterfaceIndex `
                -ErrorAction Stop

        return [PSCustomObject]@{
            Rx =
                [double]$Stats.ReceivedBytes

            Tx =
                [double]$Stats.SentBytes
        }
    }
    catch
    {
        return [PSCustomObject]@{
            Rx = 0.0
            Tx = 0.0
        }
    }
}


# ============================================================
# EVENTS
# ============================================================

'"Timestamp","ElapsedSeconds","Type","Reason"' |
    Set-Content `
        -LiteralPath $EventsCsv `
        -Encoding UTF8


function Add-TraceEvent
{
    param(
        [string]$Type,

        [string]$Reason,

        [double]$ElapsedSeconds
    )

    $SafeReason =
        $Reason.Replace(
            '"',
            '""'
        )

    $Line =
        '"{0}","{1}","{2}","{3}"' -f `
            (Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"),
            ([Math]::Round($ElapsedSeconds, 3)),
            $Type,
            $SafeReason

    Add-Content `
        -LiteralPath $EventsCsv `
        -Value $Line `
        -Encoding UTF8
}


# ============================================================
# GAMEPLAY CSV HEADER
# ============================================================

$GameplayHeader =
    @(
        '"Timestamp"',
        '"ElapsedSeconds"',
        '"SystemCpuPercent"',
        '"DpcPercent"',
        '"NovoraCpuPercent"',
        '"NovoraMemoryMB"',
        '"NovoraThreads"',
        '"RelayCpuPercent"',
        '"RelayMemoryMB"',
        '"RelayThreads"',
        '"ScrcpyCpuPercent"',
        '"ScrcpyMemoryMB"',
        '"ScrcpyThreads"',
        '"AdbCpuPercent"',
        '"AdbMemoryMB"',
        '"AdbThreads"',
        '"ControlListen"',
        '"ControlConnected"',
        '"VpnRunning"',
        '"DataListen"',
        '"DataConnected"',
        '"AndroidApp"',
        '"RelayRunning"',
        '"ScrcpyRunning"',
        '"RxMbps"',
        '"TxMbps"',
        '"ManualStutters"'
    ) -join ","

$GameplayHeader |
    Set-Content `
        -LiteralPath $GameplayCsv `
        -Encoding UTF8


# ============================================================
# MAIN
# ============================================================

$TestStart =
    $null

$TestEnd =
    $null

$StopReason =
    "UNKNOWN"

$Samples =
    0

$HealthySamples =
    0

$ManualStutters =
    0

$ControlBadSamples =
    0

$VpnBadSamples =
    0

$DataBadSamples =
    0

$RelayBadSamples =
    0

$ScrcpyBadSamples =
    0

$MaxSystemCpu =
    0.0

$MaxDpc =
    0.0

$MaxNovoraCpu =
    0.0

$MaxRelayCpu =
    0.0

$MaxScrcpyCpu =
    0.0

$MaxRxMbps =
    0.0

$MaxTxMbps =
    0.0

$Model =
    ""

$Serial =
    ""

$InitialControl =
    $false

$InitialVpn =
    $false

$InitialData =
    $false

$InitialRelay =
    $false

$InitialApp =
    $false


try
{
    Clear-Host

    Write-Section "NOVORA-LINK - LINKENGINE GAMEPLAY TRACE V4.1"

    Write-Host "FLUJO:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. Este script abre NOVORA."
    Write-Host "2. Activa LINKENGINE."
    Write-Host "3. Abre VIDEO."
    Write-Host "4. Al detectar scrcpy.exe comienza 00:00."
    Write-Host ""
    Write-Host "SPACE = MARCAR TIRON" -ForegroundColor Magenta
    Write-Host "ENTER = TERMINAR Y GUARDAR" -ForegroundColor Green
    Write-Host ""


    # ========================================================
    # ADB
    # ========================================================

    if (
        -not (
            Test-Path `
                -LiteralPath $AdbPath
        )
    )
    {
        throw "No existe adb.exe: $AdbPath"
    }

    & $AdbPath start-server |
        Out-Null


    # ========================================================
    # ABRIR NOVORA
    # ========================================================

    Write-Section "ABRIENDO NOVORA"

    $NovoraProcess =
        Start-Novora

    Write-Host "[OK] NOVORA abierto." -ForegroundColor Green
    Write-Host "PID ................. $($NovoraProcess.Id)"
    Write-Host ""


    # ========================================================
    # ESPERAR SCRCPY
    # ========================================================

    Write-Section "ACTIVA LINKENGINE Y DESPUES VIDEO"

    Write-Host "En NOVORA:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. Selecciona A56."
    Write-Host "2. START LINKENGINE."
    Write-Host "3. Espera que LinkEngine tenga Internet."
    Write-Host "4. Abre VIDEO."
    Write-Host ""
    Write-Host "NO se mide tiempo todavía." -ForegroundColor Cyan
    Write-Host "El cronómetro comienza al detectar scrcpy.exe." -ForegroundColor Cyan
    Write-Host ""

    $Device =
        $null

    $LastDeviceCheck =
        [DateTime]::MinValue

    $LastPreStartAndroidCheck =
        [DateTime]::MinValue

    $PreStartVpn =
        $false

    $PreStartApp =
        $false

    while ($true)
    {
        $CurrentNovora =
            @(Get-NovoraProcesses)

        if ($CurrentNovora.Count -eq 0)
        {
            throw "NOVORA se cerró antes de iniciar la prueba."
        }

        $Now =
            Get-Date

        if (
            (
                $Now -
                $LastDeviceCheck
            ).TotalSeconds -ge
            3
        )
        {
            $LastDeviceCheck =
                $Now

            $CandidateDevice =
                Select-PreferredDevice

            if ($null -ne $CandidateDevice)
            {
                if (
                    $Serial -ne
                    $CandidateDevice.Serial
                )
                {
                    $Device =
                        $CandidateDevice

                    $Serial =
                        $Device.Serial

                    $Model =
                        Get-AdbProperty `
                            -Serial $Serial `
                            -Property "ro.product.model"

                    Write-Host ""
                    Write-Host "[DEVICE] $Model / $($Device.Transport)" -ForegroundColor Cyan
                }
            }
        }

        if (
            -not [string]::IsNullOrWhiteSpace(
                $Serial
            )
        )
        {
            if (
                (
                    $Now -
                    $LastPreStartAndroidCheck
                ).TotalSeconds -ge
                5
            )
            {
                $LastPreStartAndroidCheck =
                    $Now

                $PreStartVpn =
                    Get-VpnRunning `
                        -Serial $Serial

                $PreStartApp =
                    Get-AndroidAppAlive `
                        -Serial $Serial
            }
        }

        $PreControl =
            Test-PortEstablished `
                -Port $ControlPort

        $PreData =
            Test-PortEstablished `
                -Port $DataPort

        $PreRelay =
            (
                @(Get-RelayProcesses).Count -gt 0
            )

        $ScrcpyProcesses =
            @(Get-ScrcpyProcesses)

        if ($ScrcpyProcesses.Count -gt 0)
        {
            break
        }

        $WaitStatus =
            "Esperando scrcpy | LINKENGINE -> C:{0} V:{1} D:{2} R:{3}" -f `
                (YesNo $PreControl),
                (YesNo $PreStartVpn),
                (YesNo $PreData),
                (YesNo $PreRelay)

        Write-Host `
            "`r$WaitStatus                    " `
            -NoNewline `
            -ForegroundColor Yellow

        Start-Sleep `
            -Milliseconds 250
    }


    # ========================================================
    # SCRCPY DETECTADO = 00:00
    # ========================================================

    $TestStart =
        Get-Date

    $TestLimit =
        $TestStart.AddMinutes(
            $MaximumTestMinutes
        )

    Write-Host ""
    Write-Host ""

    Write-Section "TRACE START"

    Write-Host "scrcpy.exe detectado." -ForegroundColor Green
    Write-Host ""
    Write-Host "CRONOMETRO .......... 00:00.000" -ForegroundColor Green
    Write-Host ""


    # ========================================================
    # ESTADO INICIAL
    # ========================================================

    $InitialControl =
        Test-PortEstablished `
            -Port $ControlPort

    $InitialData =
        Test-PortEstablished `
            -Port $DataPort

    $InitialRelay =
        (
            @(Get-RelayProcesses).Count -gt 0
        )

    if (
        -not [string]::IsNullOrWhiteSpace(
            $Serial
        )
    )
    {
        $InitialVpn =
            Get-VpnRunning `
                -Serial $Serial

        $InitialApp =
            Get-AndroidAppAlive `
                -Serial $Serial
    }

    Write-Host "CONTROL ............. $(YesNo $InitialControl)"
    Write-Host "VPN ................. $(YesNo $InitialVpn)"
    Write-Host "DATA ................ $(YesNo $InitialData)"
    Write-Host "RELAY ............... $(YesNo $InitialRelay)"
    Write-Host "ANDROID APP ......... $(YesNo $InitialApp)"
    Write-Host "SCRCPY .............. YES"
    Write-Host ""

    Add-TraceEvent `
        -Type "TRACE_START" `
        -Reason "scrcpy.exe detectado. Inicio cronómetro." `
        -ElapsedSeconds 0

    $InitialReason =
        "CTRL={0}; VPN={1}; DATA={2}; RELAY={3}; APP={4}" -f `
            $InitialControl,
            $InitialVpn,
            $InitialData,
            $InitialRelay,
            $InitialApp

    Add-TraceEvent `
        -Type "INITIAL_STATE" `
        -Reason $InitialReason `
        -ElapsedSeconds 0

    Write-Host "YA PUEDES JUGAR." -ForegroundColor Green
    Write-Host ""
    Write-Host "SPACE = marcar tirón" -ForegroundColor Magenta
    Write-Host "ENTER = finalizar + guardar" -ForegroundColor Green
    Write-Host ""


    # ========================================================
    # NETWORK BASELINE
    # ========================================================

    $Adapter =
        Get-DefaultAdapter

    $PreviousNetworkBytes =
        $null

    $PreviousNetworkTime =
        Get-Date

    if ($null -ne $Adapter)
    {
        $PreviousNetworkBytes =
            Get-NetworkBytes `
                -InterfaceIndex $Adapter.InterfaceIndex

        Write-Host "Internet Windows .... $($Adapter.Name)"
        Write-Host ""
    }


    # ========================================================
    # TRACE STATE
    # ========================================================

    $LastMetricSample =
        [DateTime]::MinValue

    $LastAndroidSample =
        [DateTime]::MinValue

    $CachedVpn =
        $InitialVpn

    $CachedAndroidApp =
        $InitialApp

    $PreviousSpaceDown =
        $false

    $PreviousEnterDown =
        $false

    $PreviousControl =
        $InitialControl

    $PreviousData =
        $InitialData

    $PreviousVpn =
        $InitialVpn

    $PreviousRelay =
        $InitialRelay

    $PreviousScrcpy =
        $true


    # ========================================================
    # TRACE LOOP
    # ========================================================

    while (
        (Get-Date) -lt
        $TestLimit
    )
    {
        $Now =
            Get-Date

        $ElapsedSeconds =
            (
                $Now -
                $TestStart
            ).TotalSeconds


        # ====================================================
        # KEYBOARD - 50 ms
        # ====================================================

        $SpaceState =
            [NovoraGameplayKeyboard]::GetAsyncKeyState(
                $VK_SPACE
            )

        $SpaceDown =
            (
                $SpaceState -band
                0x8000
            ) -ne 0


        $EnterState =
            [NovoraGameplayKeyboard]::GetAsyncKeyState(
                $VK_RETURN
            )

        $EnterDown =
            (
                $EnterState -band
                0x8000
            ) -ne 0


        # ====================================================
        # SPACE = EDGE ONLY
        # ====================================================

        if (
            $SpaceDown -and
            -not $PreviousSpaceDown
        )
        {
            $ManualStutters++

            Add-TraceEvent `
                -Type "MANUAL_STUTTER" `
                -Reason "Tirón marcado con SPACE." `
                -ElapsedSeconds $ElapsedSeconds

            $MarkerTime =
                (
                    [TimeSpan]::FromSeconds(
                        $ElapsedSeconds
                    )
                ).ToString(
                    "hh\:mm\:ss\.fff"
                )

            Write-Host ""
            Write-Host ""

            Write-Host (
                "[SPACE] TIRON #{0} @ {1}" -f `
                    $ManualStutters,
                    $MarkerTime
            ) `
                -ForegroundColor Magenta
        }


        # ====================================================
        # ENTER = FIN
        # ====================================================

        if (
            $EnterDown -and
            -not $PreviousEnterDown
        )
        {
            $StopReason =
                "ENTER"

            Add-TraceEvent `
                -Type "TEST_STOP" `
                -Reason "Prueba terminada con ENTER." `
                -ElapsedSeconds $ElapsedSeconds

            Write-Host ""
            Write-Host ""
            Write-Host "[ENTER] FINALIZANDO Y GUARDANDO..." -ForegroundColor Green

            break
        }


        $PreviousSpaceDown =
            $SpaceDown

        $PreviousEnterDown =
            $EnterDown


        # ====================================================
        # ¿TOCA MUESTRA?
        # ====================================================

        $MetricAge =
            (
                $Now -
                $LastMetricSample
            ).TotalMilliseconds

        if (
            $MetricAge -ge
            $MetricSampleMilliseconds
        )
        {
            $LastMetricSample =
                $Now


            # =================================================
            # PROCESSES
            # =================================================

            $NovoraProcesses =
                @(Get-NovoraProcesses)

            if ($NovoraProcesses.Count -eq 0)
            {
                $StopReason =
                    "NOVORA_CLOSED"

                Add-TraceEvent `
                    -Type "NOVORA_CLOSED" `
                    -Reason "NOVORA.exe terminó." `
                    -ElapsedSeconds $ElapsedSeconds

                break
            }


            $RelayProcesses =
                @(Get-RelayProcesses)

            $ScrcpyProcesses =
                @(Get-ScrcpyProcesses)

            $AdbProcesses =
                @(Get-AdbProcesses)

            $RelayRunning =
                (
                    $RelayProcesses.Count -gt 0
                )

            $ScrcpyRunning =
                (
                    $ScrcpyProcesses.Count -gt 0
                )


            # =================================================
            # PORTS
            # =================================================

            $ControlListen =
                Test-PortListening `
                    -Port $ControlPort

            $ControlConnected =
                Test-PortEstablished `
                    -Port $ControlPort

            $DataListen =
                Test-PortListening `
                    -Port $DataPort

            $DataConnected =
                Test-PortEstablished `
                    -Port $DataPort


            # =================================================
            # ANDROID - 10 sec
            # =================================================

            if (
                -not [string]::IsNullOrWhiteSpace(
                    $Serial
                )
            )
            {
                $AndroidAge =
                    (
                        $Now -
                        $LastAndroidSample
                    ).TotalSeconds

                if (
                    $AndroidAge -ge
                    $AndroidSampleSeconds
                )
                {
                    $LastAndroidSample =
                        $Now

                    $CachedVpn =
                        Get-VpnRunning `
                            -Serial $Serial

                    $CachedAndroidApp =
                        Get-AndroidAppAlive `
                            -Serial $Serial
                }
            }


            # =================================================
            # PROCESS METRICS
            # =================================================

            $NovoraMetrics =
                Get-ProcessMetrics `
                    -Processes $NovoraProcesses `
                    -GroupName "NOVORA"

            $RelayMetrics =
                Get-ProcessMetrics `
                    -Processes $RelayProcesses `
                    -GroupName "RELAY"

            $ScrcpyMetrics =
                Get-ProcessMetrics `
                    -Processes $ScrcpyProcesses `
                    -GroupName "SCRCPY"

            $AdbMetrics =
                Get-ProcessMetrics `
                    -Processes $AdbProcesses `
                    -GroupName "ADB"


            # =================================================
            # SYSTEM
            # =================================================

            $SystemCpu =
                Get-SystemCpu

            $Dpc =
                Get-Dpc


            # =================================================
            # NETWORK
            # =================================================

            $RxMbps =
                0.0

            $TxMbps =
                0.0

            if (
                $null -ne $Adapter -and
                $null -ne $PreviousNetworkBytes
            )
            {
                $CurrentNetworkBytes =
                    Get-NetworkBytes `
                        -InterfaceIndex $Adapter.InterfaceIndex

                $NetworkDeltaSeconds =
                    (
                        $Now -
                        $PreviousNetworkTime
                    ).TotalSeconds

                if (
                    $NetworkDeltaSeconds -gt 0
                )
                {
                    $RxDelta =
                        $CurrentNetworkBytes.Rx -
                        $PreviousNetworkBytes.Rx

                    $TxDelta =
                        $CurrentNetworkBytes.Tx -
                        $PreviousNetworkBytes.Tx

                    if ($RxDelta -lt 0)
                    {
                        $RxDelta =
                            0
                    }

                    if ($TxDelta -lt 0)
                    {
                        $TxDelta =
                            0
                    }

                    $RxMbps =
                        (
                            $RxDelta *
                            8.0 /
                            $NetworkDeltaSeconds /
                            1000000.0
                        )

                    $TxMbps =
                        (
                            $TxDelta *
                            8.0 /
                            $NetworkDeltaSeconds /
                            1000000.0
                        )

                    $RxMbps =
                        [Math]::Round(
                            $RxMbps,
                            3
                        )

                    $TxMbps =
                        [Math]::Round(
                            $TxMbps,
                            3
                        )
                }

                $PreviousNetworkBytes =
                    $CurrentNetworkBytes

                $PreviousNetworkTime =
                    $Now
            }


            # =================================================
            # HEALTH
            # =================================================

            $Healthy =
                $ControlConnected -and
                $DataConnected -and
                $CachedVpn -and
                $CachedAndroidApp -and
                $RelayRunning -and
                $ScrcpyRunning

            $Samples++

            if ($Healthy)
            {
                $HealthySamples++
            }

            if (-not $ControlConnected)
            {
                $ControlBadSamples++
            }

            if (-not $CachedVpn)
            {
                $VpnBadSamples++
            }

            if (-not $DataConnected)
            {
                $DataBadSamples++
            }

            if (-not $RelayRunning)
            {
                $RelayBadSamples++
            }

            if (-not $ScrcpyRunning)
            {
                $ScrcpyBadSamples++
            }


            # =================================================
            # TRANSITIONS
            # =================================================

            if (
                -not $ControlConnected -and
                $PreviousControl
            )
            {
                Add-TraceEvent `
                    -Type "CONTROL_DROP" `
                    -Reason "CONTROL perdió Established." `
                    -ElapsedSeconds $ElapsedSeconds
            }


            if (
                -not $DataConnected -and
                $PreviousData
            )
            {
                Add-TraceEvent `
                    -Type "DATA_DROP" `
                    -Reason "DATA perdió Established." `
                    -ElapsedSeconds $ElapsedSeconds
            }


            if (
                -not $CachedVpn -and
                $PreviousVpn
            )
            {
                Add-TraceEvent `
                    -Type "VPN_DROP" `
                    -Reason "VPN dejó de aparecer activa." `
                    -ElapsedSeconds $ElapsedSeconds
            }


            if (
                -not $RelayRunning -and
                $PreviousRelay
            )
            {
                Add-TraceEvent `
                    -Type "RELAY_DROP" `
                    -Reason "Relay terminó." `
                    -ElapsedSeconds $ElapsedSeconds
            }


            if (
                -not $ScrcpyRunning -and
                $PreviousScrcpy
            )
            {
                Add-TraceEvent `
                    -Type "SCRCPY_DROP" `
                    -Reason "scrcpy.exe terminó." `
                    -ElapsedSeconds $ElapsedSeconds
            }


            $PreviousControl =
                $ControlConnected

            $PreviousData =
                $DataConnected

            $PreviousVpn =
                $CachedVpn

            $PreviousRelay =
                $RelayRunning

            $PreviousScrcpy =
                $ScrcpyRunning


            # =================================================
            # MAXIMUMS
            # =================================================

            if (
                $SystemCpu -gt
                $MaxSystemCpu
            )
            {
                $MaxSystemCpu =
                    $SystemCpu
            }

            if (
                $Dpc -gt
                $MaxDpc
            )
            {
                $MaxDpc =
                    $Dpc
            }

            if (
                $NovoraMetrics.Cpu -gt
                $MaxNovoraCpu
            )
            {
                $MaxNovoraCpu =
                    $NovoraMetrics.Cpu
            }

            if (
                $RelayMetrics.Cpu -gt
                $MaxRelayCpu
            )
            {
                $MaxRelayCpu =
                    $RelayMetrics.Cpu
            }

            if (
                $ScrcpyMetrics.Cpu -gt
                $MaxScrcpyCpu
            )
            {
                $MaxScrcpyCpu =
                    $ScrcpyMetrics.Cpu
            }

            if (
                $RxMbps -gt
                $MaxRxMbps
            )
            {
                $MaxRxMbps =
                    $RxMbps
            }

            if (
                $TxMbps -gt
                $MaxTxMbps
            )
            {
                $MaxTxMbps =
                    $TxMbps
            }


            # =================================================
            # CSV
            # =================================================

            $CsvLine =
                @(
                    "`"$(
                        $Now.ToString(
                            "yyyy-MM-dd HH:mm:ss.fff"
                        )
                    )`"",

                    [Math]::Round(
                        $ElapsedSeconds,
                        3
                    ),

                    $SystemCpu,
                    $Dpc,

                    $NovoraMetrics.Cpu,
                    $NovoraMetrics.Memory,
                    $NovoraMetrics.Threads,

                    $RelayMetrics.Cpu,
                    $RelayMetrics.Memory,
                    $RelayMetrics.Threads,

                    $ScrcpyMetrics.Cpu,
                    $ScrcpyMetrics.Memory,
                    $ScrcpyMetrics.Threads,

                    $AdbMetrics.Cpu,
                    $AdbMetrics.Memory,
                    $AdbMetrics.Threads,

                    $ControlListen,
                    $ControlConnected,

                    $CachedVpn,

                    $DataListen,
                    $DataConnected,

                    $CachedAndroidApp,
                    $RelayRunning,
                    $ScrcpyRunning,

                    $RxMbps,
                    $TxMbps,

                    $ManualStutters
                ) -join ","

            Add-Content `
                -LiteralPath $GameplayCsv `
                -Value $CsvLine `
                -Encoding UTF8


            # =================================================
            # STATUS COLOR
            #
            # ESTE ERA EL ERROR DEL SCRIPT ANTERIOR.
            #
            # NO ponemos:
            #
            # -ForegroundColor (if (...) {...})
            #
            # Windows PowerShell 5.1 no lo acepta.
            # =================================================

            $StatusColor =
                "Green"

            if (-not $Healthy)
            {
                $StatusColor =
                    "Yellow"
            }


            # =================================================
            # DISPLAY
            # =================================================

            $DisplayTime =
                (
                    [TimeSpan]::FromSeconds(
                        $ElapsedSeconds
                    )
                ).ToString(
                    "hh\:mm\:ss"
                )

            $StatusLine =
                "{0} | SYS:{1,5:N1}% DPC:{2,4:N1}% | NOV:{3,5:N1}% LE:{4,5:N1}% SCR:{5,5:N1}% | RX:{6,6:N2} TX:{7,6:N2} | C:{8} V:{9} D:{10} R:{11} | TIRON:{12}" -f `
                    $DisplayTime,
                    $SystemCpu,
                    $Dpc,
                    $NovoraMetrics.Cpu,
                    $RelayMetrics.Cpu,
                    $ScrcpyMetrics.Cpu,
                    $RxMbps,
                    $TxMbps,
                    (YesNo $ControlConnected),
                    (YesNo $CachedVpn),
                    (YesNo $DataConnected),
                    (YesNo $RelayRunning),
                    $ManualStutters

            Write-Host `
                "`r$StatusLine                    " `
                -NoNewline `
                -ForegroundColor $StatusColor
        }


        # ====================================================
        # 50 ms KEYBOARD POLL
        # ====================================================

        Start-Sleep `
            -Milliseconds $KeyboardPollMilliseconds
    }


    # ========================================================
    # FINAL TIME
    # ========================================================

    if ($StopReason -eq "UNKNOWN")
    {
        $StopReason =
            "MAXIMUM_TIME"
    }

    $TestEnd =
        Get-Date


    # ========================================================
    # SUMMARY
    # ========================================================

    $Duration =
        $TestEnd -
        $TestStart

    $Availability =
        0.0

    if ($Samples -gt 0)
    {
        $Availability =
            (
                $HealthySamples /
                $Samples
            ) *
                100.0

        $Availability =
            [Math]::Round(
                $Availability,
                3
            )
    }


    $Summary =
@"
============================================================
 NOVORA-LINK
 LINKENGINE GAMEPLAY TRACE V4.1
============================================================

DISPARADOR

Inicio de cronómetro ... scrcpy.exe detectado

TIEMPO

Inicio ................. $($TestStart.ToString("yyyy-MM-dd HH:mm:ss.fff"))
Fin .................... $($TestEnd.ToString("yyyy-MM-dd HH:mm:ss.fff"))
Duración ................ $Duration
Finalización ............ $StopReason

DEVICE

Modelo .................. $Model
Serial .................. $Serial

MUESTREO

Samples ................. $Samples
Healthy ................. $HealthySamples
Disponibilidad .......... $Availability %

TIRONES

Marcados con SPACE ...... $ManualStutters

MUESTRAS DEGRADADAS

CONTROL ................. $ControlBadSamples
VPN ..................... $VpnBadSamples
DATA .................... $DataBadSamples
Relay ................... $RelayBadSamples
scrcpy .................. $ScrcpyBadSamples

CPU MAX

System .................. $([Math]::Round($MaxSystemCpu, 2)) %
DPC ..................... $([Math]::Round($MaxDpc, 2)) %

NOVORA .................. $([Math]::Round($MaxNovoraCpu, 2)) %
LinkEngine Relay ........ $([Math]::Round($MaxRelayCpu, 2)) %
scrcpy .................. $([Math]::Round($MaxScrcpyCpu, 2)) %

TRAFICO WINDOWS MAX

RX ...................... $([Math]::Round($MaxRxMbps, 3)) Mbps
TX ...................... $([Math]::Round($MaxTxMbps, 3)) Mbps

LINKENGINE AL INICIAR CRONOMETRO

CONTROL ................. $(YesNo $InitialControl)
VPN ..................... $(YesNo $InitialVpn)
DATA .................... $(YesNo $InitialData)
Relay ................... $(YesNo $InitialRelay)
Android App ............. $(YesNo $InitialApp)

ARCHIVOS

Gameplay:
$GameplayCsv

Events:
$EventsCsv

Summary:
$SummaryPath

Console:
$ConsoleLog

============================================================
"@

    $Summary |
        Set-Content `
            -LiteralPath $SummaryPath `
            -Encoding UTF8


    Write-Host ""
    Write-Host ""

    Write-Section "RESULTADOS"

    Write-Host $Summary

    Write-Host ""
    Write-Host "GUARDADO EN:" -ForegroundColor Green
    Write-Host $SessionPath
    Write-Host ""
}
catch
{
    Write-Host ""
    Write-Host ""

    Write-Section "ERROR DEL SCRIPT"

    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""

    if (
        -not [string]::IsNullOrWhiteSpace(
            $_.ScriptStackTrace
        )
    )
    {
        Write-Host "Stack:" -ForegroundColor Yellow
        Write-Host $_.ScriptStackTrace
    }

    Write-Host ""
}
finally
{
    try
    {
        if ($null -ne $SystemCpuCounter)
        {
            $SystemCpuCounter.Dispose()
        }
    }
    catch
    {
    }

    try
    {
        if ($null -ne $DpcCounter)
        {
            $DpcCounter.Dispose()
        }
    }
    catch
    {
    }

    if ($TranscriptStarted)
    {
        try
        {
            Stop-Transcript |
                Out-Null
        }
        catch
        {
        }
    }

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " TRACE CERRADO" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""

    Write-Host "Resultados:"
    Write-Host $SessionPath
    Write-Host ""
}