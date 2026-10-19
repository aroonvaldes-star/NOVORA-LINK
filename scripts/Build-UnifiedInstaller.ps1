[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$ComplianceDir,
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputBaseFilename = "NOVORA-Setup-$Version",
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+(?:\.\d+)?(?:-pre\.\d+)?$') {
    throw "Versión no válida: $Version"
}
if ($OutputBaseFilename -notmatch '^[a-zA-Z0-9._-]+$') {
    throw 'El nombre del instalador contiene caracteres no válidos.'
}

$publish = [IO.Path]::GetFullPath($PublishDir)
$compliance = [IO.Path]::GetFullPath($ComplianceDir)
$required = @(
    'NOVORA-LINK.exe', 'hostfxr.dll', 'hostpolicy.dll',
    'Android\NLAndroidApp.apk', 'Android\NLAndroidRelease.json',
    'Tools\adb.exe',
    'Tools\LinkEngine\LENetworkRelay.exe',
    'Tools\Legal\THIRD-PARTY-SOURCE-NOTES.txt',
    'Tools\Legal\FFmpeg-LGPL-2.1.txt',
    'Tools\Legal\SDL3-zlib-LICENSE.txt',
    'Tools\Legal\dav1d-BSD-2-Clause.txt',
    'Tools\Legal\tcp_ip-Apache-2.0-LICENSE.txt'
)
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) {
        throw "Falta archivo del instalador: $name"
    }
}

foreach ($name in @('Tools\scrcpy.exe', 'Tools\scrcpy-server',
        'Tools\gnirehtet.exe', 'Tools\gnirehtet.apk')) {
    if (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf) {
        throw "El instalador incluye una herramienta retirada: $name"
    }
}

$descriptor = Get-Content -LiteralPath (Join-Path $publish 'Android\NLAndroidRelease.json') -Raw | ConvertFrom-Json
$apkHash = (Get-FileHash -LiteralPath (Join-Path $publish 'Android\NLAndroidApp.apk') -Algorithm SHA256).Hash
if ($descriptor.PackageId -ne 'com.novora.appcontrol' -or $descriptor.Sha256 -ne $apkHash) {
    throw 'La APK no coincide con el descriptor de Release integrado.'
}

$archives = @{
    'ffmpeg-8.1.2.tar.xz' = '464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c'
}
foreach ($archive in $archives.Keys) {
    $path = Join-Path $compliance $archive
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $archives[$archive]) {
        throw "Fuente legal ausente o con SHA-256 incorrecto: $archive"
    }
}

if (-not $IsccPath) {
    $IsccPath = Get-ChildItem 'C:\Program Files\Inno Setup *\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup *\ISCC.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'No se encontró ISCC.exe de Inno Setup.'
}

$numericVersion = ($Version -replace '-.*$', '')
$versionParts = $numericVersion.Split('.')
$fileVersion = if ($versionParts.Count -eq 2) { "$numericVersion.0.0" } else { "$numericVersion.0" }
$installerScript = Join-Path $PSScriptRoot '..\Installer\NOVORA.Installer.iss'

& $IsccPath "/DAppVersion=$Version" "/DVersionInfoVersion=$fileVersion" `
    "/DPublishDir=$publish" "/DComplianceDir=$compliance" `
    "/DSetupBaseFilename=$OutputBaseFilename" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup terminó con código $LASTEXITCODE." }

$installer = Join-Path $PSScriptRoot "..\Installer\output\$OutputBaseFilename.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "No se generó el instalador: $installer"
}
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$hash | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii -NoNewline
Write-Host "Instalador único: $installer"
Write-Host "SHA-256: $hash"
