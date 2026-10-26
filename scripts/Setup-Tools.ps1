[CmdletBinding()]
param(
    [string]$ToolsDir
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($ToolsDir)) {
    $ToolsDir = Join-Path $PSScriptRoot "..\src\NOVORA\Tools"
}
$resolvedToolsDir = [IO.Path]::GetFullPath($ToolsDir)
$required = @(
    "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll",
    "avcodec-62.dll", "avutil-60.dll", "swresample-6.dll", "SDL3.dll"
)
$missing = @($required | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $resolvedToolsDir $_) -PathType Leaf)
})
if ($missing.Count -gt 0) {
    throw "Faltan herramientas de NOVORA: $($missing -join ', ')"
}
foreach ($name in @("scrcpy.exe", "scrcpy-server", "gnirehtet.exe", "gnirehtet.apk")) {
    if (Test-Path -LiteralPath (Join-Path $resolvedToolsDir $name) -PathType Leaf) {
        throw "Herramienta retirada presente en Tools: $name"
    }
}
Write-Host "NOVORA Tools verificados en: $resolvedToolsDir"
