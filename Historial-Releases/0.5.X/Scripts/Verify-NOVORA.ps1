$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\NOVORA.Launcher\NOVORA.Launcher.csproj'
$runtime = Join-Path $root 'Tools\Runtime'
Write-Host "NOVORA project: $project"
Write-Host "Runtime: $runtime"
if (!(Test-Path $project)) { throw 'Falta el proyecto WPF.' }
foreach ($name in 'adb.exe','scrcpy.exe','scrcpy-server','gnirehtet.exe','gnirehtet.apk') { if (!(Test-Path (Join-Path $runtime $name))) { throw "Falta $name" } }
Write-Host 'OK: estructura y herramientas requeridas presentes.'
