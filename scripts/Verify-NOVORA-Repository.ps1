#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$RepoPath = '',
    [switch]$NoRestore,
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepoPath)) { $RepoPath = Split-Path -Parent $PSScriptRoot }

$RepoPath = [IO.Path]::GetFullPath($RepoPath)
$NovoraProject = Join-Path $RepoPath 'src\NOVORA\NLProjectDesktop.csproj'
$AndroidProject = Join-Path $RepoPath 'src\NOVORA.Android\NLProjectAndroid.csproj'
$TestsProjects = @(
    'tests\NOVORA.LinkEngine.Tests\NOVORA.LinkEngine.Tests.csproj',
    'tests\NOVORA.ExInHid.Tests\NOVORA.ExInHid.Tests.csproj',
    'tests\NOVORA.DynamicAddress.Tests\NOVORA.DynamicAddress.Tests.csproj',
    'tests\NOVORA.VisionEngine.Startup.Tests\NOVORA.VisionEngine.Startup.Tests.csproj',
    'tests\NOVORA.VisionEngine.Presentation.Tests\NLProjectVisionEnginePresentationTests.csproj',
    'tests\NOVORA.NetworkPolicy.Tests\NOVORA.NetworkPolicy.Tests.csproj',
    'tests\NOVORA.RelayPolicy.Tests\NOVORA.RelayPolicy.Tests.csproj',
    'tests\NOVORA.NetworkSession.Tests\NOVORA.NetworkSession.Tests.csproj',
    'tests\NOVORA.NetworkControlProbe.Tests\NOVORA.NetworkControlProbe.Tests.csproj',
    'tests\NOVORA.CapturePolicy.Tests\NOVORA.CapturePolicy.Tests.csproj',
    'tests\NOVORA.CaptureControl.Tests\NOVORA.CaptureControl.Tests.csproj',
    'tests\NOVORA.FileProtocol.Tests\NOVORA.FileProtocol.Tests.csproj'
) | ForEach-Object { Join-Path $RepoPath $_ }
$NovoraRelay = Join-Path $RepoPath 'src\NOVORA\LinkEngine\Network\Native\NovoraRelay'
$CargoManifest = Join-Path $NovoraRelay 'Cargo.toml'
$ProtocolPath = Join-Path $RepoPath 'src\NOVORA\Control\NLControlProtocol.cs'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepoPath ('artifacts\verification\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)

function Section([string]$Text) {
    Write-Host ''
    Write-Host ('=' * 64) -ForegroundColor Cyan
    Write-Host " $Text" -ForegroundColor Cyan
    Write-Host ('=' * 64) -ForegroundColor Cyan
}

function Require-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Falta archivo requerido: $Path"
    }
}

function Run-Step([string]$Name, [scriptblock]$Command) {
    Section $Name
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name terminó con código $LASTEXITCODE."
    }
    Write-Host "[OK] $Name" -ForegroundColor Green
}

Section 'PRECHECK NOVORA-LINK'
Require-File $NovoraProject
Require-File $AndroidProject
foreach ($TestsProject in $TestsProjects) { Require-File $TestsProject }
Require-File $CargoManifest
Require-File $ProtocolPath

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet no está disponible en PATH.'
}
if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw 'cargo no está disponible en PATH.'
}

$forbiddenPolling = @(
    'MaintenanceIntervalLE',
    'StatusRefreshIntervalLE',
    'MonitorIntervalLE',
    'RequiredSocketFailuresLE',
    'RequiredInfrastructureFailuresLE'
)

$linkFiles = Get-ChildItem -LiteralPath (Join-Path $RepoPath 'src\NOVORA\LinkEngine') -Recurse -File -Filter '*.cs'
foreach ($token in $forbiddenPolling) {
    $hit = $linkFiles | Select-String -SimpleMatch $token | Select-Object -First 1
    if ($hit) {
        throw "Polling legado detectado: $token en $($hit.Path):$($hit.LineNumber)"
    }
}
Write-Host '[OK] No se detectaron los loops periódicos legacy retirados.' -ForegroundColor Green

[xml]$androidProjectXml = Get-Content -LiteralPath $AndroidProject -Raw
$linkedProtocol = @($androidProjectXml.SelectNodes('/Project/ItemGroup/Compile[@Include]') |
    Where-Object { $_.GetAttribute('Include') -notmatch '[*?]' } | ForEach-Object {
    [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $AndroidProject) $_.GetAttribute('Include')))
}) -contains [IO.Path]::GetFullPath($ProtocolPath)
if (-not $linkedProtocol) {
    throw 'AppControl debe compilar el mismo NLControlProtocol que Windows.'
}
Write-Host '[OK] NLControlProtocol compartido por PC y Android.' -ForegroundColor Green

Run-Step 'CARGO CHECK NOVORA RELAY' {
    Push-Location $NovoraRelay
    try { cargo check --all-targets --offline --locked --manifest-path $CargoManifest }
    finally { Pop-Location }
}

Run-Step 'CARGO TEST NOVORA RELAY' {
    Push-Location $NovoraRelay
    try { cargo test --release --offline --locked --manifest-path $CargoManifest }
    finally { Pop-Location }
}

Run-Step 'CARGO BUILD RELEASE NOVORA RELAY' {
    Push-Location $NovoraRelay
    try { cargo build --release --offline --locked --manifest-path $CargoManifest }
    finally { Pop-Location }
}

if (-not $NoRestore) {
    Run-Step 'DOTNET RESTORE WINDOWS' { dotnet restore $NovoraProject }
}

Run-Step 'DOTNET BUILD WINDOWS RELEASE' {
    dotnet build $NovoraProject -c Release --no-restore "-p:OutputPath=$(Join-Path $OutputRoot 'Windows')/"
}

foreach ($TestsProject in $TestsProjects) {
    $TestName = [IO.Path]::GetFileNameWithoutExtension($TestsProject)
    if (-not $NoRestore) {
        Run-Step "DOTNET RESTORE $TestName" { dotnet restore $TestsProject }
    }
    Run-Step "DOTNET RUN $TestName" {
        dotnet run --project $TestsProject -c Release --no-restore "-p:OutputPath=$(Join-Path $OutputRoot $TestName)/"
    }
}

if (-not $NoRestore) {
    Run-Step 'DOTNET RESTORE ANDROID' { dotnet restore $AndroidProject -r android-arm64 }
}

Run-Step 'DOTNET BUILD ANDROID RELEASE' {
    dotnet build $AndroidProject -c Release -t:Compile --no-restore "-p:OutputPath=$(Join-Path $OutputRoot 'Android')/"
}

Run-Step 'TEST REPARACION FIREWALL SIN CAMBIOS AL SISTEMA' {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $RepoPath 'tests\Test-NOVORA-PrivateLanFirewall.ps1')
}

Section 'RESULTADO'
Write-Host '[OK] NOVORA Windows compila.' -ForegroundColor Green
Write-Host '[OK] Código Android compilado; no se generó ni instaló un APK.' -ForegroundColor Green
Write-Host "[OK] Las $($TestsProjects.Count) suites de consola pasaron." -ForegroundColor Green
Write-Host '[OK] NovoraRelay compila y pasa tests.' -ForegroundColor Green
Write-Host '[OK] Verificación de código terminada; la sesión física PC-Android requiere pruebas independientes.' -ForegroundColor Green
