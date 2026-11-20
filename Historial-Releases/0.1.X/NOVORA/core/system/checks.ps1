# ============================================================
# PROYECTO NOVORA
# Archivo : Checks.ps1
# Autor   : ArDess61
# Función : Verificación del entorno
# Build   : 0.2.0 Alpha
# ============================================================

function Test-NovoraEnvironment {

    Write-NovoraInfo "Verificando entorno NOVORA..."

    $Success = $true

    #----------------------------------------------------------
    # ADB
    #----------------------------------------------------------

    if (Test-NovoraADB) {

        Write-NovoraSuccess "ADB encontrado."

    }
    else {

        Write-NovoraError "ADB no encontrado."

        $Success = $false

    }

    #----------------------------------------------------------
    # scrcpy
    #----------------------------------------------------------

    if (Test-NovoraScrcpy) {

        Write-NovoraSuccess "scrcpy encontrado."

    }
    else {

        Write-NovoraError "scrcpy no encontrado."

        $Success = $false

    }

    #----------------------------------------------------------
    # Gnirehtet
    #----------------------------------------------------------

    if (Test-NovoraGnirehtet) {

        Write-NovoraSuccess "Gnirehtet encontrado."

    }
    else {

        Write-NovoraWarning "Gnirehtet no encontrado."

    }

    #----------------------------------------------------------
    # NirCmd
    #----------------------------------------------------------

    if (Test-NovoraNirCmd) {

        Write-NovoraSuccess "NirCmd encontrado."

    }
    else {

        Write-NovoraWarning "NirCmd no encontrado."

    }

    return $Success

}

function Start-NovoraChecks {

    return Test-NovoraEnvironment

}

function Show-NovoraSystemStatus {

    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "           ESTADO DEL SISTEMA"
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host ""

    Write-Host ("ADB............... {0}" -f (Test-NovoraADB))
    Write-Host ("scrcpy........... {0}" -f (Test-NovoraScrcpy))
    Write-Host ("Gnirehtet........ {0}" -f (Test-NovoraGnirehtet))
    Write-Host ("NirCmd........... {0}" -f (Test-NovoraNirCmd))

    Write-Host ""

}