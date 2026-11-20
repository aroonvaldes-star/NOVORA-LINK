# ============================================================
# PROYECTO NOVORA
# Archivo : ADB.ps1
# Autor   : ArDess61
# Función : Gestión completa de ADB
# ============================================================

#------------------------------------------------------------
# Ruta de ADB
#------------------------------------------------------------

$Global:ADB = @{
    Path = Join-Path $Global:NovoraRoot "tools\adb\adb.exe"
}

#------------------------------------------------------------
# Verificar ADB
#------------------------------------------------------------

function Test-NovoraADB {

    return (Test-Path $Global:ADB.Path)

}

#------------------------------------------------------------
# Iniciar servidor
#------------------------------------------------------------

function Start-NovoraADB {

    if (!(Test-NovoraADB)) {

        Write-NovoraError "ADB no encontrado."

        return $false

    }

    & $Global:ADB.Path start-server | Out-Null

    Write-NovoraSuccess "Servidor ADB iniciado."

    return $true

}

#------------------------------------------------------------
# Detener servidor
#------------------------------------------------------------

function Stop-NovoraADB {

    if (!(Test-NovoraADB)) {

        Write-NovoraError "ADB no encontrado."

        return

    }

    & $Global:ADB.Path kill-server | Out-Null

    Write-NovoraInfo "Servidor ADB detenido."

}

#------------------------------------------------------------
# Reiniciar servidor
#------------------------------------------------------------

function Restart-NovoraADB {

    Stop-NovoraADB

    Start-Sleep -Seconds 1

    Start-NovoraADB

}

#------------------------------------------------------------
# Obtener dispositivos
#------------------------------------------------------------

function Get-NovoraADBDevices {

    if (!(Test-NovoraADB)) {

        return @()

    }

    return (& $Global:ADB.Path devices)

}

#------------------------------------------------------------
# Esperar dispositivo
#------------------------------------------------------------

function Wait-NovoraADBDevice {

    if (!(Test-NovoraADB)) {

        return

    }

    & $Global:ADB.Path wait-for-device

}

#------------------------------------------------------------
# Ejecutar comando ADB
#------------------------------------------------------------

function Invoke-NovoraADB {

    param(

        [Parameter(Mandatory)]

        [string[]]$Arguments

    )

    if (!(Test-NovoraADB)) {

        Write-NovoraError "ADB no encontrado."

        return

    }

    & $Global:ADB.Path @Arguments

}

#------------------------------------------------------------
# Obtener propiedades
#------------------------------------------------------------

function Get-NovoraADBProperty {

    param(

        [Parameter(Mandatory)]

        [string]$Property

    )

    Invoke-NovoraADB @(
        "shell"
        "getprop"
        $Property
    )

}

#------------------------------------------------------------
# Reiniciar dispositivo
#------------------------------------------------------------

function Restart-NovoraDevice {

    Invoke-NovoraADB @(
        "reboot"
    )

}

#------------------------------------------------------------
# Reiniciar Recovery
#------------------------------------------------------------

function Restart-NovoraRecovery {

    Invoke-NovoraADB @(
        "reboot"
        "recovery"
    )

}

#------------------------------------------------------------
# Reiniciar Bootloader
#------------------------------------------------------------

function Restart-NovoraBootloader {

    Invoke-NovoraADB @(
        "reboot"
        "bootloader"
    )

}

#------------------------------------------------------------
# Estado del servidor
#------------------------------------------------------------

function Get-NovoraADBStatus {

    return @{

        Installed = Test-NovoraADB

        Devices = Get-NovoraADBDevices

    }

}