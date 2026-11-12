[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WindowsProject = Join-Path $RepoRoot 'src\WINDOWS\NOVORA.csproj'
$AndroidProject = Join-Path $RepoRoot 'NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj'

$Expected = @(
    'src\WINDOWS\LinkEngine\Core\StatesCoreLE.cs',
    'src\WINDOWS\LinkEngine\Core\ResultCoreLE.cs',
    'src\WINDOWS\LinkEngine\Core\StatusCoreLE.cs',
    'src\WINDOWS\LinkEngine\Core\EngineCoreLE.cs',
    'src\WINDOWS\LinkEngine\Device\ConnectionDeviceLE.cs',
    'src\WINDOWS\LinkEngine\Device\ManagerDeviceLE.cs',
    'src\WINDOWS\LinkEngine\Device\PollingDeviceLE.cs',
    'src\WINDOWS\LinkEngine\Device\ProvisioningDeviceLE.cs',
    'src\WINDOWS\LinkEngine\Device\SessionDeviceLE.cs',
    'src\WINDOWS\LinkEngine\Device\StateDeviceLE.cs',
    'src\WINDOWS\LinkEngine\Metrics\CollectorMetricsLE.cs',
    'src\WINDOWS\LinkEngine\Network\DnsNetworkLE.cs',
    'src\WINDOWS\LinkEngine\Network\ManagerNetworkLE.cs',
    'src\WINDOWS\LinkEngine\Network\RelayNetworkLE.cs',
    'src\WINDOWS\LinkEngine\Network\SessionNetworkLE.cs',
    'src\WINDOWS\LinkEngine\Network\StateNetworkLE.cs',
    'src\WINDOWS\LinkEngine\Protocol\HeartbeatProtocolLE.cs',
    'src\WINDOWS\LinkEngine\Recovery\ManagerRecoveryLE.cs',
    'src\WINDOWS\LinkEngine\Recovery\MonitorRecoveryLE.cs',
    'src\WINDOWS\LinkEngine\Recovery\PolicyRecoveryLE.cs',
    'src\WINDOWS\LinkEngine\Recovery\ResultRecoveryLE.cs',
    'src\WINDOWS\LinkEngine\Recovery\StateRecoveryLE.cs',
    'src\WINDOWS\LinkEngine\Runtime\ManagerRuntimeLE.cs',
    'src\WINDOWS\LinkEngine\Runtime\SessionRuntimeLE.cs',
    'src\WINDOWS\LinkEngine\Runtime\StateRuntimeLE.cs',
    'src\WINDOWS\LinkEngine\Transport\DataTransportLE.cs',
    'src\WINDOWS\LinkEngine\Transport\HandshakeTransportLE.cs',
    'src\WINDOWS\LinkEngine\Transport\ListenerTransportLE.cs',
    'src\WINDOWS\LinkEngine\Transport\ManagerTransportLE.cs',
    'src\WINDOWS\LinkEngine\Transport\PortTransportLE.cs',
    'src\WINDOWS\LinkEngine\Transport\SessionTransportLE.cs',
    'src\WINDOWS\LinkEngine\Transport\StateTransportLE.cs',
    'src\WINDOWS\VisionEngine\Core\StatesCoreVE.cs',
    'src\WINDOWS\VisionEngine\Core\ResultCoreVE.cs',
    'src\WINDOWS\VisionEngine\Core\StatusCoreVE.cs',
    'src\WINDOWS\VisionEngine\Core\SessionCoreVE.cs',
    'src\WINDOWS\VisionEngine\Core\EngineCoreVE.cs',
    'src\WINDOWS\VisionEngine\Core\RuntimeCoreVE.cs',
    'src\WINDOWS\VisionEngine\Device\StatesDeviceVE.cs',
    'src\WINDOWS\VisionEngine\Device\CapabilitiesDeviceVE.cs',
    'src\WINDOWS\VisionEngine\Device\StatusDeviceVE.cs',
    'src\WINDOWS\VisionEngine\Device\SessionDeviceVE.cs',
    'src\WINDOWS\VisionEngine\Device\ManagerDeviceVE.cs',
    'src\WINDOWS\VisionEngine\Server\StatesServerVE.cs',
    'src\WINDOWS\VisionEngine\Server\BackendServerVE.cs',
    'src\WINDOWS\VisionEngine\Server\OptionsServerVE.cs',
    'src\WINDOWS\VisionEngine\Server\StatusServerVE.cs',
    'src\WINDOWS\VisionEngine\Server\SessionServerVE.cs',
    'src\WINDOWS\VisionEngine\Server\ManagerServerVE.cs',
    'src\WINDOWS\VisionEngine\Protocol\ConstantsProtocolVE.cs',
    'src\WINDOWS\VisionEngine\Protocol\CodecProtocolVE.cs',
    'src\WINDOWS\VisionEngine\Protocol\ExtensionsProtocolVE.cs',
    'src\WINDOWS\VisionEngine\Protocol\KindProtocolVE.cs',
    'src\WINDOWS\VisionEngine\Protocol\BinaryProtocolVE.cs',
    'src\WINDOWS\VisionEngine\Protocol\SessionProtocolVE.cs',
    'src\WINDOWS\VisionEngine\Protocol\HeaderProtocolVE.cs',
    'src\WINDOWS\VisionEngine\Protocol\ReaderProtocolVE.cs',
    'src\WINDOWS\VisionEngine\Transport\ModeTransportVE.cs',
    'src\WINDOWS\VisionEngine\Transport\StatesTransportVE.cs',
    'src\WINDOWS\VisionEngine\Transport\TunnelTransportVE.cs',
    'src\WINDOWS\VisionEngine\Transport\SessionTransportVE.cs',
    'src\WINDOWS\VisionEngine\Transport\ManagerTransportVE.cs',
    'src\WINDOWS\VisionEngine\Video\StatesVideoVE.cs',
    'src\WINDOWS\VisionEngine\Video\PacketVideoVE.cs',
    'src\WINDOWS\VisionEngine\Video\FrameVideoVE.cs',
    'src\WINDOWS\VisionEngine\Video\StatsVideoVE.cs',
    'src\WINDOWS\VisionEngine\Video\StatusVideoVE.cs',
    'src\WINDOWS\VisionEngine\Video\MergerVideoVE.cs',
    'src\WINDOWS\VisionEngine\Video\DemuxerVideoVE.cs',
    'src\WINDOWS\VisionEngine\Video\DecoderVideoVE.cs',
    'src\WINDOWS\VisionEngine\Video\ManagerVideoVE.cs'
)

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' NOVORA - ENGINE NAMING VERIFICATION' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

$Missing = @()
foreach ($RelativePath in $Expected) {
    $FullPath = Join-Path $RepoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $FullPath -PathType Leaf)) {
        $Missing += $RelativePath
    }
}

