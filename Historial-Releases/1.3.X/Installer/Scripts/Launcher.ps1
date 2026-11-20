[CmdletBinding()]
param(
    [switch]$CheckTools,
    [switch]$Launch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ToolDir = Join-Path $Root 'src\NOVORA\Tools'
$Exe = Join-Path $Root 'src\NOVORA\NOVORA.exe'

$required = @(
    'adb.exe',
    'scrcpy.exe',
    'gnirehtet.exe',
    'gnirehtet.apk'
)

if ($CheckTools) {
    foreach ($name in $required) {
        $path = Join-Path $ToolDir $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Falta la herramienta: $path"
        }
        Write-Host "OK  $path"
    }
    exit 0
}

if ($Launch) {
    if (-not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
        throw "No se encontró NOVORA.exe: $Exe"
    }

    Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe)
    exit 0
}

Write-Host 'NOVORA Launcher'
Write-Host "Tools: $ToolDir"
Write-Host "NOVORA: $Exe"
