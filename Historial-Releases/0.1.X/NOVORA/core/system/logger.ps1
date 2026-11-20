# ============================================================
# PROYECTO NOVORA
# Archivo : Logger.ps1
# Autor   : ArDess61
# Función : Registro de eventos
# ============================================================

#------------------------------------------------------------
# Archivo de Log
#------------------------------------------------------------

$Global:LogFile = Join-Path $Global:LogsPath "latest.log"

#------------------------------------------------------------
# Inicializar Logger
#------------------------------------------------------------

function Initialize-Logger {

    if (!(Test-Path $Global:LogsPath)) {

        New-Item `
            -ItemType Directory `
            -Path $Global:LogsPath `
            -Force | Out-Null

    }

    if (!(Test-Path $Global:LogFile)) {

        New-Item `
            -ItemType File `
            -Path $Global:LogFile `
            -Force | Out-Null

    }

}

#------------------------------------------------------------
# Escribir en Log
#------------------------------------------------------------

function Write-NovoraLog {

    param(

        [Parameter(Mandatory)]
        [ValidateSet("INFO","SUCCESS","WARNING","ERROR")]
        [string]$Level,

        [Parameter(Mandatory)]
        [string]$Message

    )

    $Time = Get-Date -Format "yyyy-MM-dd HH:mm:ss"

    $Line = "[$Time] [$Level] $Message"

    Add-Content `
        -Path $Global:LogFile `
        -Value $Line

}

#------------------------------------------------------------
# INFO
#------------------------------------------------------------

function Write-NovoraInfo {

    param([string]$Message)

    Write-Host "[INFO] $Message" -ForegroundColor Cyan

    Write-NovoraLog `
        -Level "INFO" `
        -Message $Message

}

#------------------------------------------------------------
# SUCCESS
#------------------------------------------------------------

function Write-NovoraSuccess {

    param([string]$Message)

    Write-Host "[SUCCESS] $Message" -ForegroundColor Green

    Write-NovoraLog `
        -Level "SUCCESS" `
        -Message $Message

}

#------------------------------------------------------------
# WARNING
#------------------------------------------------------------

function Write-NovoraWarning {

    param([string]$Message)

    Write-Host "[WARNING] $Message" -ForegroundColor Yellow

    Write-NovoraLog `
        -Level "WARNING" `
        -Message $Message

}

#------------------------------------------------------------
# ERROR
#------------------------------------------------------------

function Write-NovoraError {

    param([string]$Message)

    Write-Host "[ERROR] $Message" -ForegroundColor Red

    Write-NovoraLog `
        -Level "ERROR" `
        -Message $Message

}