if ($Missing.Count -gt 0) {
    Write-Host 'Faltan archivos normalizados:' -ForegroundColor Red
    $Missing | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    throw 'La estructura de motores no cumple la nomenclatura.'
}

$OldNames = @(
    'LinkEngineLE',
    'LinkResultLE',
    'LinkStatusLE',
    'LinkStateLE',
    'DeviceLE',
    'NetworkLE',
    'RecoveryLE',
    'RuntimeLE',
    'TransportLE',
    'MetricsLE',
    'RuntimeSessionLE',
    'RuntimeStateLE',
    'TransportSessionLE',
    'TransportStateLE',
    'NetworkSessionLE',
    'NetworkStateLE'
)

$CodeRoots = @(
    (Join-Path $RepoRoot 'src\NOVORA'),
    (Join-Path $RepoRoot 'NOVORA.linkEngine.Android'),
    (Join-Path $RepoRoot 'tests\NOVORA.Tests')
)

$Stale = @()
foreach ($CodeRoot in $CodeRoots) {
    if (-not (Test-Path -LiteralPath $CodeRoot)) {
        continue
    }

    Get-ChildItem -LiteralPath $CodeRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object {
            $Text = Get-Content -LiteralPath $_.FullName -Raw
            foreach ($OldName in $OldNames) {
                if ($Text -match "\b$([regex]::Escape($OldName))\b") {
                    $Stale += "$($_.FullName): $OldName"
                }
            }
        }
}

