[CmdletBinding()]
param(
    [string]$ToolsDir = (Join-Path $PSScriptRoot "..\src\NOVORA\Tools")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScrcpyVersion = "4.1"
$ScrcpySha256 = "5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db"
$ScrcpyUrl = "https://github.com/Genymobile/scrcpy/releases/download/v$ScrcpyVersion/scrcpy-win64-v$ScrcpyVersion.zip"

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$ExpectedSha256
    )
    Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing
    $actual = (Get-FileHash -Path $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256.ToLowerInvariant()) {
        throw "SHA-256 invÃ¡lido para $Uri. Esperado: $ExpectedSha256. Obtenido: $actual"
    }
}

$resolvedToolsDir = [IO.Path]::GetFullPath($ToolsDir)
New-Item -ItemType Directory -Force -Path $resolvedToolsDir | Out-Null
$temp = Join-Path ([IO.Path]::GetTempPath()) ("novora-tools-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    $scrcpyZip = Join-Path $temp "scrcpy.zip"
    $scrcpyExtract = Join-Path $temp "scrcpy"
    Get-VerifiedArchive -Uri $ScrcpyUrl -Destination $scrcpyZip -ExpectedSha256 $ScrcpySha256
    Expand-Archive -Path $scrcpyZip -DestinationPath $scrcpyExtract -Force
    $scrcpyRoot = Get-ChildItem -Path $scrcpyExtract -Directory | Select-Object -First 1
    if (-not $scrcpyRoot) { throw "No se encontrÃ³ el directorio de scrcpy." }
    Copy-Item -Path (Join-Path $scrcpyRoot.FullName "*") -Destination $resolvedToolsDir -Recurse -Force

    $required = @("adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "scrcpy.exe", "scrcpy-server")
    $missing = @($required | Where-Object { -not (Test-Path (Join-Path $resolvedToolsDir $_)) })
    if ($missing.Count -gt 0) { throw "Faltan herramientas: $($missing -join ', ')" }
    Write-Host "NOVORA Tools preparados en: $resolvedToolsDir"
}
finally {
    Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
}
