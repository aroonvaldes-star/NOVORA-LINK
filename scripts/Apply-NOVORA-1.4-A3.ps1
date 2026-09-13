#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$RepoPath = 'C:\Users\Aroon\Desktop\NOVORA-LINK',
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$PatchRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$PayloadRoot = Join-Path $PatchRoot 'payload'
$RepoPath = [IO.Path]::GetFullPath($RepoPath)

function Write-Section([string]$Text) {
    Write-Host ''
    Write-Host ('=' * 64) -ForegroundColor Cyan
    Write-Host " $Text" -ForegroundColor Cyan
    Write-Host ('=' * 64) -ForegroundColor Cyan
}

function Get-RelativePathNV([string]$Path, [string]$Base) {
    return $Path.Substring($Base.Length).TrimStart('\','/')
}

if (-not (Test-Path -LiteralPath (Join-Path $RepoPath 'NOVORA.sln') -PathType Leaf)) {
    throw "No parece un repositorio NOVORA-LINK válido: $RepoPath"
}
if (-not (Test-Path -LiteralPath $PayloadRoot -PathType Container)) {
    throw "No encontré el payload del parche: $PayloadRoot"
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $env:TEMP "NOVORA-1.4-A3-PATCH-$stamp"
$backupFiles = Join-Path $backup 'files'
$stateFile = Join-Path $backup 'PATCH-STATE.txt'
New-Item -ItemType Directory -Path $backupFiles -Force | Out-Null

$createdFiles = New-Object 'System.Collections.Generic.List[string]'
$backedUpFiles = New-Object 'System.Collections.Generic.List[string]'
$removedFiles = New-Object 'System.Collections.Generic.List[string]'

$legacyFiles = @(
    'src\NOVORA\LinkEngine\Device\PollingDeviceLE.cs',
    'src\NOVORA\Services\PollingCenter.cs',
    'src\NOVORA\Services\PollingCoordinatorService.cs',
    'src\NOVORA\LinkEngine\Device\PollingDeviceLE.txt',
    'src\NOVORA\LinkEngine\Device\ProvisioningDeviceLE.txt',
    'src\NOVORA\LinkEngine\Failover\AdbFailoverLE.txt'
)

function Backup-ExistingNV([string]$Relative) {
    $source = Join-Path $RepoPath $Relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        return $false
    }

    $destination = Join-Path $backupFiles $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $backedUpFiles.Add($Relative)
    return $true
}

function Rollback-NV {
    Write-Section 'ROLLBACK NOVORA-LINK 1.4 A3'

    foreach ($relative in $createdFiles) {
        $path = Join-Path $RepoPath $relative
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }

    $backupItems = Get-ChildItem -LiteralPath $backupFiles -File -Recurse -ErrorAction SilentlyContinue
    foreach ($file in $backupItems) {
        $relative = Get-RelativePathNV -Path $file.FullName -Base $backupFiles
        $destination = Join-Path $RepoPath $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }

    Write-Host '[OK] Estado anterior restaurado.' -ForegroundColor Yellow
    Write-Host "Backup conservado: $backup" -ForegroundColor DarkGray
}

try {
    Write-Section 'APLICANDO NOVORA-LINK 1.4 A3'

    $files = Get-ChildItem -LiteralPath $PayloadRoot -File -Recurse
    foreach ($file in $files) {
        $relative = Get-RelativePathNV -Path $file.FullName -Base $PayloadRoot
        $destination = Join-Path $RepoPath $relative
        $destinationDir = Split-Path -Parent $destination

        $existed = Backup-ExistingNV -Relative $relative
        if (-not $existed) {
            $createdFiles.Add($relative)
        }

        New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        Write-Host "[OK] $relative" -ForegroundColor Green
    }

    foreach ($relative in $legacyFiles) {
        $path = Join-Path $RepoPath $relative
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            [void](Backup-ExistingNV -Relative $relative)
            Remove-Item -LiteralPath $path -Force
            $removedFiles.Add($relative)
            Write-Host "[DEL] $relative" -ForegroundColor DarkYellow
        }
    }

    @(
        'NOVORA-LINK 1.4 A3 patch state',
        "Applied: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "Repo: $RepoPath",
        '',
        'Backed up:',
        ($backedUpFiles | Sort-Object),
        '',
        'Created:',
        ($createdFiles | Sort-Object),
        '',
        'Removed legacy:',
        ($removedFiles | Sort-Object)
    ) | Set-Content -LiteralPath $stateFile -Encoding UTF8

    if ($Verify) {
        Write-Section 'VERIFICACIÓN COMPLETA'
        $verifyScript = Join-Path $RepoPath 'scripts\Verify-NOVORA-Repository.ps1'
        if (-not (Test-Path -LiteralPath $verifyScript -PathType Leaf)) {
            throw "No encontré el verificador: $verifyScript"
        }

        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $verifyScript -RepoPath $RepoPath
        if ($LASTEXITCODE -ne 0) {
            throw "La verificación terminó con código $LASTEXITCODE."
        }
    }

    Write-Section 'RESULTADO'
    Write-Host '[OK] NOVORA-LINK 1.4 A3 aplicado.' -ForegroundColor Green
    Write-Host "Backup transaccional: $backup" -ForegroundColor DarkGray
    Write-Host '[INFO] RemoteNV usa protocolo v2 con token efímero de sesión.' -ForegroundColor Cyan
    Write-Host '[IMPORTANTE] Recompila el APK Android A3 antes de probar Remote Android.' -ForegroundColor Yellow
}
catch {
    $message = $_.Exception.Message
    Write-Host ''
    Write-Host "[FAIL] $message" -ForegroundColor Red
    Rollback-NV
    throw
}
