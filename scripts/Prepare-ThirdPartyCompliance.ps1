[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDir,

    [string]$OutputDir = ''
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $versionsRoot = if ($env:NOVORA_VERSIONES_DIR) { $env:NOVORA_VERSIONES_DIR } else { Join-Path $PSScriptRoot '..\..\..\Versiones' }
    $OutputDir = Join-Path $versionsRoot 'Generados\NOVORA-LINK\compliance'
}

function Get-VerifiedFile {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$ExpectedSha256
    )

    if (Test-Path -LiteralPath $Destination -PathType Leaf) {
        $existing = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existing -eq $ExpectedSha256.ToLowerInvariant()) {
            Write-Host "Fuente verificada en caché: $Destination"
            return
        }
    }

    Write-Host "Descargando: $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing

    $actual = (Get-FileHash -Path $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = $ExpectedSha256.ToLowerInvariant()

    if ($actual -ne $expected) {
        throw "SHA-256 inválido para $Uri. Esperado: $expected. Obtenido: $actual"
    }
}

function Copy-FirstMatchingLicense {
    param(
        [Parameter(Mandatory)][string]$Archive,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Candidates,
        [Parameter(Mandatory)][string]$Destination
    )

    $entries = & tar -tf $Archive
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo listar $Archive con tar."
    }
    foreach ($candidate in $Candidates) {
        $match = $entries |
            Where-Object { $_ -notmatch '(^|/)\.\.(/|$)' -and
                [IO.Path]::GetFileName($_) -eq $candidate } |
            Select-Object -First 1

        if ($match) {
            & tar -xf $Archive -C $Root -- $match
            if ($LASTEXITCODE -ne 0) {
                throw "No se pudo extraer la licencia $match de $Archive."
            }
            Copy-Item -LiteralPath (Join-Path $Root $match.Replace('/', [IO.Path]::DirectorySeparatorChar)) -Destination $Destination -Force
            return
        }
    }

    throw "No se encontró una licencia esperada en $Archive. Candidatos: $($Candidates -join ', ')"
}

$resolvedPublishDir = [IO.Path]::GetFullPath($PublishDir)
$resolvedOutputDir = [IO.Path]::GetFullPath($OutputDir)
$legalDir = Join-Path $resolvedPublishDir "Tools\Legal"
$tcpIpLicense = Join-Path $PSScriptRoot "..\src\NOVORA\LinkEngine\Network\Native\NovoraRelay\tcp_ip-LICENSE.txt"

if (-not (Test-Path -LiteralPath $resolvedPublishDir -PathType Container)) {
    throw "PublishDir no existe: $resolvedPublishDir"
}

New-Item -ItemType Directory -Force -Path $resolvedOutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $legalDir | Out-Null
if (-not (Test-Path -LiteralPath $tcpIpLicense -PathType Leaf)) {
    throw "Falta licencia del stack IP del relay: $tcpIpLicense"
}
Copy-Item -LiteralPath $tcpIpLicense -Destination (Join-Path $legalDir "tcp_ip-Apache-2.0-LICENSE.txt") -Force

$dependencies = @(
    @{
        Name = "FFmpeg"
        Version = "8.1.2"
        Uri = "https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz"
        Sha256 = "464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c"
        Archive = "ffmpeg-8.1.2.tar.xz"
        LicenseCandidates = @("COPYING.LGPLv2.1")
        LicenseOutput = "FFmpeg-LGPL-2.1.txt"
        PublishSource = $true
    },
    @{
        Name = "SDL3"
        Version = "3.4.12"
        Uri = "https://github.com/libsdl-org/SDL/archive/refs/tags/release-3.4.12.tar.gz"
        Sha256 = "b68381f06a7580e63400b3b6eb547ec57d8c3ebde70f9f40e0aba530ba05da27"
        Archive = "SDL-3.4.12.tar.gz"
        LicenseCandidates = @("LICENSE.txt", "LICENSE")
        LicenseOutput = "SDL3-zlib-LICENSE.txt"
        PublishSource = $false
    },
    @{
        Name = "dav1d"
        Version = "1.5.3"
        Uri = "https://code.videolan.org/videolan/dav1d/-/archive/1.5.3/dav1d-1.5.3.tar.gz"
        Sha256 = "cbe212b02faf8c6eed5b6d55ef8a6e363aaab83f15112e960701a9c3df813686"
        Archive = "dav1d-1.5.3.tar.gz"
        LicenseCandidates = @("COPYING", "LICENSE")
        LicenseOutput = "dav1d-BSD-2-Clause.txt"
        PublishSource = $false
    }
)

$temp = Join-Path ([IO.Path]::GetTempPath()) ("novora-compliance-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

$notes = New-Object System.Collections.Generic.List[string]
$notes.Add("NOVORA-LINK third-party source and license bundle")
$notes.Add("NOVORA redistributes FFmpeg and SDL3 for video, audio and gamepad support.")
$notes.Add("NOVORA LinkEngine relay uses the tcp_ip stack under Apache-2.0; its license is installed as Tools/Legal/tcp_ip-Apache-2.0-LICENSE.txt.")
$notes.Add("")

try {
    foreach ($dependency in $dependencies) {
        $archivePath = Join-Path $resolvedOutputDir $dependency.Archive

        Get-VerifiedFile `
            -Uri $dependency.Uri `
            -Destination $archivePath `
            -ExpectedSha256 $dependency.Sha256

        $extractDir = Join-Path $temp ($dependency.Name + "-" + $dependency.Version)
        New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

        Copy-FirstMatchingLicense `
            -Archive $archivePath `
            -Root $extractDir `
            -Candidates $dependency.LicenseCandidates `
            -Destination (Join-Path $legalDir $dependency.LicenseOutput)

        $notes.Add("$($dependency.Name) $($dependency.Version)")
        $notes.Add("Source: $($dependency.Uri)")
        $notes.Add("SHA-256: $($dependency.Sha256)")
        $notes.Add("License copy installed as: Tools/Legal/$($dependency.LicenseOutput)")
        $notes.Add("")

        if (-not $dependency.PublishSource) {
            Remove-Item -LiteralPath $archivePath -Force
        }
    }

    $notes.Add("The FFmpeg source archive is published beside the NOVORA installer for the redistributed FFmpeg DLLs.")

    $notesPath = Join-Path $resolvedOutputDir "THIRD-PARTY-SOURCE-NOTES.txt"
    $notes | Set-Content -Path $notesPath -Encoding UTF8
    Copy-Item -LiteralPath $notesPath -Destination (Join-Path $legalDir "THIRD-PARTY-SOURCE-NOTES.txt") -Force

    $requiredLegal = @(
        "FFmpeg-LGPL-2.1.txt",
        "SDL3-zlib-LICENSE.txt",
        "dav1d-BSD-2-Clause.txt",
        "tcp_ip-Apache-2.0-LICENSE.txt",
        "THIRD-PARTY-SOURCE-NOTES.txt"
    )

    foreach ($file in $requiredLegal) {
        $path = Join-Path $legalDir $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Aviso/licencia legal ausente: $path"
        }
    }

    foreach ($sourceArchive in @("ffmpeg-8.1.2.tar.xz")) {
        $path = Join-Path $resolvedOutputDir $sourceArchive
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Fuente LGPL requerida ausente: $path"
        }
    }

    Write-Host "Bundle legal preparado en: $legalDir"
    Write-Host "Fuentes LGPL preparadas en: $resolvedOutputDir"
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
