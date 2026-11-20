# ============ Core/Detect.ps1 ============
# Detección de dispositivos y herramientas

function Test-ADB {
    <#
    .SYNOPSIS
        Verifica que ADB esté instalado
    #>
    try {
        Get-Command adb -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Get-AndroidDevices {
    <#
    .SYNOPSIS
        Lista dispositivos Android conectados
    #>
    if (-not (Test-ADB)) {
        Write-Log "ADB no encontrado" "ERROR"
        return @()
    }
    
    try {
        $output = adb devices | Select-String "device$"
        $devices = @()
        foreach ($line in $output) {
            $parts = $line -split '\t'
            if ($parts.Count -ge 2 -and $parts[1] -eq "device") {
                $devices += $parts[0]
            }
        }
        return $devices
    } catch {
        Write-Log "Error al listar dispositivos: $_" "ERROR"
        return @()
    }
}

function Test-Scrcpy {
    <#
    .SYNOPSIS
        Verifica que scrcpy esté instalado
    #>
    try {
        Get-Command scrcpy -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}