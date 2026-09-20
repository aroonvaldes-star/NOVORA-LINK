[CmdletBinding()]
param([string]$RepoPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$ErrorActionPreference = 'Stop'
$source = Join-Path $RepoPath 'src\NOVORA\Control\NLControlUsbAutoPolicy.cs'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Falta NLControlUsbAutoPolicy.cs.' }
# Compile the actual production class, not a PowerShell reimplementation.
if ($null -eq ('NOVORA.Control.NLControlUsbAutoPolicy' -as [type])) {
    Add-Type -Path $source
}
function Assert-UsbPolicy([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    Write-Host "PASS: $Name" -ForegroundColor Green
}
$p = New-Object NOVORA.Control.NLControlUsbAutoPolicy
Assert-UsbPolicy (-not $p.TryBegin($false)) 'Sin telefono no se prepara una sesion'
[void]$p.Observe('PHONE-TEST', $true, $true, $true)
Assert-UsbPolicy (-not $p.TryBegin($false)) 'Wi-Fi no se trata como USB'
[void]$p.Observe('PHONE-TEST', $true, $false, $false)
Assert-UsbPolicy (-not $p.TryBegin($false)) 'ADB no autorizado no habilita conexion'
[void]$p.Observe('PHONE-TEST', $true, $false, $true)
Assert-UsbPolicy (-not $p.TryBegin($true)) 'LAN o instalacion en curso bloquea sin consumir intento'
Assert-UsbPolicy ($p.TryBegin($false)) 'Conexion autorizada dispara un intento'
$epoch = $p.Epoch
[void]$p.Observe('PHONE-TEST', $true, $false, $true)
Assert-UsbPolicy (-not $p.TryBegin($false)) 'Eventos repetidos no crean sockets duplicados'
Assert-UsbPolicy ($p.IsCurrent('PHONE-TEST', $epoch)) 'Evento repetido conserva generacion'
[void]$p.Observe('PHONE-TEST', $false, $false, $false)
Assert-UsbPolicy (-not $p.IsCurrent('PHONE-TEST', $epoch)) 'Desconectar invalida awaits anteriores'
[void]$p.Observe('PHONE-TEST', $true, $false, $true)
Assert-UsbPolicy ($p.TryBegin($false)) 'Reconectar crea un intento nuevo'
$p.Pause()
Assert-UsbPolicy (-not $p.TryBegin($false)) 'Detener explicitamente no reconecta solo'
$p.Retry()
Assert-UsbPolicy ($p.TryBegin($false)) 'Reintentar explicito permite una nueva preparacion'
$epoch = $p.Epoch
[void]$p.Observe('OTHER-TEST', $true, $false, $true)
Assert-UsbPolicy (-not $p.IsCurrent('PHONE-TEST', $epoch)) 'Cambiar telefono invalida sesion anterior'
Assert-UsbPolicy ($p.TryBegin($false)) 'Nuevo telefono autorizado tiene su propio intento'
$online = [NOVORA.Control.NLControlUsbAutoPolicy]::ParseOnline("A`tdevice`nB`toffline`nC`tunauthorized`n")
Assert-UsbPolicy ($online.Contains('A') -and -not $online.Contains('B') -and -not $online.Contains('C')) 'Track-devices acepta solo estado device'
Write-Host 'POLICY TESTS: 14 PASS' -ForegroundColor Green