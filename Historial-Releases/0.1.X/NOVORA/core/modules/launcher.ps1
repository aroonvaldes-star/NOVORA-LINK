# ============================================================
# PROYECTO NOVORA
# Archivo : Launcher.ps1
# Autor   : ArDess61
# Función : Lanzador principal
# Build   : 0.3.0 Alpha
# ============================================================

#------------------------------------------------------------
# Inicializar Launcher
#------------------------------------------------------------

function Initialize-NovoraLauncher {

    Write-NovoraInfo "Inicializando Launcher..."

    if (!(Test-NovoraEngine)) {

        Write-NovoraError "El Engine no pudo inicializarse."

        return $false

    }

    Write-NovoraSuccess "Launcher listo."

    return $true

}

#------------------------------------------------------------
# Iniciar modo Gaming
#------------------------------------------------------------

function Start-NovoraGamingLauncher {

    if (!(Initialize-NovoraLauncher)) {

        return

    }

    Start-NovoraGaming

}

#------------------------------------------------------------
# Iniciar modo Streaming
#------------------------------------------------------------

function Start-NovoraStreamingLauncher {

    if (!(Initialize-NovoraLauncher)) {

        return

    }

    Start-NovoraStreaming

}

#------------------------------------------------------------
# Iniciar modo Creator
#------------------------------------------------------------

function Start-NovoraCreatorLauncher {

    if (!(Initialize-NovoraLauncher)) {

        return

    }

    Start-NovoraCreator

}

#------------------------------------------------------------
# Mostrar información del dispositivo
#------------------------------------------------------------

function Show-NovoraDeviceLauncher {

    Show-NovoraDevice

}

#------------------------------------------------------------
# Mostrar estado del sistema
#------------------------------------------------------------

function Show-NovoraStatusLauncher {

    $Status = Get-NovoraEngineStatus

    Write-Host ""
    Write-Host "=============== ESTADO NOVORA ===============" -ForegroundColor Cyan
    Write-Host ""

    Write-Host ("ADB............... {0}" -f $Status.ADB.Installed)
    Write-Host ("Scrcpy........... {0}" -f $Status.Scrcpy.Installed)
    Write-Host ("Gnirehtet........ {0}" -f $Status.Gnirehtet.Installed)
    Write-Host ("NirCmd........... {0}" -f $Status.NirCmd.Installed)

    if ($null -ne $Status.Device) {

        Write-Host ""
        Write-Host ("Dispositivo...... {0}" -f $Status.Device.Model)
        Write-Host ("Android.......... {0}" -f $Status.Device.Android)

    }

    Write-Host ""

}

#------------------------------------------------------------
# Reiniciar sesión
#------------------------------------------------------------

function Restart-NovoraLauncher {

    Restart-NovoraSession

}

#------------------------------------------------------------
# Cerrar NOVORA
#------------------------------------------------------------

function Exit-NovoraLauncher {

    Stop-NovoraSession

    Write-NovoraSuccess "NOVORA finalizado."

}