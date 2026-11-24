# Windows: compatibilidad con las mejoras de conexión de Linux

2026-11-08. Se trasladan comportamientos a WPF/.NET/DPAPI de Windows. No se enlaza código de `NOVORA-LINUX` ni se modifican sus fuentes. Los cambios Android/Linux concurrentes de Claude quedan fuera de esta entrega.

## Correspondencia revisada

| Mejora de Linux | Estado Windows |
|---|---|
| Interfaz de motores, grabación, archivos, audio, ExIn y métricas LE | Ya existen implementaciones Windows; se conservan, sin copiar SDL/Linux ni Avalonia |
| Escucha LAN automática y seguimiento de interfaces | Adaptada con NetworkChange, Dispatcher y selección de interfaces Windows; conserva dirección válida y reintenta fallos con backoff 1/2/4/8/15 s |
| Descubrimiento continuo después del enlace | Adaptado; reintenta UDP independientemente sin reemplazar un TCP sano |
| Identidad común USB/LAN | Nuevos bootstraps USB utilizan el almacén principal DPAPI y su certificado; permite usar la credencial como respaldo LAN de Android compatible |
| Conservar USB al reconfigurar LAN | Parada LAN separada; no invalida la generación global USB |
| Selección de teléfono | Sin preferencia y con varios teléfonos USB no escoge el primero; un teléfono o una preferencia válida sí permiten inicio automático |
| Recuperación USB | Tres bootstraps por conexión, 20 s de autorización, 2 s tras cierre; el tercer timeout también cierra el listener. Una parada explícita/revocación pausa el recovery |
| Seguridad de órdenes encoladas | La identidad del servidor se comprueba dentro del dispatcher; una orden vieja LAN no puede afectar a la sesión nueva |

El seguimiento ADB Windows existente se reutiliza. Los almacenes USB históricos no se borran ni se reescriben; un nuevo bootstrap protegido presenta la identidad principal y Android puede requerir recordar esa identidad. Una credencial de la identidad USB histórica no se convierte silenciosamente en confianza LAN.

No se anuncia soporte WAN ni `mobileDataV1` completo: el plan móvil y las piezas Android de Claude aún no demuestran transporte remoto, límites de consumo o perfiles aplicados extremo a extremo. La recuperación automática de motores entre rutas y UHID remoto tampoco se certifican; el propio informe Linux deja fases pendientes.

## Archivos de este cambio

- `Control/NLControlDynamicConnectionPolicy.cs`: backoff, elección LAN y recuperación USB sin I/O.
- `Control/NLControlUsbAutoPolicy.cs`: selección no ambigua.
- `UI/NLUIWindowMainAutomaticUsb.cs`, `NLUIWindowMainAndroidTrust.cs`, `NLUIWindowMainAndroidLan.cs`: adaptación Windows de conexión y confianza.
- `UI/NLUIWindowMainAndroidControl.cs` y nuevo `NLUIWindowMainAndroidDispatch.cs`: pausa explícita y validación de servidor en el despachador.
- `tests/NOVORA.WindowsConnection.Tests`: pruebas portables y frontera de dispatcher encolado que enlaza el método de producción.
- `.github/workflows/windows-main-preview.yml`: build Windows x64 autocontenido y artifact descargable al subir main; no compila ni publica una APK.

## Verificación y límites

- Suite nueva: **25 comprobaciones PASS**. Antes del arreglo falló la selección automática con dos teléfonos. Al quitar temporalmente la comprobación de servidor, falló la prueba de orden LAN obsoleta; restaurada la producción, vuelve a pasar.
- Sintaxis Roslyn de siete archivos de producción: PASS; no sustituye compilación WPF.
- `git diff --check` de archivos propios: PASS.
- Intento general de las 25 suites existentes/nueva con SDK del sistema: 7 suites pasan; 18 quedan bloqueadas o fallan por entorno. Detalle guardado durante la sesión en `/tmp/novora-windows-port-checks.json`. Pasan AndroidVideoEncoder (31), ExInRuntime (23), ExInSession (7), NetworkSession, RelayPolicy (9), VisionEngine.Startup y WindowsConnection. CapturePolicy: 23/24, falla `OptionalFilesFailurePreservesVideoAndControl` por sockets denegados. ExInOutput: 2/3, falta `powershell.exe`. NetworkControlProbe/RelayStartup: sockets denegados. Otras suites: SDK WindowsDesktop/.NET 10 ausentes o assets de NuGet heredados con rutas Windows inexistentes. No se declara suite general verde.
- SDK Windows aislado: `.NET 8.0.427` en Descargas, con paquetes del cache del proyecto. Build normal del árbol compartido bloqueado por `Asset/NLAssetExInXboxCalibration.png`, eliminado antes de esta tarea; la eliminación se conserva.
- Build WPF diagnóstico con recurso original de HEAD suministrado desde `/tmp` mediante targets temporales: **0 advertencias, 0 errores**. Ese resultado también incluye fuentes compartidas concurrentes; no es un release del árbol íntegro ni prueba física.
- Antes de publicar se valida aparte el subconjunto exacto de Windows sobre HEAD, con el recurso que ya existe en Git. Consultar la entrada de entrega para su resultado y SHA remoto.
- Faltan ejecución WPF en Windows, comportamiento real de DPAPI y pruebas físicas USB/LAN con teléfono. No se instaló software ni se cambió APK, firma, descriptor o versión Android.

## Descarga de prueba

En GitHub Actions, abrir **Windows main preview**, elegir la ejecución del commit de main y descargar el artifact `NOVORA-LINK-Windows-x64-<sha>`. Extraer todo y ejecutar `NOVORA-LINK.exe`. Requiere que la ejecución termine correctamente; un push por sí solo no demuestra que el ZIP esté disponible.
