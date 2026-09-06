[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [Parameter(Mandatory = $false)]
    [string]$RepoPath = (Split-Path -Parent $PSScriptRoot),

    [switch]$IncludeDiagnostics
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoPath = [IO.Path]::GetFullPath($RepoPath)

if (-not (Test-Path -LiteralPath $RepoPath -PathType Container)) {
    throw "No existe la carpeta: $RepoPath"
}

$Solution = Join-Path $RepoPath "NOVORA.sln"
$WindowsProject = Join-Path $RepoPath "src\NOVORA\NOVORA.csproj"
$AndroidProject = Join-Path $RepoPath "NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj"

foreach ($Required in @($Solution, $WindowsProject, $AndroidProject)) {
    if (-not (Test-Path -LiteralPath $Required -PathType Leaf)) {
        throw "La carpeta no parece ser NOVORA-LINK: falta $Required"
    }
}

function Remove-SafePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $Full = [IO.Path]::GetFullPath($Path)
    if (-not $Full.StartsWith($RepoPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Protección activada: ruta fuera del repo: $Full"
    }

    if ($PSCmdlet.ShouldProcess($Full, "Eliminar ($Reason)")) {
        Remove-Item -LiteralPath $Full -Recurse -Force -ErrorAction Stop
        Write-Host "[REMOVED] $Full" -ForegroundColor DarkGray
    }
}

Write-Host "" 
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " NOVORA-LINK - LIMPIEZA SEGURA DEL WORKSPACE" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Repo: $RepoPath"
Write-Host ""

# Directorios de IDE y build totalmente reproducibles.
$GeneratedDirectories = @(
    (Join-Path $RepoPath ".vs"),
    (Join-Path $RepoPath "src\NOVORA\bin"),
    (Join-Path $RepoPath "src\NOVORA\obj"),
    (Join-Path $RepoPath "NOVORA.linkEngine.Android\bin"),
    (Join-Path $RepoPath "NOVORA.linkEngine.Android\obj"),
    (Join-Path $RepoPath "src\NOVORA\LinkEngine\Network\Native\RelayCore\target"),
    (Join-Path $RepoPath "Installer\output"),
    (Join-Path $RepoPath "artifacts")
)

foreach ($Path in $GeneratedDirectories) {
    Remove-SafePath -Path $Path -Reason "build/cache reproducible"
}

# Backups históricos creados durante el desarrollo. El código activo está fuera de estas rutas.
$BackupPatterns = @(
    "Backups",
    "_backup_*",
    "_NOVORA_BACKUP_*",
    "Checkpoints"
)

foreach ($Pattern in $BackupPatterns) {
    Get-ChildItem -LiteralPath $RepoPath -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like $Pattern } |
        ForEach-Object { Remove-SafePath -Path $_.FullName -Reason "backup/checkpoint histórico" }
}

if ($IncludeDiagnostics) {
    Remove-SafePath -Path (Join-Path $RepoPath "Diagnostics") -Reason "diagnósticos generados"
}

# Copias de seguridad o archivos temporales dentro del árbol activo.
$BackupFiles = Get-ChildItem -LiteralPath $RepoPath -File -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -match '\.backup_\d+' -or
        $_.Name -match '\.fixbackup_\d+' -or
        $_.Extension -in @('.tmp', '.temp', '.bak', '.old', '.orig', '.rej') -or
        $_.Name.EndsWith('~')
    }

foreach ($File in $BackupFiles) {
    Remove-SafePath -Path $File.FullName -Reason "archivo temporal/backup"
}

# Archivos de usuario de Visual Studio no deben viajar con el proyecto.
Get-ChildItem -LiteralPath $RepoPath -Filter "*.csproj.user" -File -Recurse -Force -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-SafePath -Path $_.FullName -Reason "configuración local de Visual Studio" }

Write-Host ""
Write-Host "Limpieza terminada." -ForegroundColor Green
Write-Host "No se eliminaron src, proyectos, APK firmado, relay, ADB, scrcpy, assets, docs ni tests." -ForegroundColor Green
