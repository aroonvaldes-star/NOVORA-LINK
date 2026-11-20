# ============================================================
# PROYECTO NOVORA
# Archivo : NirCmd.ps1
# Autor   : ArDess61
# Función : Gestión de NirCmd
# Build   : 0.3.0 Alpha
# ============================================================

#------------------------------------------------------------
# Configuración
#------------------------------------------------------------

$Global:NovoraNirCmd = [PSCustomObject]@{

    Executable = Join-Path $Global:ToolsPath "nircmd\nircmd.exe"

    Running = $false

}

#------------------------------------------------------------
# Verificar instalación
#------------------------------------------------------------

function Test-NovoraNirCmd {

    return (Test-Path $Global:NovoraNirCmd.Executable)

}

#------------------------------------------------------------
# Obtener ruta
#------------------------------------------------------------

function Get-NovoraNirCmd {

    return $Global:NovoraNirCmd.Executable

}

#------------------------------------------------------------
# Ejecutar comando
#------------------------------------------------------------

function Invoke-NovoraNirCmd {

    param(

        [Parameter(Mandatory)]
        [string[]]$Arguments

    )

    if (!(Test-NovoraNirCmd)) {

        Write-NovoraError "NirCmd no encontrado."

        return $false

    }

    & $Global:NovoraNirCmd.Executable @Arguments

    return $true

}

#------------------------------------------------------------
# Cambiar volumen
#------------------------------------------------------------

function Set-NovoraVolume {

    param(

        [Parameter(Mandatory)]
        [ValidateRange(0,65535)]
        [int]$Level

    )

    Invoke-NovoraNirCmd @(
        "setsysvolume"
        $Level
    )

}

#------------------------------------------------------------
# Incrementar volumen
#------------------------------------------------------------

function Add-NovoraVolume {

    param(

        [int]$Step = 5000

    )

    Invoke-NovoraNirCmd @(
        "changesysvolume"
        $Step
    )

}

#------------------------------------------------------------
# Disminuir volumen
#------------------------------------------------------------

function Remove-NovoraVolume {

    param(

        [int]$Step = -5000

    )

    Invoke-NovoraNirCmd @(
        "changesysvolume"
        $Step
    )

}

#------------------------------------------------------------
# Silenciar
#------------------------------------------------------------

function Disable-NovoraVolume {

    Invoke-NovoraNirCmd @(
        "mutesysvolume"
        "1"
    )

}

#------------------------------------------------------------
# Activar sonido
#------------------------------------------------------------

function Enable-NovoraVolume {

    Invoke-NovoraNirCmd @(
        "mutesysvolume"
        "0"
    )

}

#------------------------------------------------------------
# Ocultar ventana
#------------------------------------------------------------

function Hide-NovoraWindow {

    param(

        [Parameter(Mandatory)]
        [string]$Title

    )

    Invoke-NovoraNirCmd @(
        "win"
        "hide"
        "title"
        $Title
    )

}

#------------------------------------------------------------
# Mostrar ventana
#------------------------------------------------------------

function Show-NovoraWindow {

    param(

        [Parameter(Mandatory)]
        [string]$Title

    )

    Invoke-NovoraNirCmd @(
        "win"
        "show"
        "title"
        $Title
    )

}

#------------------------------------------------------------
# Minimizar ventana
#------------------------------------------------------------

function Minimize-NovoraWindow {

    param(

        [Parameter(Mandatory)]
        [string]$Title

    )

    Invoke-NovoraNirCmd @(
        "win"
        "min"
        "title"
        $Title
    )

}

#------------------------------------------------------------
# Restaurar ventana
#------------------------------------------------------------

function Restore-NovoraWindow {

    param(

        [Parameter(Mandatory)]
        [string]$Title

    )

    Invoke-NovoraNirCmd @(
        "win"
        "normal"
        "title"
        $Title
    )

}

#------------------------------------------------------------
# Estado
#------------------------------------------------------------

function Get-NovoraNirCmdStatus {

    return [PSCustomObject]@{

        Installed = Test-NovoraNirCmd

        Path = $Global:NovoraNirCmd.Executable

    }

}