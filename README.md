# NOVORA-LINK 1.3

NOVORA-LINK es una aplicación WPF para conectar, visualizar y controlar dispositivos Android desde Windows mediante ADB, scrcpy y Gnirehtet.

## Base estable de desarrollo

**NOVORA-LINK 1.3 es la base actual del repositorio.**

La solución principal está en:

- `NOVORA.sln`
- `src/NOVORA/NOVORA.csproj`

## Estructura

- `src/NOVORA/` — aplicación WPF y servicios.
- `src/NOVORA/Tools/` — herramientas de ejecución restauradas por `scripts/Setup-Tools.ps1`.
- `Installer/` — instalador Inno Setup.
- `scripts/` — preparación reproducible de dependencias.
- `docs/` — arquitectura y estructura técnica.
- `.github/workflows/` — compilación y publicación de releases.

## Dependencias de ejecución

Los binarios de terceros no se versionan dentro del repositorio. `scripts/Setup-Tools.ps1` descarga versiones fijadas y verifica SHA-256 antes de colocarlas en `src/NOVORA/Tools/`.

Versiones fijadas para 1.3:

- scrcpy 4.1 (Windows x64)
- Gnirehtet 2.5.1 (Rust, Windows x64)

## Compilar

```powershell
pwsh -File .\scripts\Setup-Tools.ps1
dotnet restore .\NOVORA.sln
dotnet build .\NOVORA.sln -c Release
```

## Releases

Las releases oficiales usan tags de dos componentes, por ejemplo `v1.3` o `v1.4`. GitHub Actions publica un instalador `NOVORA-Setup-<version>.exe` y su SHA-256.

## Créditos y licencias

NOVORA utiliza proyectos de terceros como scrcpy, Gnirehtet y Android Debug Bridge. Consulta `LICENSE`, `COPYRIGHT.md`, `ACKNOWLEDGEMENTS.md` y `THIRD-PARTY-NOTICES.md`.

---

**NOVORA © 2026 Aaron Yair Galarza Valdes — All Rights Reserved.**
