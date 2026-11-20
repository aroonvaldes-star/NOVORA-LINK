# ============================================================
# PROYECTO NOVORA
# Archivo : NirCmd.ps1
# Autor   : ArDess61
# Función : Gestión de NirCmd
# ============================================================

#------------------------------------------------------------
# Ruta de NirCmd
#------------------------------------------------------------

$Global:NirCmd = @{
    Path = Join-Path $Global:NovoraRoot "tools\nircmd\nircmd.exe"
}

#------------------------------------------------------------
# Verificar instalación
#------------------------------------------------------------

function Test-NovoraNirCmd {

    return (Test-Path $Global:NirCmd.Path)

}

#------------------------------------------------------------
# Ejecutar comando NirCmd
#------------------------------------------------------------

function Invoke-NovoraNirCmd {

    param(

        [Parameter(Mandatory)]
        [string[]]$Arguments

    )

    if (!(Test-NovoraNirCmd)) {

        Write-NovoraError "NirCmd no encontrado."

        return

    }

    & $Global:NirCmd.Path @Arguments

}

#------------------------------------------------------------
# Subir volumen
#------------------------------------------------------------

function Set-NovoraVolumeUp {

    param(

        [int]$Step = 5000

    )

    Invoke-NovoraNirCmd @(
        "changesysvolume"
        $Step
    )

}

#------------------------------------------------------------
# Bajar volumen
#------------------------------------------------------------

function Set-NovoraVolumeDown {

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
# Estado
#------------------------------------------------------------

function Get-NovoraNirCmdStatus {

    return [PSCustomObject]@{

        Installed = Test-NovoraNirCmd

    }

}