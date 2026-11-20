# ============================================================
# PROYECTO NOVORA
# Archivo : Menu.ps1
# Autor   : ArDess61
# Función : Menú principal
# Build   : 0.3.0 Alpha
# ============================================================

function Show-NovoraMenu {

    do {

        Show-NovoraHeader

        Write-Host "============== MENU PRINCIPAL ==============" -ForegroundColor Cyan
        Write-Host ""
        Write-Host " [1] Modo Gaming"
        Write-Host " [2] Modo Streaming"
        Write-Host " [3] Modo Creator"
        Write-Host ""
        Write-Host " [4] Información del dispositivo"
        Write-Host " [5] Estado del sistema"
        Write-Host " [6] Reiniciar sesión"
        Write-Host ""
        Write-Host " [0] Salir"
        Write-Host ""

        $Option = Read-Host "Seleccione una opción"

        switch ($Option) {

            "1" {

                Start-NovoraGamingLauncher

                Wait-Novora

            }

            "2" {

                Start-NovoraStreamingLauncher

                Wait-Novora

            }

            "3" {

                Start-NovoraCreatorLauncher

                Wait-Novora

            }

            "4" {

                Show-NovoraDeviceLauncher

                Wait-Novora

            }

            "5" {

                Show-NovoraStatusLauncher

                Wait-Novora

            }

            "6" {

                Restart-NovoraLauncher

                Wait-Novora

            }

            "0" {

                Exit-NovoraLauncher

                break

            }

            default {

                Write-Host ""
                Write-Host "Opción no válida." -ForegroundColor Yellow

                Wait-Novora

            }

        }

    } while ($true)

}