[CmdletBinding()]
param(
    [string]$RepoPath = "",
    [string]$DeviceSerial = "",
    [switch]$SkipTests,
    [switch]$Launch
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

function Assert-FileVE {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description no encontrado: $Path"
    }
}

if ([string]::IsNullOrWhiteSpace($RepoPath)) {
    $RepoPath = Split-Path -Parent $PSScriptRoot
}

$RepoPath = [System.IO.Path]::GetFullPath($RepoPath)
$Solution = Join-Path $RepoPath "NOVORA.sln"
$TestsProject = Join-Path $RepoPath "tests\NOVORA.Tests\NOVORA.Tests.csproj"
$StaticVerifier = Join-Path $RepoPath "tests\visionengine\verify_block_d.py"
$NamingVerifier = Join-Path $RepoPath "scripts\validate_engine_naming.py"
$MainXaml = Join-Path $RepoPath "src\NOVORA\MainWindow.xaml"
$MainCode = Join-Path $RepoPath "src\NOVORA\MainWindow.xaml.cs"
$Sdl = Join-Path $RepoPath "src\NOVORA\Tools\SDL3.dll"
$Avutil = Join-Path $RepoPath "src\NOVORA\Tools\avutil-60.dll"
$Adb = Join-Path $RepoPath "src\NOVORA\Tools\adb.exe"

Write-Section "NOVORA - VISIONENGINE BLOCK D VERIFICATION"

Assert-FileVE $Solution "NOVORA.sln"
Assert-FileVE $TestsProject "NOVORA.Tests"
Assert-FileVE $StaticVerifier "verify_block_d.py"
Assert-FileVE $NamingVerifier "validate_engine_naming.py"
Assert-FileVE $MainXaml "MainWindow.xaml"
Assert-FileVE $MainCode "MainWindow.xaml.cs"
Assert-FileVE $Sdl "SDL3.dll"
Assert-FileVE $Avutil "avutil-60.dll"
Assert-FileVE $Adb "adb.exe"

Write-Host "[1/6] Contrato estatico Block D..." -ForegroundColor Yellow

$Python = Get-Command python -ErrorAction SilentlyContinue
if (-not $Python) {
    $Python = Get-Command py -ErrorAction SilentlyContinue
}

if ($Python) {
    if ($Python.Name -eq "py.exe" -or $Python.Name -eq "py") {
        & $Python.Source -3 $StaticVerifier
        if ($LASTEXITCODE -ne 0) {
            throw "verify_block_d.py fallo con codigo $LASTEXITCODE."
        }

        & $Python.Source -3 $NamingVerifier
        if ($LASTEXITCODE -ne 0) {
            throw "validate_engine_naming.py fallo con codigo $LASTEXITCODE."
        }
    }
    else {
        & $Python.Source $StaticVerifier
        if ($LASTEXITCODE -ne 0) {
            throw "verify_block_d.py fallo con codigo $LASTEXITCODE."
        }

        & $Python.Source $NamingVerifier
        if ($LASTEXITCODE -ne 0) {
            throw "validate_engine_naming.py fallo con codigo $LASTEXITCODE."
        }
    }
}
else {
    Write-Warning "Python no esta disponible; se omite verificador estatico."
}

Write-Host ""
Write-Host "[2/6] Verificando que Play ya no use ScrcpyService..." -ForegroundColor Yellow

$MainText = Get-Content -LiteralPath $MainCode -Raw

if ($MainText -match '_scrcpy\.StartOptimized') {
    throw "MainActionButton todavia contiene _scrcpy.StartOptimized()."
}

if ($MainText -notmatch 'ToggleVisionEngineVEAsync') {
    throw "MainActionButton no esta conectado a ToggleVisionEngineVEAsync()."
}

Write-Host "[OK] Play -> VisionEngine." -ForegroundColor Green

Write-Host ""
Write-Host "[3/6] Build Release..." -ForegroundColor Yellow

& dotnet build $Solution -c Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build fallo con codigo $LASTEXITCODE."
}

if (-not $SkipTests) {
    Write-Host ""
    Write-Host "[4/6] xUnit Block D..." -ForegroundColor Yellow

    & dotnet test $TestsProject -c Release --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test fallo con codigo $LASTEXITCODE."
    }
}
else {
    Write-Host ""
    Write-Host "[4/6] xUnit omitido por parametro." -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "[5/6] ADB / dispositivo..." -ForegroundColor Yellow

if ([string]::IsNullOrWhiteSpace($DeviceSerial)) {
    $Devices = @(
        & $Adb devices |
            Select-Object -Skip 1 |
            Where-Object { $_ -match '^\S+\s+device$' }
    )

    if ($Devices.Count -eq 1) {
        $DeviceSerial = (($Devices[0] -split '\s+')[0]).Trim()
    }
}

if (-not [string]::IsNullOrWhiteSpace($DeviceSerial)) {
    $State = (& $Adb -s $DeviceSerial get-state).Trim()
    if ($State -ne "device") {
        throw "ADB no ve $DeviceSerial como device. Estado: $State"
    }

    $Model = (& $Adb -s $DeviceSerial shell getprop ro.product.model).Trim()
    $Android = (& $Adb -s $DeviceSerial shell getprop ro.build.version.release).Trim()

    Write-Host "Device  : $Model"
    Write-Host "Android : $Android"
    Write-Host "Serial  : $DeviceSerial"
}
else {
    Write-Warning "No se selecciono un Android para la prueba fisica."
}

Write-Host ""
Write-Host "[6/6] Host de renderer en XAML..." -ForegroundColor Yellow

$XamlText = Get-Content -LiteralPath $MainXaml -Raw
$HasRendererHost = $XamlText -match 'HostRendererVE'

if ($HasRendererHost) {
    Write-Host "[OK] MainWindow.xaml contiene HostRendererVE." -ForegroundColor Green
}
else {
    Write-Host "[INFO] MainWindow.xaml aun no contiene HostRendererVE." -ForegroundColor Yellow
    Write-Host "El motor D compila y Play puede iniciar VE, pero no mostrara imagen hasta que coloques el host en tu diseno." -ForegroundColor Yellow
}

Write-Section "VISIONENGINE BLOCK D - VERIFICACION DE CODIGO COMPLETA"

Write-Host "Build Release: OK" -ForegroundColor Green
if (-not $SkipTests) {
    Write-Host "xUnit:         OK" -ForegroundColor Green
}
Write-Host "Play -> VE:    OK" -ForegroundColor Green
Write-Host "SDL3:          PRESENTE" -ForegroundColor Green
Write-Host "avutil:        PRESENTE" -ForegroundColor Green
Write-Host ""

if ($Launch) {
    $Candidates = @(
        Join-Path $RepoPath "src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\NOVORA.exe",
        Join-Path $RepoPath "src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\win-x64\NOVORA.exe"
    )

    $Exe = $Candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $Exe) {
        throw "Build correcto, pero no se encontro NOVORA.exe para Launch."
    }

    Write-Host "Iniciando NOVORA para la prueba visual..." -ForegroundColor Cyan
    Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe)
}
else {
    Write-Host "Para abrir NOVORA despues del build:" -ForegroundColor Cyan
    Write-Host ".\scripts\Test-VisionEngine-BlockD.ps1 -Launch"
}

Write-Host ""
Write-Host "La primera imagen solo se considera validada cuando HostRendererVE este en tu XAML y el A56 presente video por direct3d11." -ForegroundColor Yellow
