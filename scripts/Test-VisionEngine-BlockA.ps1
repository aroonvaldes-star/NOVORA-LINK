[CmdletBinding()]
param(
    [string]$DeviceSerial,
    [ValidateRange(1, 300)]
    [int]$Seconds = 15,
    [switch]$SkipAndroidBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot
$WindowsProject = Join-Path $RepoRoot 'src\NOVORA\NOVORA.csproj'
$AndroidProject = Join-Path $RepoRoot 'NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj'
$TestsProject = Join-Path $RepoRoot 'tests\NOVORA.Tests\NOVORA.Tests.csproj'
$HarnessProject = Join-Path $RepoRoot 'tests\NOVORA.VisionEngine.HeadlessHarness\NOVORA.VisionEngine.HeadlessHarness.csproj'
$NamingScript = Join-Path $PSScriptRoot 'Verify-EngineNaming.ps1'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' NOVORA - VISIONENGINE BLOCK A HEADLESS VERIFICATION' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet no esta disponible en PATH.'
}

Write-Host '=== NAMING / STRUCTURE ===' -ForegroundColor Cyan
& $NamingScript -SkipBuild
if ($LASTEXITCODE -ne 0) {
    throw 'Fallo Verify-EngineNaming.ps1.'
}

Write-Host ''
Write-Host '=== BUILD NOVORA WINDOWS ===' -ForegroundColor Cyan
& dotnet build $WindowsProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "NOVORA Windows fallo con codigo $LASTEXITCODE."
}

Write-Host ''
Write-Host '=== XUNIT ===' -ForegroundColor Cyan
& dotnet test $TestsProject -c Release
if ($LASTEXITCODE -ne 0) {
    throw "NOVORA.Tests fallo con codigo $LASTEXITCODE."
}

if (-not $SkipAndroidBuild) {
    Write-Host ''
    Write-Host '=== BUILD ANDROID COMPANION ===' -ForegroundColor Cyan
    & dotnet build $AndroidProject -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "NOVORA Android fallo con codigo $LASTEXITCODE."
    }
}

if ([string]::IsNullOrWhiteSpace($DeviceSerial)) {
    Write-Host ''
    Write-Host '[OK] Build y pruebas estaticas terminadas.' -ForegroundColor Green
    Write-Host 'Para probar el stream headless:' -ForegroundColor Yellow
    Write-Host '.\scripts\Test-VisionEngine-BlockA.ps1 -DeviceSerial SERIAL -Seconds 15' -ForegroundColor Yellow
    exit 0
}

Write-Host ''
Write-Host '=== DEVICE HEADLESS TEST ===' -ForegroundColor Cyan
Write-Host "Serial: $DeviceSerial"
Write-Host "Duracion: $Seconds segundos"
Write-Host 'Renderer: DISABLED'

& dotnet run --project $HarnessProject -c Release -- $DeviceSerial $Seconds
if ($LASTEXITCODE -ne 0) {
    throw "VisionEngine headless harness fallo con codigo $LASTEXITCODE."
}

Write-Host ''
Write-Host '[OK] VisionEngine Block A recibio y decodifico frames sin mostrar imagen.' -ForegroundColor Green
