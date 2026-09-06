[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoPath = [IO.Path]::GetFullPath($RepoPath)
$RelayCore = Join-Path $RepoPath "src\NOVORA\LinkEngine\Network\Native\RelayCore"
$Manifest = Join-Path $RelayCore "Cargo.toml"
$BuiltExe = Join-Path $RelayCore "target\release\novora-linkengine-relay.exe"
$ToolsDir = Join-Path $RepoPath "src\NOVORA\Tools\LinkEngine"
$RuntimeExe = Join-Path $ToolsDir "NOVORA.LinkEngine.Relay.exe"

if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) {
    throw "No se encontró RelayCore: $Manifest"
}

$Cargo = Get-Command cargo -ErrorAction SilentlyContinue
if (-not $Cargo) {
    throw "Cargo no está instalado o no está disponible en PATH. Instala Rust para Windows y vuelve a ejecutar este script."
}

Write-Host "" 
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " NOVORA-LINK - BUILD LINKENGINE RELAY" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "RelayCore: $RelayCore"
Write-Host ""

Push-Location $RelayCore
try {
    & cargo build --release
    if ($LASTEXITCODE -ne 0) {
        throw "cargo build --release terminó con código $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $BuiltExe -PathType Leaf)) {
    throw "Cargo terminó pero no se encontró el ejecutable esperado: $BuiltExe"
}

New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
Copy-Item -LiteralPath $BuiltExe -Destination $RuntimeExe -Force

$Hash = (Get-FileHash -LiteralPath $RuntimeExe -Algorithm SHA256).Hash.ToLowerInvariant()
$Info = Get-Item -LiteralPath $RuntimeExe

Write-Host "" 
Write-Host "Relay actualizado correctamente." -ForegroundColor Green
Write-Host "Destino : $RuntimeExe" -ForegroundColor Green
Write-Host "Tamaño  : $($Info.Length) bytes"
Write-Host "SHA-256 : $Hash"
