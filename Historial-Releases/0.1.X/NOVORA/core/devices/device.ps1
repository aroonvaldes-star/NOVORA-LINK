# ============================================================
# PROYECTO NOVORA
# Archivo : Device.ps1
# Autor   : ArDess61
# Función : Gestión del dispositivo Android
# Build   : 0.2.0 Alpha
# ============================================================

#------------------------------------------------------------
# Verificar dispositivo conectado
#------------------------------------------------------------

function Test-NovoraDevice {

    if (!(Test-NovoraADB)) {
        return $false
    }

    $Devices = & (Get-NovoraADB) devices

    foreach ($Line in $Devices) {

        if ($Line -match "device$" -and
            $Line -notmatch "List of devices") {

            return $true

        }

    }

    return $false

}

#------------------------------------------------------------
# Esperar dispositivo
#------------------------------------------------------------

function Wait-NovoraDevice {

    if (!(Test-NovoraADB)) {
        return $false
    }

    Write-NovoraInfo "Esperando dispositivo..."

    & (Get-NovoraADB) wait-for-device

    Write-NovoraSuccess "Dispositivo conectado."

    return $true

}

#------------------------------------------------------------
# Obtener propiedades
#------------------------------------------------------------

function Get-NovoraDeviceInfo {

    if (!(Test-NovoraDevice)) {
        return $null
    }

    return [PSCustomObject]@{

        Model = (& (Get-NovoraADB) shell getprop ro.product.model).Trim()

        Brand = (& (Get-NovoraADB) shell getprop ro.product.brand).Trim()

        Device = (& (Get-NovoraADB) shell getprop ro.product.device).Trim()

        Android = (& (Get-NovoraADB) shell getprop ro.build.version.release).Trim()

        SDK = (& (Get-NovoraADB) shell getprop ro.build.version.sdk).Trim()

        Manufacturer = (& (Get-NovoraADB) shell getprop ro.product.manufacturer).Trim()

        Serial = (& (Get-NovoraADB) get-serialno).Trim()

    }

}

#------------------------------------------------------------
# Mostrar información
#------------------------------------------------------------

function Show-NovoraDevice {

    $Device = Get-NovoraDeviceInfo

    if ($null -eq $Device) {

        Write-NovoraWarning "No hay dispositivo conectado."

        return

    }

    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "          DISPOSITIVO ANDROID"
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host ""

    Write-Host ("Marca............. {0}" -f $Device.Brand)
    Write-Host ("Fabricante........ {0}" -f $Device.Manufacturer)
    Write-Host ("Modelo............ {0}" -f $Device.Model)
    Write-Host ("Dispositivo....... {0}" -f $Device.Device)
    Write-Host ("Android........... {0}" -f $Device.Android)
    Write-Host ("SDK............... {0}" -f $Device.SDK)
    Write-Host ("Serial............ {0}" -f $Device.Serial)

    Write-Host ""

}

#------------------------------------------------------------
# Reiniciar dispositivo
#------------------------------------------------------------

function Restart-NovoraDevice {

    if (!(Test-NovoraDevice)) {

        Write-NovoraWarning "No hay dispositivo conectado."

        return

    }

    Write-NovoraInfo "Reiniciando dispositivo..."

    & (Get-NovoraADB) reboot

}

#------------------------------------------------------------
# Apagar dispositivo
#------------------------------------------------------------

function Shutdown-NovoraDevice {

    if (!(Test-NovoraDevice)) {

        Write-NovoraWarning "No hay dispositivo conectado."

        return

    }

    Write-NovoraInfo "Apagando dispositivo..."

    & (Get-NovoraADB) shell reboot -p

}