# ============================================================
# PROYECTO NOVORA
# Archivo : Monitor.ps1
# Autor   : ArDess61
# Función : Monitor del sistema
# Build   : 0.2.0 Alpha
# ============================================================

$Global:NovoraMonitor = [PSCustomObject]@{

    Started = Get-Date

    Status = "Stopped"

    LastCheck = $null

    DeviceConnected = $false

    ADBRunning = $false

    ScrcpyRunning = $false

    GnirehtetRunning = $false

}

#------------------------------------------------------------
# Inicializar
#------------------------------------------------------------

function Initialize-NovoraMonitor {

    $Global:NovoraMonitor.Started = Get-Date

    $Global:NovoraMonitor.Status = "Ready"

}

#------------------------------------------------------------
# Actualizar estado
#------------------------------------------------------------

function Update-NovoraMonitor {

    $Global:NovoraMonitor.LastCheck = Get-Date

    $Global:NovoraMonitor.ADBRunning =
        Test-NovoraADB

    $Global:NovoraMonitor.DeviceConnected =
        Test-NovoraDevice

    $Global:NovoraMonitor.ScrcpyRunning =
        (Get-NovoraScrcpyStatus).Running

    $Global:NovoraMonitor.GnirehtetRunning =
        (Get-NovoraGnirehtetStatus).Running

}

#------------------------------------------------------------
# Obtener estado
#------------------------------------------------------------

function Get-NovoraMonitor {

    Update-NovoraMonitor

    return $Global:NovoraMonitor

}

#------------------------------------------------------------
# Mostrar estado
#------------------------------------------------------------

function Show-NovoraMonitor {

    $Monitor = Get-NovoraMonitor

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "           MONITOR NOVORA"
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    Write-Host ("Estado.............. {0}" -f $Monitor.Status)
    Write-Host ("ADB................. {0}" -f $Monitor.ADBRunning)
    Write-Host ("Dispositivo......... {0}" -f $Monitor.DeviceConnected)
    Write-Host ("scrcpy.............. {0}" -f $Monitor.ScrcpyRunning)
    Write-Host ("Gnirehtet........... {0}" -f $Monitor.GnirehtetRunning)
    Write-Host ("Última revisión..... {0}" -f $Monitor.LastCheck)

    Write-Host ""

}

#------------------------------------------------------------
# Cambiar estado
#------------------------------------------------------------

function Set-NovoraMonitorStatus {

    param(

        [Parameter(Mandatory)]
        [string]$Status

    )

    $Global:NovoraMonitor.Status = $Status

}