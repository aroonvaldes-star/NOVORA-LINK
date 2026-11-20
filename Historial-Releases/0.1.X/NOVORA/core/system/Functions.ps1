# ============ Core/Functions.ps1 ============
# Funciones auxiliares de NOVORA

function Write-Log {
    param(
        [string]$message,
        [string]$level = "INFO"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[${timestamp}] [${level}] ${message}"

    # Mostrar en consola
    Write-Output $logEntry

    # Guardar en archivo
    $logFile = Join-Path $PSScriptRoot "..\logs\NOVORA.log"
    $logDir = Split-Path $logFile

    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }

    Add-Content -Path $logFile -Value $logEntry -ErrorAction SilentlyContinue
}