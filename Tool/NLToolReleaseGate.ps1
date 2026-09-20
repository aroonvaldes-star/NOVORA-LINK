#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Section([string]$Text) {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host " $Text" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

function Run([scriptblock]$Command, [string]$Name) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name fallo. ExitCode=$LASTEXITCODE" }
}

$RepoPath = [IO.Path]::GetFullPath($RepoPath)
$DesktopProject = Join-Path $RepoPath "src\NOVORA\NLProjectDesktop.csproj"
$AndroidProject = Join-Path $RepoPath "src\NOVORA.Android\NLProjectAndroid.csproj"
$TestsProject = Join-Path $RepoPath "tests\NOVORA.Tests\NLProjectTests.csproj"
$CargoManifest = Join-Path $RepoPath "src\NOVORA\LinkEngine\Network\Native\RelayCore\Cargo.toml"
$VerifyTool = Join-Path $RepoPath "Tool\NLToolVerify.py"
$Server = Join-Path $RepoPath "src\NOVORA\Tools\scrcpy-server"
$ManualES = Join-Path $RepoPath "Documentation\Manual\ES\NLDocumentationManualUsuarioES.txt"
$ManualEN = Join-Path $RepoPath "Documentation\Manual\EN\NLDocumentationUserManualEN.txt"

Section "STATIC REQUIRED FILES"
foreach ($p in @($DesktopProject,$AndroidProject,$TestsProject,$CargoManifest,$VerifyTool,$Server,$ManualES,$ManualEN)) {
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw "Falta archivo requerido: $p" }
    Write-Host "[OK] $p" -ForegroundColor Green
}

Section "DESKTOP RESTORE + BUILD"
Run { dotnet restore $DesktopProject } "dotnet restore desktop"
Run { dotnet build $DesktopProject -c Release --no-restore } "dotnet build desktop"

Section "TESTS"
Run { dotnet restore $TestsProject } "dotnet restore tests"
Run { dotnet test $TestsProject -c Release --no-restore } "dotnet test"

Section "ANDROID COMPILE - NO APK IMPLICITO"
Run { dotnet restore $AndroidProject } "dotnet restore android"
Run { dotnet build $AndroidProject -c Release -t:Compile --no-restore } "dotnet android Compile"

Section "RELAYCORE"
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) { throw "Cargo no esta disponible en PATH." }
Run { cargo check --manifest-path $CargoManifest --all-targets } "cargo check"
Run { cargo test --manifest-path $CargoManifest } "cargo test"

Section "NOMENCLATURE / REPOSITORY VERIFY"
if (-not (Get-Command python -ErrorAction SilentlyContinue)) { throw "Python no esta disponible en PATH." }
Run { python $VerifyTool } "NLToolVerify"

Section "IMPORTANT RUNTIME NOTE"
Write-Host "[INFO] El fuente RelayCore ya limita LinkEngine a 1 dispositivo." -ForegroundColor Yellow
Write-Host "[INFO] Recompile/copie LENetworkRelay.exe con el proceso oficial antes de publicar." -ForegroundColor Yellow
Write-Host "[INFO] Este gate no declara PASS fisico USB/LAN/VE. Esas pruebas siguen siendo obligatorias." -ForegroundColor Yellow

Section "RELEASE GATE AUTOMATIZADO COMPLETADO"
