function Test-Requirements {

    Write-Host ""
    Write-Host "Comprobando herramientas..." -ForegroundColor Cyan

    # ADB
    if (Get-Command adb -ErrorAction SilentlyContinue) {
        Write-Host "[OK] ADB encontrado" -ForegroundColor Green
    }
    else {
        Write-Host "[X] ADB NO encontrado" -ForegroundColor Red
    }

    # scrcpy
    if (Get-Command scrcpy -ErrorAction SilentlyContinue) {
        Write-Host "[OK] scrcpy encontrado" -ForegroundColor Green
    }
    else {
        Write-Host "[X] scrcpy NO encontrado" -ForegroundColor Red
    }

}