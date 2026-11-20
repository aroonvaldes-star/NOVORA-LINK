# ============================================================
# PROYECTO NOVORA
# Archivo : Device.ps1
# Autor   : ArDess61
# Función : Información del dispositivo Android
# ============================================================

function Test-NovoraDevice {

    $Devices = Get-NovoraADBDevices

    if ($Devices -match "device$") {
        return $true
    }

    return $false
}

function Get-NovoraDeviceModel {

    return (Get-NovoraADBProperty "ro.product.model").Trim()

}

function Get-NovoraDeviceManufacturer {

    return (Get-NovoraADBProperty "ro.product.manufacturer").Trim()

}

function Get-NovoraDeviceBrand {

    return (Get-NovoraADBProperty "ro.product.brand").Trim()

}

function Get-NovoraDeviceAndroid {

    return (Get-NovoraADBProperty "ro.build.version.release").Trim()

}

function Get-NovoraDeviceSDK {

    return (Get-NovoraADBProperty "ro.build.version.sdk").Trim()

}

function Get-NovoraDeviceSerial {

    return (Get-NovoraADBProperty "ro.serialno").Trim()

}

function Get-NovoraDeviceResolution {

    Invoke-NovoraADB @(
        "shell"
        "wm"
        "size"
    )

}

function Get-NovoraDeviceDensity {

    Invoke-NovoraADB @(
        "shell"
        "wm"
        "density"
    )

}

function Get-NovoraBattery {

    Invoke-NovoraADB @(
        "shell"
        "dumpsys"
        "battery"
    )

}

function Get-NovoraDeviceInfo {

    if (!(Test-NovoraDevice)) {

        Write-NovoraWarning "No hay ningún dispositivo conectado."

        return

    }

    [PSCustomObject]@{

        Manufacturer = Get-NovoraDeviceManufacturer

        Brand        = Get-NovoraDeviceBrand

        Model        = Get-NovoraDeviceModel

        Android      = Get-NovoraDeviceAndroid

        SDK          = Get-NovoraDeviceSDK

        Serial       = Get-NovoraDeviceSerial

        Resolution   = Get-NovoraDeviceResolution

        Density      = Get-NovoraDeviceDensity

    }

}

function Show-NovoraDevice {

    $Device = Get-NovoraDeviceInfo

    if ($null -eq $Device) {

        return

    }

    Write-Host ""
    Write-Host "=========================================" -ForegroundColor Cyan
    Write-Host "          DISPOSITIVO ANDROID"
    Write-Host "=========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Fabricante : $($Device.Manufacturer)"
    Write-Host "Marca      : $($Device.Brand)"
    Write-Host "Modelo     : $($Device.Model)"
    Write-Host "Android    : $($Device.Android)"
    Write-Host "SDK        : $($Device.SDK)"
    Write-Host "Serie      : $($Device.Serial)"
    Write-Host "Resolución : $($Device.Resolution)"
    Write-Host "Densidad   : $($Device.Density)"
    Write-Host ""
}