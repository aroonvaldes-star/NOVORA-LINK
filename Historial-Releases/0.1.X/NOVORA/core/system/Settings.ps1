# ============================================================
# PROYECTO NOVORA
# Archivo : Settings.ps1
# ============================================================

$Global:NovoraSettings = @{

    Theme = "Dark"

    Language = "es-MX"

    Debug = $false

    AutoCheck = $true

}

function Get-NovoraSettings {

    return $Global:NovoraSettings

}

function Set-NovoraSetting {

    param(

        [string]$Key,

        $Value

    )

    $Global:NovoraSettings[$Key] = $Value

}