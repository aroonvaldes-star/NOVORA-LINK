[CmdletBinding()]
param(
    [string]$DeviceSerial,
    [ValidateRange(10, 3600)]
    [int]$Seconds = 60,
    [switch]$SkipAndroidBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WindowsProject = Join-Path $RepoRoot 'src\NOVORA\NOVORA.csproj'
$AndroidProject = Join-Path $RepoRoot 'NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj'
$TestsProject = Join-Path $RepoRoot 'tests\NOVORA.Tests\NOVORA.Tests.csproj'
$HarnessProject = Join-Path $RepoRoot 'tests\NOVORA.VisionEngine.BlockCHarness\NOVORA.VisionEngine.BlockCHarness.csproj'
$NamingScript = Join-Path $PSScriptRoot 'Verify-EngineNaming.ps1'
$StaticScript = Join-Path $RepoRoot 'tests\visionengine\verify_block_c.py'
$Adb = Join-Path $RepoRoot 'src\NOVORA\Tools\adb.exe'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' NOVORA - VISIONENGINE BLOCK C VERIFICATION' -ForegroundColor Cyan
Write-Host ' RECOVERY + METRICS + PERFORMANCE + STRESS / HEADLESS' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet no esta disponible en PATH.'
}

Write-Host '=== NAMING / STRUCTURE ===' -ForegroundColor Cyan
& $NamingScript -SkipBuild
if ($LASTEXITCODE -ne 0) { throw 'Fallo Verify-EngineNaming.ps1.' }

if (Get-Command python -ErrorAction SilentlyContinue) {
    Write-Host ''
    Write-Host '=== BLOCK C STATIC CONTRACT ===' -ForegroundColor Cyan
    & python $StaticScript
    if ($LASTEXITCODE -ne 0) { throw 'Fallo verify_block_c.py.' }
}

Write-Host ''
Write-Host '=== BUILD NOVORA WINDOWS ===' -ForegroundColor Cyan
& dotnet build $WindowsProject -c Release
if ($LASTEXITCODE -ne 0) { throw "NOVORA Windows fallo con codigo $LASTEXITCODE." }

Write-Host ''
Write-Host '=== XUNIT BLOCK A + B + C ===' -ForegroundColor Cyan
& dotnet test $TestsProject -c Release
if ($LASTEXITCODE -ne 0) { throw "NOVORA.Tests fallo con codigo $LASTEXITCODE." }

if (-not $SkipAndroidBuild) {
    Write-Host ''
    Write-Host '=== BUILD ANDROID COMPANION ===' -ForegroundColor Cyan
    & dotnet build $AndroidProject -c Release
    if ($LASTEXITCODE -ne 0) { throw "NOVORA Android fallo con codigo $LASTEXITCODE." }
}

if ([string]::IsNullOrWhiteSpace($DeviceSerial)) {
    Write-Host ''
    Write-Host '[OK] Build y tests Block C terminados.' -ForegroundColor Green
    Write-Host 'Para el Galaxy A56 5G:' -ForegroundColor Yellow
    Write-Host '.\scripts\Test-VisionEngine-BlockC.ps1 -DeviceSerial SERIAL -Seconds 60' -ForegroundColor Yellow
    exit 0
}

if (-not (Test-Path -LiteralPath $Adb)) { throw "ADB no encontrado: $Adb" }
$State = (& $Adb -s $DeviceSerial get-state).Trim()
if ($State -ne 'device') { throw "ADB no ve $DeviceSerial como device. Estado: $State" }

$Manufacturer = (& $Adb -s $DeviceSerial shell getprop ro.product.manufacturer).Trim()
$Model = (& $Adb -s $DeviceSerial shell getprop ro.product.model).Trim()
$Android = (& $Adb -s $DeviceSerial shell getprop ro.build.version.release).Trim()

Write-Host ''
Write-Host '=== DEVICE BLOCK C STRESS ===' -ForegroundColor Cyan
Write-Host "Device   : $Manufacturer $Model"
Write-Host "Android  : $Android"
Write-Host "Serial   : $DeviceSerial"
Write-Host "Duration : $Seconds s"
Write-Host 'Renderer : DISABLED'
Write-Host ''
Write-Host 'Reproduce video/audio durante la prueba.' -ForegroundColor Yellow

& dotnet run --project $HarnessProject -c Release -- $DeviceSerial $Seconds
if ($LASTEXITCODE -ne 0) { throw "VisionEngine Block C harness fallo con codigo $LASTEXITCODE." }

Write-Host ''
Write-Host '[OK] VisionEngine Block C supero recovery/metrics/performance/stress sin renderer.' -ForegroundColor Green
