# ============ Core/Config.ps1 ============

# Definir configuración global
$script:Config = @{
    Version = "0.3.0"
    ADBPath = "adb"
    ScrcpyPath = "scrcpy"
    AutoConnect = $true
    LogLevel = "INFO"
}

function Get-Config {
    return $script:Config
}

function Set-Config {
    param($key, $value)
    if ($script:Config.ContainsKey($key)) {
        $script:Config[$key] = $value
        return $true
    }
    return $false
}