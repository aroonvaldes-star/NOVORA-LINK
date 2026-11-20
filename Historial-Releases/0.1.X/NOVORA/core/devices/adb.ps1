# ============================================================
# PROYECTO NOVORA
# Archivo : ADB.ps1
# Autor   : ArDess61
# Función : Gestión de Android Debug Bridge
# Build   : 0.3.0 Alpha
# ============================================================

#------------------------------------------------------------
# Configuración
#------------------------------------------------------------

$Global:NovoraADB = [PSCustomObject]@{

    Executable = Join-Path $Global:ToolsPath "adb\adb.exe"

    Running    = $false

}

#------------------------------------------------------------
# Obtener ruta
#------------------------------------------------------------

function Get-NovoraADB {

    return $Global:NovoraADB.Executable

}

#------------------------------------------------------------
# Verificar instalación
#------------------------------------------------------------

function Test-NovoraADB {

    return (Test-Path $Global:NovoraADB.Executable)

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

        return $null

    }

    return & $Global:NovoraADB.Executable @Arguments

}

#------------------------------------------------------------
# Iniciar servidor
#------------------------------------------------------------

function Start-NovoraADB {

    if (!(Test-NovoraADB)) {

        return $false

    }

    Write-NovoraInfo "Iniciando servidor ADB..."

    Invoke-NovoraADB @("start-server") | Out-Null

    Start-Sleep -Milliseconds 500

    $Global:NovoraADB.Running = $true

    Write-NovoraSuccess "Servidor iniciado."

    return $true

}

#------------------------------------------------------------
# Detener servidor
#------------------------------------------------------------

function Stop-NovoraADB {

    if (!(Test-NovoraADB)) {

        return

    }

    Write-NovoraInfo "Deteniendo servidor ADB..."

    Invoke-NovoraADB @("kill-server") | Out-Null

    $Global:NovoraADB.Running = $false

    Write-NovoraSuccess "Servidor detenido."

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
# Cambiar a USB
#------------------------------------------------------------

function Set-NovoraADBUSB {

    if (!(Test-NovoraADB)) {

        return

    }

    Invoke-NovoraADB @("usb") | Out-Null

}

#------------------------------------------------------------
# Cambiar a TCP/IP
#------------------------------------------------------------

function Set-NovoraADBTCP {

    param(

        [int]$Port = 5555

    )

    if (!(Test-NovoraADB)) {

        return

    }

    Invoke-NovoraADB @("tcpip", $Port) | Out-Null

}

#------------------------------------------------------------
# Esperar dispositivo
#------------------------------------------------------------

function Wait-NovoraADBDevice {

    if (!(Test-NovoraADB)) {

        return $false

    }

    Write-NovoraInfo "Esperando dispositivo..."

    Invoke-NovoraADB @("wait-for-device") | Out-Null

    Write-NovoraSuccess "Dispositivo detectado."

    return $true

}

#------------------------------------------------------------
# Obtener dispositivos
#------------------------------------------------------------

function Get-NovoraADBDevices {

    if (!(Test-NovoraADB)) {

        return @()

    }

    return Invoke-NovoraADB @("devices")

}

#------------------------------------------------------------
# Ejecutar shell
#------------------------------------------------------------

function Invoke-NovoraADBShell {

    param(

        [Parameter(Mandatory)]
        [string]$Command

    )

    return Invoke-NovoraADB @("shell", $Command)

}

#------------------------------------------------------------
# Enviar archivo
#------------------------------------------------------------

function Send-NovoraADBFile {

    param(

        [Parameter(Mandatory)]
        [string]$Source,

        [Parameter(Mandatory)]
        [string]$Destination

    )

    Invoke-NovoraADB @("push", $Source, $Destination)

}

#------------------------------------------------------------
# Recibir archivo
#------------------------------------------------------------

function Receive-NovoraADBFile {

    param(

        [Parameter(Mandatory)]
        [string]$Source,

        [Parameter(Mandatory)]
        [string]$Destination

    )

    Invoke-NovoraADB @("pull", $Source, $Destination)

}

#------------------------------------------------------------
# Instalar APK
#------------------------------------------------------------

function Install-NovoraAPK {

    param(

        [Parameter(Mandatory)]
        [string]$APK

    )

    Invoke-NovoraADB @("install", $APK)

}

#------------------------------------------------------------
# Desinstalar paquete
#------------------------------------------------------------

function Uninstall-NovoraPackage {

    param(

        [Parameter(Mandatory)]
        [string]$Package

    )

    Invoke-NovoraADB @("uninstall", $Package)

}

#------------------------------------------------------------
# Reiniciar dispositivo
#------------------------------------------------------------

function Restart-NovoraADBDevice {

    Invoke-NovoraADB @("reboot")

}

#------------------------------------------------------------
# Estado
#------------------------------------------------------------

function Get-NovoraADBStatus {

    $Installed = Test-NovoraADB

    $Running = $false

    if ($Installed) {

        $Processes = Get-Process `
            -Name "adb" `
            -ErrorAction SilentlyContinue

        $Running = ($Processes.Count -gt 0)

    }

    $Global:NovoraADB.Running = $Running

    return [PSCustomObject]@{

        Installed = $Installed

        Running = $Running

        Path = $Global:NovoraADB.Executable

    }

}