[CmdletBinding()]
param(
    [switch]$CheckTools
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$ToolDir = Join-Path $Root 'Tools\NOVORA V1.1'

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

Write-Host 'NOVORA Launcher'
Write-Host "Tools: $ToolDir"
