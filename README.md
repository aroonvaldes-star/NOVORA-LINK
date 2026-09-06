# NOVORA-LINK 1.3


## Base estable de desarrollo

**NOVORA-LINK 1.3 es la base actual del repositorio.**

La soluciÃ³n principal estÃ¡ en:

- `NOVORA.sln`
- `src/NOVORA/NOVORA.csproj`

## Estructura

- `src/NOVORA/` â€” aplicaciÃ³n WPF y servicios.
- `src/NOVORA/Tools/` â€” herramientas de ejecuciÃ³n restauradas por `scripts/Setup-Tools.ps1`.
- `Installer/` â€” instalador Inno Setup.
- `scripts/` â€” preparaciÃ³n reproducible de dependencias.
- `docs/` â€” arquitectura y estructura tÃ©cnica.
- `.github/workflows/` â€” compilaciÃ³n y publicaciÃ³n de releases.

## Dependencias de ejecuciÃ³n

Los binarios de terceros no se versionan dentro del repositorio. `scripts/Setup-Tools.ps1` descarga versiones fijadas y verifica SHA-256 antes de colocarlas en `src/NOVORA/Tools/`.

Versiones fijadas para 1.3:

- scrcpy 4.1 (Windows x64)

## Compilar

```powershell
pwsh -File .\scripts\Setup-Tools.ps1
dotnet restore .\NOVORA.sln
dotnet build .\NOVORA.sln -c Release
```

## Releases

Las releases oficiales usan tags de dos componentes, por ejemplo `v1.3` o `v1.4`. GitHub Actions publica un instalador `NOVORA-Setup-<version>.exe` y su SHA-256.

## CrÃ©ditos y licencias


---

**NOVORA Â© 2026 Aaron Yair Galarza Valdes â€” All Rights Reserved.**

## VisionEngine Block C

Block C añade `Recovery`, `Metrics`, `Performance` y `Stress` al pipeline headless. El renderer continúa deshabilitado; la primera imagen queda reservada para Block D.

Prueba recomendada con Galaxy A56 5G:

```powershell
.\scripts\Test-VisionEngine-BlockC.ps1 -DeviceSerial SERIAL -Seconds 60
```
