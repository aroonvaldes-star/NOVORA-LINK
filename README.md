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
- `docs/` — arquitectura, estructura técnica y revisión legal.
- `.github/workflows/` — compilación y publicación de releases.

## Dependencias de ejecución

Los binarios de terceros no se versionan dentro del repositorio. `scripts/Setup-Tools.ps1` descarga versiones fijadas y verifica SHA-256 antes de colocarlas en `src/NOVORA/Tools/`.

Versiones fijadas para 1.3:

- scrcpy 4.1 (Windows x64)
- Gnirehtet 2.5.1 (Rust, Windows x64)

El proceso también prepara `Tools/Legal/` para que las releases incluyan avisos de autoría, licencia, seguridad, privacidad y terceros junto al software distribuido.

## Compilar

```powershell
pwsh -File .\scripts\Setup-Tools.ps1
dotnet restore .\NOVORA.sln
dotnet build .\NOVORA.sln -c Release
```

## Releases

Las releases oficiales usan tags de dos componentes, por ejemplo `v1.3` o `v1.4`. GitHub Actions publica un instalador `NOVORA-Setup-<version>.exe` y su SHA-256.

## Seguridad

Consulta [`SECURITY.md`](SECURITY.md) para el proceso de reporte responsable de vulnerabilidades, alcance y principios de seguridad del proyecto.

No publiques exploits, credenciales, tokens ni detalles sensibles de una vulnerabilidad en issues públicos.

## Privacidad

Consulta [`PRIVACY.md`](PRIVACY.md). NOVORA-LINK 1.3 opera principalmente de forma local, almacena preferencias en `%LocalAppData%\NOVORA\settings.json` y usa GitHub por HTTPS para el mecanismo de actualizaciones. La revisión actual no identificó telemetría o analítica publicitaria propia.

## Créditos, copyright y licencias

Las partes originales de NOVORA se distribuyen bajo la licencia propietaria del repositorio. Los componentes de terceros conservan sus propias licencias y autoría.

Documentos relevantes:

- [`LICENSE`](LICENSE)
- [`COPYRIGHT.md`](COPYRIGHT.md)
- [`ACKNOWLEDGEMENTS.md`](ACKNOWLEDGEMENTS.md)
- [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)
- [`docs/COPYRIGHT-COMPLIANCE.md`](docs/COPYRIGHT-COMPLIANCE.md)

La revisión de copyright confirma Apache-2.0 para scrcpy 4.1 y Gnirehtet 2.5.1, y Apache-2.0 en los componentes ADB aplicables. También identifica dependencias transitivas de la build Windows de scrcpy (FFmpeg, SDL3, dav1d y libusb). La redistribución LGPL de FFmpeg/libusb requiere mantener una estrategia de licencias y código fuente correspondiente antes de declarar cumplimiento legal total.

---

**NOVORA © 2026 Aaron Yair Galarza Valdes — All Rights Reserved.**
