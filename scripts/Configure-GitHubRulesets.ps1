[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Repository = "aroonvaldes-star/NOVORA-LINK"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Command {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontró '$Name' en PATH. Instala GitHub CLI y vuelve a ejecutar el script."
    }
}

function Invoke-GhApi {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = & gh @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $message = ($output | Out-String).Trim()
        throw "GitHub CLI terminó con código $exitCode.`n$message"
    }

    return ($output | Out-String).Trim()
}

function Get-RepositoryRulesets {
    param(
        [Parameter(Mandatory)]
        [string]$Repo
    )

    $json = Invoke-GhApi -Arguments @(
        "api",
        "--method", "GET",
        "-H", "Accept: application/vnd.github+json",
        "-H", "X-GitHub-Api-Version: 2026-03-10",
        "repos/$Repo/rulesets"
    )

    if ([string]::IsNullOrWhiteSpace($json)) {
        return @()
    }

    $parsed = $json | ConvertFrom-Json
    return @($parsed)
}

function Set-RepositoryRuleset {
    param(
        [Parameter(Mandatory)]
        [string]$Repo,

        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [object[]]$ExistingRulesets
    )

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "No existe el archivo de ruleset: $FilePath"
    }

    $payload = Get-Content -LiteralPath $FilePath -Raw | ConvertFrom-Json

    if ([string]::IsNullOrWhiteSpace([string]$payload.name)) {
        throw "El ruleset '$FilePath' no contiene un nombre válido."
    }

    if ([string]::IsNullOrWhiteSpace([string]$payload.target)) {
        throw "El ruleset '$FilePath' no contiene un target válido."
    }

    $existing = @(
        $ExistingRulesets |
        Where-Object {
            $_.name -eq $payload.name -and
            $_.target -eq $payload.target
        }
    ) | Select-Object -First 1

    if ($null -ne $existing) {
        $endpoint = "repos/$Repo/rulesets/$($existing.id)"
        $description = "Actualizar ruleset '$($payload.name)' (#$($existing.id))"

        if ($PSCmdlet.ShouldProcess($Repo, $description)) {
            Write-Host "[UPDATE] $($payload.name)" -ForegroundColor Yellow

            $result = Invoke-GhApi -Arguments @(
                "api",
                "--method", "PUT",
                "-H", "Accept: application/vnd.github+json",
                "-H", "X-GitHub-Api-Version: 2026-03-10",
                $endpoint,
                "--input", $FilePath
            )

            $updated = $result | ConvertFrom-Json
            Write-Host "[OK] Ruleset actualizado. ID: $($updated.id)" -ForegroundColor Green
        }

        return
    }

    $description = "Crear ruleset '$($payload.name)'"

    if ($PSCmdlet.ShouldProcess($Repo, $description)) {
        Write-Host "[CREATE] $($payload.name)" -ForegroundColor Cyan

        $result = Invoke-GhApi -Arguments @(
            "api",
            "--method", "POST",
            "-H", "Accept: application/vnd.github+json",
            "-H", "X-GitHub-Api-Version: 2026-03-10",
            "repos/$Repo/rulesets",
            "--input", $FilePath
        )

        $created = $result | ConvertFrom-Json
        Write-Host "[OK] Ruleset creado. ID: $($created.id)" -ForegroundColor Green
    }
}

if ($Repository -notmatch '^[^/]+/[^/]+$') {
    throw "Repository debe tener formato owner/repo. Valor recibido: '$Repository'."
}

Assert-Command -Name "gh"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " NOVORA-LINK - CONFIGURAR GITHUB RULESETS" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Repositorio: $Repository"
Write-Host ""

Write-Host "Verificando autenticación de GitHub CLI..." -ForegroundColor Cyan
& gh auth status
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI no tiene una sesión válida. Ejecuta: gh auth login"
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$rulesetFiles = @(
    (Join-Path $repoRoot "rulesets\novora-link-branch.json"),
    (Join-Path $repoRoot "rulesets\novora-release-tags.json")
)

Write-Host "Leyendo Rulesets actuales..." -ForegroundColor Cyan

try {
    $existingRulesets = Get-RepositoryRulesets -Repo $Repository
}
catch {
    throw @"
No fue posible leer los Rulesets de '$Repository'.

La cuenta/token de GitHub CLI debe tener permisos de administración sobre el repositorio.
Para un fine-grained PAT, habilita:
  Repository permissions -> Administration -> Read and write

Detalle:
$($_.Exception.Message)
"@
}

foreach ($file in $rulesetFiles) {
    try {
        Set-RepositoryRuleset `
            -Repo $Repository `
            -FilePath $file `
            -ExistingRulesets $existingRulesets
    }
    catch {
        throw @"
Falló la configuración del Ruleset definido en:
$file

Comprueba que tu autenticación tenga:
  Repository permissions -> Administration -> Read and write

Detalle:
$($_.Exception.Message)
"@
    }

    # Actualizar la lista para que una segunda definición o una ejecución
    # repetida nunca cree duplicados.
    if (-not $WhatIfPreference) {
        $existingRulesets = Get-RepositoryRulesets -Repo $Repository
    }
}

Write-Host ""
Write-Host "Rulesets efectivos:" -ForegroundColor Cyan

$finalRulesets = Get-RepositoryRulesets -Repo $Repository

foreach ($ruleset in $finalRulesets) {
    Write-Host (
        "  - {0} | target={1} | enforcement={2} | id={3}" -f
        $ruleset.name,
        $ruleset.target,
        $ruleset.enforcement,
        $ruleset.id
    )
}

Write-Host ""
Write-Host "[OK] Configuración de Rulesets terminada." -ForegroundColor Green
Write-Host ""
