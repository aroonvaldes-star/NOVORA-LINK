[CmdletBinding()]
param(
    [string]$ToolsDir = (Join-Path $PSScriptRoot "..\src\NOVORA\Tools")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScrcpyVersion = "4.1"
$ScrcpySha256 = "5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db"
$ScrcpyUrl = "https://github.com/Genymobile/scrcpy/releases/download/v$ScrcpyVersion/scrcpy-win64-v$ScrcpyVersion.zip"

$GnirehtetVersion = "2.5.1"
$GnirehtetSha256 = "7f5b1063e7895182aa60def1437e50363c3758144088dcd079037bb7c3c46a1c"
$GnirehtetUrl = "https://github.com/Genymobile/gnirehtet/releases/download/v$GnirehtetVersion/gnirehtet-rust-win64-v$GnirehtetVersion.zip"

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$ExpectedSha256
    )

    Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing

    $actual =
        (Get-FileHash -Path $Destination -Algorithm SHA256)
            .Hash
            .ToLowerInvariant()

    if ($actual -ne $ExpectedSha256.ToLowerInvariant()) {
        throw "SHA-256 inválido para $Uri. Esperado: $ExpectedSha256. Obtenido: $actual"
    }
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Archivo legal requerido ausente: $Source"
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedToolsDir = [IO.Path]::GetFullPath($ToolsDir)

New-Item -ItemType Directory -Force -Path $resolvedToolsDir | Out-Null

$temp = Join-Path (
    [IO.Path]::GetTempPath()
) ("novora-tools-" + [Guid]::NewGuid().ToString("N"))

New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    # ============================================================
    # SCRCPY + ADB + DEPENDENCIAS WINDOWS
    # ============================================================

    $scrcpyZip = Join-Path $temp "scrcpy.zip"
    $scrcpyExtract = Join-Path $temp "scrcpy"

    Get-VerifiedArchive `
        -Uri $ScrcpyUrl `
        -Destination $scrcpyZip `
        -ExpectedSha256 $ScrcpySha256

    Expand-Archive `
        -Path $scrcpyZip `
        -DestinationPath $scrcpyExtract `
        -Force

    $scrcpyRoot =
        Get-ChildItem -Path $scrcpyExtract -Directory |
        Select-Object -First 1

    if (-not $scrcpyRoot) {
        throw "No se encontró el directorio de scrcpy."
    }

    Copy-Item `
        -Path (Join-Path $scrcpyRoot.FullName "*") `
        -Destination $resolvedToolsDir `
        -Recurse `
        -Force

    # ============================================================
    # GNIREHTET
    # ============================================================

    $gnirehtetZip = Join-Path $temp "gnirehtet.zip"
    $gnirehtetExtract = Join-Path $temp "gnirehtet"

    Get-VerifiedArchive `
        -Uri $GnirehtetUrl `
        -Destination $gnirehtetZip `
        -ExpectedSha256 $GnirehtetSha256

    Expand-Archive `
        -Path $gnirehtetZip `
        -DestinationPath $gnirehtetExtract `
        -Force

    $gnirehtetExe =
        Get-ChildItem `
            -Path $gnirehtetExtract `
            -Recurse `
            -Filter "gnirehtet.exe" |
        Select-Object -First 1

    $gnirehtetApk =
        Get-ChildItem `
            -Path $gnirehtetExtract `
            -Recurse `
            -Filter "gnirehtet.apk" |
        Select-Object -First 1

    if (-not $gnirehtetExe -or -not $gnirehtetApk) {
        throw "El paquete de Gnirehtet está incompleto."
    }

    Copy-Item `
        -LiteralPath $gnirehtetExe.FullName `
        -Destination (Join-Path $resolvedToolsDir "gnirehtet.exe") `
        -Force

    Copy-Item `
        -LiteralPath $gnirehtetApk.FullName `
        -Destination (Join-Path $resolvedToolsDir "gnirehtet.apk") `
        -Force

    # ============================================================
    # AVISOS LEGALES DEL ARTEFACTO INSTALABLE
    # ============================================================

    $legalDir = Join-Path $resolvedToolsDir "Legal"
    New-Item -ItemType Directory -Force -Path $legalDir | Out-Null

    $scrcpyLicense = Join-Path $scrcpyRoot.FullName "LICENSE.txt"

    Copy-RequiredFile `
        -Source $scrcpyLicense `
        -Destination (Join-Path $legalDir "Apache-2.0.txt")

    Copy-RequiredFile `
        -Source $scrcpyLicense `
        -Destination (Join-Path $legalDir "scrcpy-LICENSE.txt")

    $repoLegalFiles = @(
        @{ Source = "LICENSE"; Destination = "NOVORA-LICENSE.txt" },
        @{ Source = "COPYRIGHT.md"; Destination = "COPYRIGHT.md" },
        @{ Source = "ACKNOWLEDGEMENTS.md"; Destination = "ACKNOWLEDGEMENTS.md" },
        @{ Source = "THIRD-PARTY-NOTICES.md"; Destination = "THIRD-PARTY-NOTICES.md" },
        @{ Source = "SECURITY.md"; Destination = "SECURITY.md" },
        @{ Source = "PRIVACY.md"; Destination = "PRIVACY.md" },
        @{ Source = "docs\COPYRIGHT-COMPLIANCE.md"; Destination = "COPYRIGHT-COMPLIANCE.md" }
    )

    foreach ($entry in $repoLegalFiles) {
        Copy-RequiredFile `
            -Source (Join-Path $repoRoot $entry.Source) `
            -Destination (Join-Path $legalDir $entry.Destination)
    }

    @"
