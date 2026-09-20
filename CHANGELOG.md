# Changelog

## NOVORA-LINK 1.4 PRERELEASE PRE FINAL — 2026-09-17

### Estructura
- La raíz activa fue limpiada de respaldos `RespaldoAndroidBloque*`, laboratorios y entregas históricas que no deben formar parte de la rama principal.
- Se conserva `NLDocumentationNamingMap.json` como autoridad de nombres.
- `NOVORA.sln` referencia escritorio, Android y pruebas; Android conserva build explícito para no empaquetar accidentalmente.

### Arquitectura
- El límite de LinkEngine se alinea con la regla actual: **1 dispositivo Android activo por PC**.
- Los motores conservan Recovery separado.
- Se mantiene Zero Polling como regla; los loops de eventos/deadlines válidos no se eliminan a ciegas.

### UI
- El panel dinámico de PC conserva secciones Inicio/Pantalla/Red/Rendimiento/Game Input/Integración/Privacidad/Configuración.
- Configuración incorpora acceso al manual ES/EN.
- Android incorpora manual en español e inglés.

### Documentación / repositorio
- README principal.
- CHANGELOG.
- SECURITY.
- CONTRIBUTING.
- Manual ES/EN.
- Reglamento de proyecto.
- Informe funcional prerelease.
- Avisos y reconocimientos de terceros en raíz.

### Installer
- `NLInstallerSetup.iss` actualizado a `1.4 PRERELEASE PRE FINAL`.
- Acceso de escritorio de NOVORA.
- Copia de Manual ES y EN al Escritorio.

### Correcciones
- Se eliminó una duplicación sintáctica detectada en `RefreshDevices_Click` si estaba presente en el baseline.
- Se restaura `scrcpy-server` en el paquete candidato desde un snapshot previo aceptado cuando el contexto compacto lo omitió por ser un archivo sin extensión. La procedencia y hash quedan documentados.

### Pendiente antes de Release estable
- Ejecutar `Tool/NLToolReleaseGate.ps1` en Windows con los SDK requeridos.
- Prueba física USB y LAN.
- Pruebas de LinkEngine, VisionEngine, STEngine y Recovery independiente.
- Investigación final de rendimiento, seguridad y calidad.
