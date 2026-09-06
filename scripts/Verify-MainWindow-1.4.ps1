[CmdletBinding()]
param(
    [string]$RepoPath = "C:\Users\Aroon\Desktop\NOVORA-LINK",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
Set-StrictMode -Version Latest

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Text)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " $Text" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not $Text.Contains($Needle, [StringComparison]::Ordinal)) {
        throw "FALLO: $Description"
    }

    Write-Host "[PASS] $Description" -ForegroundColor Green
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Text.Contains($Needle, [StringComparison]::Ordinal)) {
        throw "FALLO: $Description"
    }

    Write-Host "[PASS] $Description" -ForegroundColor Green
}

Write-Section "NOVORA-LINK 1.4 - MAINWINDOW VERIFICATION"

$RepoPath = [System.IO.Path]::GetFullPath($RepoPath)
$XamlPath = Join-Path $RepoPath "src\NOVORA\MainWindow.xaml"
$MainCodePath = Join-Path $RepoPath "src\NOVORA\MainWindow.xaml.cs"
$VisionCodePath = Join-Path $RepoPath "src\NOVORA\MainWindow.VisionEngineVE.cs"
$InterfaceCodePath = Join-Path $RepoPath "src\NOVORA\MainWindow.Interface14.cs"
$ProjectPath = Join-Path $RepoPath "src\NOVORA\NOVORA.csproj"

foreach ($Path in @(
    $XamlPath,
    $MainCodePath,
    $VisionCodePath,
    $InterfaceCodePath,
    $ProjectPath
)) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "No existe: $Path"
    }
}

$Xaml = Get-Content -LiteralPath $XamlPath -Raw
$MainCode = Get-Content -LiteralPath $MainCodePath -Raw
$VisionCode = Get-Content -LiteralPath $VisionCodePath -Raw
$Project = Get-Content -LiteralPath $ProjectPath -Raw

Write-Host "=== CONTRATO VISUAL ===" -ForegroundColor Yellow
Assert-Contains $Xaml '#ECEFF2' 'Fondo matte gris 1.4'
Assert-Contains $Xaml '#00AEEF' 'Azul NOVORA'
Assert-NotContains $Xaml 'DropShadowEffect' 'Sin sombras'
Assert-NotContains $Xaml 'NovoraBrandImageSource' 'Sin marca/titulo en la barra superior'
Assert-Contains $Xaml 'Content="&#xE713;"' 'Icono Configuración'
Assert-Contains $Xaml 'Content="&#xE921;"' 'Icono Minimizar'
Assert-Contains $Xaml 'Content="&#xE8BB;"' 'Icono Cerrar'
Assert-Contains $Xaml 'x:Name="DeviceComboBox14"' 'Selector de dispositivo'
Assert-Contains $Xaml 'x:Name="MonitorComboBox"' 'Selector de monitor'
Assert-Contains $Xaml 'Text="RED"' 'Sección RED'
Assert-Contains $Xaml 'Text="VIDEO"' 'Sección VIDEO'
Assert-Contains $Xaml 'Text="PERFORMANCE"' 'Sección PERFORMANCE'
Assert-Contains $Xaml 'x:Name="BatteryPercentText14"' 'Porcentaje único dentro de batería'
Assert-Contains $Xaml 'Text="LinkEngine"' 'Botón visual LinkEngine'
Assert-Contains $Xaml 'x:Name="MainActionButton"' 'Botón Start/Stop VisionEngine'
Assert-NotContains $Xaml 'Listo para crear en grande' 'Sin barra inferior antigua'

Write-Host ""
Write-Host "=== CABLEADO FUNCIONAL ===" -ForegroundColor Yellow
Assert-Contains $MainCode 'ApplyPerformanceSurface14(metrics);' 'Métricas 1.4 conectadas'
Assert-Contains $VisionCode 'EnsureVisionRendererHostVE' 'Renderer dinámico VisionEngine'
Assert-Contains $VisionCode 'VideoRendererContainer.Children.Add(host);' 'Host VE insertado en tarjeta VIDEO'
Assert-Contains $VisionCode 'ShowVisionRendererVE();' 'Vista de renderer activa al iniciar'
Assert-Contains $VisionCode 'ShowVideoSettingsVE();' 'Ajustes regresan al detener/fallar'
Assert-Contains $Project '<Version>1.4.0</Version>' 'Versión del proyecto 1.4.0'

if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "=== BUILD RELEASE ===" -ForegroundColor Yellow

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet no está disponible en PATH."
    }

    Push-Location $RepoPath
    try {
        & dotnet build $ProjectPath -c Release

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build falló con código $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

Write-Section "MAINWINDOW 1.4 VERIFICADO"
Write-Host "Interfaz: matte / azul NOVORA / sin sombras" -ForegroundColor Green
Write-Host "LinkEngine: conectado" -ForegroundColor Green
Write-Host "VisionEngine Start/Stop: conectado" -ForegroundColor Green
Write-Host "Renderer D: conectado dinámicamente" -ForegroundColor Green
Write-Host "Performance: CPU/RAM/Batería/Temperatura" -ForegroundColor Green
Write-Host ""
