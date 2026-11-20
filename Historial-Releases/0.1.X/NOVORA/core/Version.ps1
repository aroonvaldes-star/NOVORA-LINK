# ============================================================
# PROYECTO NOVORA
# Archivo : Version.ps1
# Autor   : ArDess61
# Función : Información de versión
# ============================================================

$Global:NovoraVersion = [PSCustomObject]@{

    Name        = "NOVORA"

    Edition     = "ArDess61"

    Version     = "0.2.0 Alpha"

    Build       = "2026.08.02"

    PowerShell  = $PSVersionTable.PSVersion.ToString()

}

function Get-NovoraVersion {

    return $Global:NovoraVersion

}

function Show-NovoraVersion {

    $Version = Get-NovoraVersion

    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "                 PROYECTO NOVORA" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host (" Edición      : {0}" -f $Version.Edition)
    Write-Host (" Versión      : {0}" -f $Version.Version)
    Write-Host (" Build        : {0}" -f $Version.Build)
    Write-Host (" PowerShell   : {0}" -f $Version.PowerShell)
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host ""

}

function Get-NovoraBuild {

    return $Global:NovoraVersion.Build

}