$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$launcher = Join-Path $root 'Start.ps1'

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "FAILED: $Message" }
    Write-Host "PASS: $Message"
}

$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile($launcher, [ref] $tokens, [ref] $errors) | Out-Null
Assert-Condition ($errors.Count -eq 0) 'El lanzador tiene sintaxis PowerShell valida'

$source = Get-Content -LiteralPath $launcher -Raw
Assert-Condition ($source -match "low\s+=\s+@\('--max-size=1024', '--max-fps=30', '--video-bit-rate=4M', '--no-audio'\)") 'El perfil low limita resolucion, FPS, bitrate y audio'
Assert-Condition ($source -match 'Gnirehtet is reverse tethering') 'La UX no presenta gnirehtet como VPN de anonimato'
Assert-Condition ($source -match "'start'\s+\{ Start-NovoraSession") 'Existe un inicio integrado de tethering y mirroring'
Assert-Condition ($source -match 'Test-NovoraGnirehtetClient') 'El tethering confirma el cliente VPN en Android'
Assert-Condition ($source -match 'Stop-Process -Id \$state\.MirrorPid') 'La detencion solo finaliza el proceso de la sesion registrada'
Assert-Condition ($source -match '\$mirror\.Path -eq \$script:ScrcpyPath') 'La detencion valida que el PID sea scrcpy empaquetado'
Assert-Condition ($source -notmatch 'Stop-Process -Force') 'No hay cierre forzoso global de procesos'
Assert-Condition (Test-Path -LiteralPath (Join-Path $root 'integrity.json') -PathType Leaf) 'Existe manifiesto de integridad de binarios'

foreach ($tool in @('tools\adb\adb.exe', 'tools\scrcpy\scrcpy.exe', 'tools\gnirehtet\gnirehtet.exe')) {
    Assert-Condition (Test-Path -LiteralPath (Join-Path $root $tool) -PathType Leaf) "Esta incluido $tool"
}
