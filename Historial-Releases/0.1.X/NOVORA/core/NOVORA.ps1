# ============ NOVORA.ps1 ============
# Punto de entrada principal - Screen Mirror + VPN Utility
# Versión: 0.3.0

$scriptRoot = $PSScriptRoot
if (-not $scriptRoot) { $scriptRoot = Get-Location }

# ============ CARGAR MÓDULOS ============
# Config y Functions están en core/system/
. "$scriptRoot\core\system\Config.ps1"
. "$scriptRoot\core\system\Functions.ps1"

# UI está en modules/
. "$scriptRoot\modules\UI.ps1"

# Detect y Engine están en core/
. "$scriptRoot\core\Detect.ps1"
. "$scriptRoot\core\Engine.ps1"

# ============ PROCESAR COMANDOS ============
function Invoke-Command {

    param(
        [string]$command
    )

    if ($command.StartsWith("/")) {

        $parts = $command.Substring(1).Trim() -split "\s+", 2
        $action = $parts[0].ToLower()

        $argument = if ($parts.Count -gt 1) {
            $parts[1]
        }
        else {
            ""
        }

        switch ($action) {

            "help" {
                Show-Help
            }

            "clear" {
                Clear-Chat
            }

            "devices" {
                Get-ConnectedDevices
            }

            "connect" {
                Connect-Device $argument
            }

            default {
                Update-Chat `
                    -Remitente "NOVORA" `
                    -Mensaje "Comando desconocido: /$action" `
                    -Tipo "error"
            }

        } # switch

    } # if StartsWith

} # function Invoke-Command


if ($MyInvocation.InvocationName -ne ".") {
    Start-NOVORA
}

        Punto de entrada principal de NOVORA
    #>
    
    Clear-Host
    Write-Host @"
╔═══════════════════════════════════════╗
║                                       ║
║           N O V O R A  V0.3.0         ║
║                                       ║
║   Screen Mirror + VPN Utility         ║
║   PowerShell Core + Python Tools      ║
║                                       ║
║   © 2026 - Proyecto NOVORA           ║
╚═══════════════════════════════════════╝
"@ -ForegroundColor Cyan
    
    # ===== VERIFICAR DEPENDENCIAS =====
    
    # 1. Verificar Python
    try {
        $pythonVersion = python --version 2>&1
        Update-Chat -Remitente "Sistema" -Mensaje "✅ Python: $pythonVersion" -Tipo "success"
    } catch {
        Update-Chat -sender "Sistema" -message "❌ Python no instalado o no está en el PATH" -type "error"
        Update-Chat -sender "Sistema" -message "💡 Descarga Python desde https://python.org" -type "info"
        return
    }
    
    # 2. Verificar ADB
    if (-not (Test-ADB)) {
        Update-Chat -sender "Sistema" -message "❌ ADB no instalado o no está en el PATH" -type "error"
        Update-Chat -sender "Sistema" -message "💡 Descarga Platform Tools desde https://developer.android.com/studio/releases/platform-tools" -type "info"
        return
    } else {
        Update-Chat -sender "Sistema" -message "✅ ADB disponible" -type "success"
    }
    
    # 3. Verificar scrcpy
    if (-not (Test-Scrcpy)) {
        Update-Chat -sender "Sistema" -message "⚠️ scrcpy no instalado (opcional para mirroring)" -type "warning"
        Update-Chat -sender "Sistema" -message "💡 Descarga scrcpy desde https://github.com/Genymobile/scrcpy" -type "info"
    } else {
        Update-Chat -sender "Sistema" -message "✅ scrcpy disponible" -type "success"
    }
    
    # 4. Buscar dispositivos
    $devices = Get-AndroidDevices
    if ($devices) {
        Update-Chat -sender "Sistema" -message "✅ Dispositivos encontrados: $($devices -join ', ')" -type "success"
    } else {
        Update-Chat -sender "Sistema" -message "⚠️ Conecta tu celular por USB y habilita depuración USB" -type "warning"
        Update-Chat -sender "Sistema" -message "💡 Ve a Ajustes > Opciones de desarrollador > Depuración USB" -type "info"
    }
    
    Update-Chat -sender "Sistema" -message "💡 Escribe /help para ver comandos" -type "info"
    Update-Chat -sender "Sistema" -message "🚀 NOVORA listo para usar" -type "success"
    
    # ===== CARGAR INTERFAZ =====
    
    # Intentar cargar UI (WPF/XAML)
    if (Initialize-UI) {
        Update-Chat -sender "Sistema" -message "🖥️ Interfaz gráfica cargada" -type "success"
        Show-UI
    } else {
        # Fallback: Modo consola
        Update-Chat -sender "Sistema" -message "⚠️ No se pudo cargar la interfaz gráfica" -type "warning"
        Update-Chat -sender "Sistema" -message "💡 Usando modo consola..." -type "info"
        
        do {
            $userInput = Read-Host "`nNOVORA> "
            Process-Command -command $userInput
        } while ($true)
    }


# ============ EJECUTAR ============
# Si el script se ejecuta directamente (no importado)
if (Invoke-Command -command $userInput) {
    Start-NOVORA
}