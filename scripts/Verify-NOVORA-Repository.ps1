#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoPath = [IO.Path]::GetFullPath($RepoPath)
$NovoraProject = Join-Path $RepoPath 'src\NOVORA\NOVORA.csproj'
$AndroidProject = Join-Path $RepoPath 'NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj'
$TestsProject = Join-Path $RepoPath 'tests\NOVORA.Tests\NOVORA.Tests.csproj'
$RelayCore = Join-Path $RepoPath 'src\NOVORA\LinkEngine\Network\Native\RelayCore'
$CargoManifest = Join-Path $RelayCore 'Cargo.toml'

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
Require-File $TestsProject
Require-File $CargoManifest

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

$pcProtocol = Get-Content -LiteralPath (Join-Path $RepoPath 'src\NOVORA\Remote\ProtocolRemoteNV.cs') -Raw
$androidProtocol = Get-Content -LiteralPath (Join-Path $RepoPath 'NOVORA.linkEngine.Android\Remote\ProtocolRemoteNV.cs') -Raw
if ($pcProtocol -notmatch 'ProtocolVersionNV\s*=\s*\r?\n\s*2;' -or
    $androidProtocol -notmatch 'ProtocolVersionNV\s*=\s*\r?\n\s*2;') {
    throw 'RemoteNV PC/Android no están ambos en protocolo v2.'
}
Write-Host '[OK] RemoteNV v2 PC/Android.' -ForegroundColor Green

Run-Step 'CARGO CHECK RELAYCORE' {
    Push-Location $RelayCore
    try { cargo check --all-targets --manifest-path $CargoManifest }
    finally { Pop-Location }
}

Run-Step 'CARGO TEST RELAYCORE' {
    Push-Location $RelayCore
    try { cargo test --manifest-path $CargoManifest }
    finally { Pop-Location }
}

Run-Step 'CARGO BUILD RELEASE RELAYCORE' {
    Push-Location $RelayCore
    try { cargo build --release --manifest-path $CargoManifest }
    finally { Pop-Location }
}

Run-Step 'DOTNET RESTORE WINDOWS' {
    dotnet restore $NovoraProject
}

Run-Step 'DOTNET BUILD WINDOWS RELEASE' {
    dotnet build $NovoraProject -c Release --no-restore
}

Run-Step 'DOTNET TEST WINDOWS' {
    dotnet test $TestsProject -c Release
}

Run-Step 'DOTNET RESTORE ANDROID' {
    dotnet restore $AndroidProject
}

Run-Step 'DOTNET BUILD ANDROID RELEASE' {
    dotnet build $AndroidProject -c Release --no-restore
}

Section 'RESULTADO'
Write-Host '[OK] NOVORA Windows compila.' -ForegroundColor Green
Write-Host '[OK] NOVORA Android compila.' -ForegroundColor Green
Write-Host '[OK] RelayCore compila y pasa tests.' -ForegroundColor Green
Write-Host '[OK] Verificación completa terminada.' -ForegroundColor Green