Gnirehtet $GnirehtetVersion
Copyright (C) 2017 Genymobile
Licensed under the Apache License, Version 2.0.
See Apache-2.0.txt in this directory.
Upstream: https://github.com/Genymobile/gnirehtet/tree/v$GnirehtetVersion
"@ |
        Set-Content `
            -Path (Join-Path $legalDir "gnirehtet-NOTICE.txt") `
            -Encoding UTF8

    @"
Android Debug Bridge (ADB)
Android Open Source Project
Apache-2.0 applies to the ADB module/components identified by AOSP.
Preserve any NOTICE/license files shipped with the upstream package.
See Apache-2.0.txt in this directory and:
https://android.googlesource.com/platform/packages/modules/adb/
"@ |
        Set-Content `
            -Path (Join-Path $legalDir "adb-NOTICE.txt") `
            -Encoding UTF8

    # ============================================================
    # VALIDACIÓN FINAL
    # ============================================================

    $required = @(
        "adb.exe",
        "AdbWinApi.dll",
        "AdbWinUsbApi.dll",
        "scrcpy.exe",
        "scrcpy-server",
        "gnirehtet.exe",
        "gnirehtet.apk",
        "Legal\Apache-2.0.txt",
        "Legal\scrcpy-LICENSE.txt",
        "Legal\gnirehtet-NOTICE.txt",
        "Legal\adb-NOTICE.txt",
        "Legal\NOVORA-LICENSE.txt",
        "Legal\COPYRIGHT.md",
        "Legal\ACKNOWLEDGEMENTS.md",
        "Legal\THIRD-PARTY-NOTICES.md",
        "Legal\SECURITY.md",
        "Legal\PRIVACY.md",
        "Legal\COPYRIGHT-COMPLIANCE.md"
    )

    $missing = @(
        $required |
        Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $resolvedToolsDir $_))
        }
    )

    if ($missing.Count -gt 0) {
        throw "Faltan herramientas o avisos legales: $($missing -join ', ')"
    }

    Write-Host "NOVORA Tools preparados en: $resolvedToolsDir"
    Write-Host "Avisos legales preparados en: $legalDir"
}
finally {
    Remove-Item `
        -Path $temp `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue
}
