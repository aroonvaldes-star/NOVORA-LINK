$ErrorActionPreference = "Stop"

$Repo = "C:\Users\Aroon\Desktop\NOVORA-LINK"
$Patch = Split-Path -Parent $MyInvocation.MyCommand.Path

$Files = @(
    @{ Source = Join-Path $Patch "ProvisioningDeviceLE.cs"; Destination = Join-Path $Repo "src\NOVORA\LinkEngine\Device\ProvisioningDeviceLE.cs" },
    @{ Source = Join-Path $Patch "MainWindow.LinkEngineLE.cs"; Destination = Join-Path $Repo "src\NOVORA\MainWindow.LinkEngineLE.cs" }
)

Write-Host "=== NOVORA LE A15 WIFI RESTORE FIX ===" -ForegroundColor Cyan

foreach ($File in $Files) {
    if (-not (Test-Path -LiteralPath $File.Source)) { throw "Falta patch: $($File.Source)" }
    if (-not (Test-Path -LiteralPath $File.Destination)) { throw "No existe destino: $($File.Destination)" }

    $Backup = "$($File.Destination).bak-LE-WIFI-RESTORE"
    Copy-Item -LiteralPath $File.Destination -Destination $Backup -Force
    Copy-Item -LiteralPath $File.Source -Destination $File.Destination -Force
    Write-Host "[OK] $($File.Destination)" -ForegroundColor Green
}

Push-Location $Repo
try {
    Write-Host "`n=== BUILD ANDROID RELEASE ===" -ForegroundColor Cyan
    dotnet build ".\NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Falló build Android." }

    Write-Host "`n=== BUILD NOVORA RELEASE ===" -ForegroundColor Cyan
    dotnet build ".\src\NOVORA\NOVORA.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Falló build NOVORA." }

    Write-Host "`nFIX COMPILADO CORRECTAMENTE." -ForegroundColor Green
}
finally {
    Pop-Location
}
