[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WindowsProject = Join-Path $RepoRoot 'src\NOVORA\NOVORA.csproj'
$AndroidProject = Join-Path $RepoRoot 'NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj'

$Expected = @(
    'src\NOVORA\LinkEngine\Core\StatesCoreLE.cs',
    'src\NOVORA\LinkEngine\Core\ResultCoreLE.cs',
    'src\NOVORA\LinkEngine\Core\StatusCoreLE.cs',
    'src\NOVORA\LinkEngine\Core\EngineCoreLE.cs',
    'src\NOVORA\LinkEngine\Device\ConnectionDeviceLE.cs',
    'src\NOVORA\LinkEngine\Device\ManagerDeviceLE.cs',
    'src\NOVORA\LinkEngine\Device\PollingDeviceLE.cs',
    'src\NOVORA\LinkEngine\Device\ProvisioningDeviceLE.cs',
    'src\NOVORA\LinkEngine\Device\SessionDeviceLE.cs',
    'src\NOVORA\LinkEngine\Device\StateDeviceLE.cs',
    'src\NOVORA\LinkEngine\Metrics\CollectorMetricsLE.cs',
    'src\NOVORA\LinkEngine\Network\DnsNetworkLE.cs',
    'src\NOVORA\LinkEngine\Network\ManagerNetworkLE.cs',
    'src\NOVORA\LinkEngine\Network\RelayNetworkLE.cs',
    'src\NOVORA\LinkEngine\Network\SessionNetworkLE.cs',
    'src\NOVORA\LinkEngine\Network\StateNetworkLE.cs',
    'src\NOVORA\LinkEngine\Protocol\HeartbeatProtocolLE.cs',
    'src\NOVORA\LinkEngine\Recovery\ManagerRecoveryLE.cs',
    'src\NOVORA\LinkEngine\Recovery\MonitorRecoveryLE.cs',
    'src\NOVORA\LinkEngine\Recovery\PolicyRecoveryLE.cs',
    'src\NOVORA\LinkEngine\Recovery\ResultRecoveryLE.cs',
    'src\NOVORA\LinkEngine\Recovery\StateRecoveryLE.cs',
    'src\NOVORA\LinkEngine\Runtime\ManagerRuntimeLE.cs',
    'src\NOVORA\LinkEngine\Runtime\SessionRuntimeLE.cs',
    'src\NOVORA\LinkEngine\Runtime\StateRuntimeLE.cs',
    'src\NOVORA\LinkEngine\Transport\DataTransportLE.cs',
    'src\NOVORA\LinkEngine\Transport\HandshakeTransportLE.cs',
    'src\NOVORA\LinkEngine\Transport\ListenerTransportLE.cs',
    'src\NOVORA\LinkEngine\Transport\ManagerTransportLE.cs',
    'src\NOVORA\LinkEngine\Transport\PortTransportLE.cs',
    'src\NOVORA\LinkEngine\Transport\SessionTransportLE.cs',
    'src\NOVORA\LinkEngine\Transport\StateTransportLE.cs',
    'src\NOVORA\VisionEngine\Core\StatesCoreVE.cs',
    'src\NOVORA\VisionEngine\Core\ResultCoreVE.cs',
    'src\NOVORA\VisionEngine\Core\StatusCoreVE.cs',
    'src\NOVORA\VisionEngine\Core\SessionCoreVE.cs',
    'src\NOVORA\VisionEngine\Core\EngineCoreVE.cs',
    'src\NOVORA\VisionEngine\Core\RuntimeCoreVE.cs',
    'src\NOVORA\VisionEngine\Device\StatesDeviceVE.cs',
    'src\NOVORA\VisionEngine\Device\CapabilitiesDeviceVE.cs',
    'src\NOVORA\VisionEngine\Device\StatusDeviceVE.cs',
    'src\NOVORA\VisionEngine\Device\SessionDeviceVE.cs',
    'src\NOVORA\VisionEngine\Device\ManagerDeviceVE.cs',
    'src\NOVORA\VisionEngine\Server\StatesServerVE.cs',
    'src\NOVORA\VisionEngine\Server\BackendServerVE.cs',
    'src\NOVORA\VisionEngine\Server\OptionsServerVE.cs',
    'src\NOVORA\VisionEngine\Server\StatusServerVE.cs',
    'src\NOVORA\VisionEngine\Server\SessionServerVE.cs',
    'src\NOVORA\VisionEngine\Server\ManagerServerVE.cs',
    'src\NOVORA\VisionEngine\Protocol\ConstantsProtocolVE.cs',
    'src\NOVORA\VisionEngine\Protocol\CodecProtocolVE.cs',
    'src\NOVORA\VisionEngine\Protocol\ExtensionsProtocolVE.cs',
    'src\NOVORA\VisionEngine\Protocol\KindProtocolVE.cs',
    'src\NOVORA\VisionEngine\Protocol\BinaryProtocolVE.cs',
    'src\NOVORA\VisionEngine\Protocol\SessionProtocolVE.cs',
    'src\NOVORA\VisionEngine\Protocol\HeaderProtocolVE.cs',
    'src\NOVORA\VisionEngine\Protocol\ReaderProtocolVE.cs',
    'src\NOVORA\VisionEngine\Transport\ModeTransportVE.cs',
    'src\NOVORA\VisionEngine\Transport\StatesTransportVE.cs',
    'src\NOVORA\VisionEngine\Transport\TunnelTransportVE.cs',
    'src\NOVORA\VisionEngine\Transport\SessionTransportVE.cs',
    'src\NOVORA\VisionEngine\Transport\ManagerTransportVE.cs',
    'src\NOVORA\VisionEngine\Video\StatesVideoVE.cs',
    'src\NOVORA\VisionEngine\Video\PacketVideoVE.cs',
    'src\NOVORA\VisionEngine\Video\FrameVideoVE.cs',
    'src\NOVORA\VisionEngine\Video\StatsVideoVE.cs',
    'src\NOVORA\VisionEngine\Video\StatusVideoVE.cs',
    'src\NOVORA\VisionEngine\Video\MergerVideoVE.cs',
    'src\NOVORA\VisionEngine\Video\DemuxerVideoVE.cs',
    'src\NOVORA\VisionEngine\Video\DecoderVideoVE.cs',
    'src\NOVORA\VisionEngine\Video\ManagerVideoVE.cs'
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


$VisionRoot = Join-Path $RepoRoot 'src\NOVORA\VisionEngine'
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