if ($Stale.Count -gt 0) {
    Write-Host 'Se encontraron identificadores viejos:' -ForegroundColor Red
    $Stale | Sort-Object -Unique | ForEach-Object {
        Write-Host " - $_" -ForegroundColor Red
    }
    throw 'Quedaron referencias de nomenclatura anterior.'
}


$VisionRoot = Join-Path $RepoRoot 'src\WINDOWS\VisionEngine'
$ForbiddenVisionTokens = @(
    'RendererVideoVE',
    'Direct3D',
    'WriteableBitmap',
    'BitmapSource',
    'System.Windows.Controls.Image'
)



$VisionNamingErrors = @()
Get-ChildItem -LiteralPath $VisionRoot -Recurse -File -Filter '*.cs' |
    ForEach-Object {
        $Stem = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
        if (-not $Stem.EndsWith('VE', [System.StringComparison]::Ordinal)) {
            $VisionNamingErrors += "$($_.FullName): el archivo no termina en VE.cs"
            return
        }

        $Text = Get-Content -LiteralPath $_.FullName -Raw
        $TypePattern = "\b(?:class|enum|interface|record(?:\s+(?:class|struct))?)\s+$([regex]::Escape($Stem))\b"
        if ($Text -notmatch $TypePattern) {
            $VisionNamingErrors += "$($_.FullName): no contiene el tipo principal $Stem"
        }
    }

if ($VisionNamingErrors.Count -gt 0) {
    Write-Host 'VisionEngine viola Función + Carpeta + VE:' -ForegroundColor Red
    $VisionNamingErrors | Sort-Object -Unique | ForEach-Object {
        Write-Host " - $_" -ForegroundColor Red
    }
    throw 'La nomenclatura VisionEngine Block B no es válida.'
}

$VisualLeaks = @()
Get-ChildItem -LiteralPath $VisionRoot -Recurse -File -Filter '*.cs' |
    ForEach-Object {
        $Text = Get-Content -LiteralPath $_.FullName -Raw
        foreach ($Token in $ForbiddenVisionTokens) {
            if ($Text.Contains($Token)) {
                $VisualLeaks += "$($_.FullName): $Token"
            }
        }
    }

if ($VisualLeaks.Count -gt 0) {
    Write-Host 'VisionEngine Block B dejo de ser headless:' -ForegroundColor Red
    $VisualLeaks | Sort-Object -Unique | ForEach-Object {
        Write-Host " - $_" -ForegroundColor Red
    }
    throw 'Renderer/visual pipeline no debe existir todavia.'
}

Write-Host '[OK] Estructura y referencias normalizadas.' -ForegroundColor Green

if ($SkipBuild) {
    Write-Host '[SKIP] Compilacion omitida por parametro.' -ForegroundColor Yellow
    exit 0
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet no esta disponible en PATH.'
}

Write-Host ''
Write-Host '=== BUILD WINDOWS ===' -ForegroundColor Cyan
& dotnet build $WindowsProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "NOVORA Windows fallo con codigo $LASTEXITCODE."
}

Write-Host ''
Write-Host '=== BUILD ANDROID ===' -ForegroundColor Cyan
& dotnet build $AndroidProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "NOVORA Android fallo con codigo $LASTEXITCODE."
}

Write-Host ''
Write-Host '[OK] Nomenclatura aplicada y ambos proyectos compilan.' -ForegroundColor Green
