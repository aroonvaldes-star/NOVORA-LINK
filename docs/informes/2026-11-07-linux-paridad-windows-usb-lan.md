# NOVORA-LINUX: interfaz Windows e integración USB/LAN

## Estado

Cambios locales implementados y comprobados sobre el árbol compartido. La
identidad visual del 100% no está certificada: falta ejecutar la referencia WPF
en condiciones equivalentes. La conectividad física con Android tampoco está
certificada; `adb devices -l` no mostró dispositivos.

## Cambios de esta tarea

- `LinuxVisualPalette.cs` y `WindowsVisualStyles.axaml`: valores de Platinum Air /
  Dark Carbon tomados del código Windows, degradados, transparencias, bordes,
  sombras, botones, estados de foco/hover/pulsación/disabled y switches.
- Shell: dimensiones Windows, marca original, títulos, navegación y recursos de
  tema. Se mantienen las fuentes Montserrat originales.
- `MainWindow.Home.cs`: tarjeta de conexión de dos columnas y tres tarjetas de
  motores, sin el panel ADB incrustado ni el resumen extra. VER PANTALLA solicita
  o detiene la captura real desde Inicio.
- `MainWindow.Vision.cs`: estado, perfil, salidas, calidad manual y opciones en
  la composición de Windows; se conservan grabación, entrada y FILES V2.
- `MainWindow.Link.cs`: estado, preparación del transporte, mediciones y selector
  de ruta. PREPARAR ESTA RUTA abre la configuración USB o LAN correspondiente.
- `MainWindow.System.cs` y la página Integración: distribución de las páginas
  Windows, manteniendo estados honestos para capacidades no implementadas.
- `MainWindow.Connection.cs`: menú USB/LAN, diálogo reutilizable, selección ADB,
  interfaz privada LAN, invitación, huella, expiración y revocación de confianza.
- Host: selección explícita y coherente de transporte; autorización USB y LAN
  separadas; prohibición de cambiar ruta con VE/LE ocupado; parada independiente
  de LAN que conserva USB. Detener una captura cancela la solicitud de permiso.
- Publicación: incorpora el relay en `Tools/LinkEngine/novora-linkengine-relay`
  y lo construye antes de preparar los archivos de salida.

## Trabajo concurrente

Durante esta tarea se incorporó ExInEngine por separado: `MainWindow.ExIn.cs`,
`ExInEngine/`, pruebas ExIn y cambios de integración en host/server/proyecto.
Se conservaron esos cambios y la tarjeta de Inicio consume `ExInStatusText`.
No se atribuye aquí la implementación de ExInEngine ni se certifica su
comportamiento físico. No se modificó el cliente Windows.

## Regresiones observadas y corregidas

Las nuevas comprobaciones fallaron antes de las correcciones correspondientes:

1. Inicio carecía de un menú USB/LAN y su botón solo navegaba a VisionEngine.
2. Solicitar captura sin conexión no devolvía error.
3. Un fallo real de limpieza del motor generaba `AggregateException` fuera del
   manejador de Inicio. La prueba inyecta una tarea fallida en el motor USB,
   conserva su parada real y comprueba que el diagnóstico quede en pantalla.
4. Con dos teléfonos, Inicio mostraba el modelo del selector ADB en vez del
   serial autorizado. La prueba conserva B autorizado y selecciona A.
5. Seleccionar USB sin autorización mientras LAN estaba autorizada producía
   rótulos contradictorios. La regresión verifica rótulo y resumen por ruta.

La revisión independiente identificó los casos 3–5; sus correcciones se
comprobaron con las regresiones antes de la verificación final.

## Evidencia de esta sesión

Entorno: Fedora 44 x64, .NET SDK 8.0.132, Avalonia 11.2.3, Cargo 1.99.1,
FFmpeg y SDL3 locales. Se restauró el proyecto de pruebas después de que la
integración concurrente añadiera Logging.Abstractions.

| Comprobación | Resultado |
|---|---|
| `dotnet build src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release --no-restore` | Exit 0; una advertencia CA1416 en `tests/ExInChecks.cs:194`, sin errores |
| `dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release --no-build` | Exit 0, incluida la nueva prueba TLS USB/LAN; repetida en el árbol final |
| `cargo test --manifest-path src/NOVORA-LINUX/LinkRelay/Cargo.toml --locked --offline` | 29 aprobadas |
| Modo `--windows-ui` | Exit 0: composición Inicio, errores de parada, modelos por serial, rótulos, diálogos LAN/USB/LAN reutilizados y render de todas las páginas |
| Modo `--ve-ui` | Exit 0; acciones VE presentes y render |
| Modos `--input-native`, `--outputs-native`, `--ve-runtime-native` | Exit 0 con SDL dummy/software; entrada, selección de salidas, sockets→FFmpeg→SDL, audio, grabación y cierre |
| `dotnet publish … -c Release --no-restore -o /tmp/novora-parity-publish-Hj6BzX` | Exit 0 |
| Relay desde el directorio publicado | Ejecutable y licencia presentes; proceso real emite `LE_READY`; autenticación DATA v2 y recepción de identidad aprobadas |
| `git diff --check` | Exit 0 |

El chequeo del relay inició únicamente el proceso creado por la prueba y lo
terminó después de autenticar un canal local. No se reenviaron paquetes hacia
destinos externos.

## Capturas

Se guardaron 30 PNG en `artifacts/reviews/linux-parity-2026-11-07/`:
Inicio, VisionEngine, LinkEngine, ExInEngine, Integración, Configuración y
Sistema Android, en Light/Dark y 1180×800 / 1000×610, más diálogos USB/LAN.
Son capturas del render de la aplicación Linux, no mockups generados.

## Pendientes de aceptación

- Comparación visual contra capturas de Windows ejecutado al mismo tamaño,
  tema, escala y estado. Local Commander no tiene equipos emparejados disponibles.
  Los iconos de navegación Linux son vectores, mientras Windows usa glyphs de
  Segoe Fluent Icons; esa diferencia necesita validación específica.
- Validación física USB/LAN con LINK Control actualizado: consentimiento,
  video/audio/entrada, archivos, LinkEngine, desconexión y revocación.
- GPU y escenarios físicos multimonitor/escalado: los probes hardware de esta
  máquina no pasaron; software sí. No se infiere aceleración desde SDL/OpenGL.
- Las capacidades Linux ausentes mantienen su estado real; sus textos dinámicos
  no serán idénticos a una sesión Windows que sí disponga de esas capacidades.

No hubo instalación de APK, cambio de firewall, despliegue, commit ni push.
