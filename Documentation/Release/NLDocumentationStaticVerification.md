# Verificación estática — NOVORA-LINK 1.4 PRERELEASE PRE FINAL

Fecha: 2026-09-17

## PASS ejecutados sobre la carpeta candidata

- `Tool/NLToolVerify.py`: PASS.
- `Documentation/NLDocumentationNamingMap.json`: JSON válido.
- JSON del repositorio: parseo correcto.
- XAML/XML/csproj/props/targets: parseo XML correcto.
- Handlers `OpenUserManualEs_Click` y `OpenUserManualEn_Click`: presentes y conectados desde XAML.
- Manual PC ES/EN: incluido en el proyecto para output/publish.
- Manual Android ES/EN: accesos presentes en `NLAndroidUIActivity`.
- Android package: `com.novora.appcontrol`.
- LinkEngine managed limit: 1 sesión.
- LinkEngine native TrafficEngine limit: 1 cliente.
- TunnelServer native limit: 1 cliente.
- `NLAssetBienvenido.png`: nombre normalizado; nombre anterior sólo permanece como clave histórica del NamingMap.
- Tests USB: nombres normalizados.
- No `bin/`, `obj/`, `target/`, `.vs` ni `RespaldoAndroidBloque*` dentro de la candidata.
- `scrcpy-server`, `LENetworkRelay.exe`, installer y Release Gate presentes.

## No ejecutado en este entorno

Este entorno no dispone de los SDK/toolchains Windows necesarios para afirmar:

- `dotnet build` WPF;
- `dotnet build -t:Compile` Android;
- `dotnet test`;
- `cargo check/test`;
- build Inno Setup;
- ejecución física USB/LAN;
- VisionEngine con teléfono;
- LinkEngine con túnel real.

Para esos pasos use `Tool/NLToolReleaseGate.ps1` en Windows y después las pruebas físicas del reglamento.

## Nota sobre Relay

El fuente ya limita a un dispositivo, pero `src/NOVORA/Tools/LinkEngine/LENetworkRelay.exe` debe recompilarse desde RelayCore antes de publicación para que el binario incorpore el cambio.
