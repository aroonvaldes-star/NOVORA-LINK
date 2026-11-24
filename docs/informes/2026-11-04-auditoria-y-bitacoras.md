# Auditoría local y contraste de bitácoras — 2026-11-04

## Alcance y evidencia

Revisión selectiva de NOVORA-LINK/Dev_Local, HEAD 607843f, de sus workflows, arranque del relay Windows, persistencia de tema y control Linux. Contraste con BITACORA_UNIVERSAL.md de la raíz, su copia en NOVORA WORKSPACE/Workspace, los resúmenes DOCUMENTACION-IA y el informe previo del 4 de noviembre. Se preservaron los cambios locales existentes; no se modificó código de producto. No se verificaron servicios remotos ni dispositivos físicos.

## Hallazgos priorizados

1. **Alta: CI y release usan rutas inexistentes.** `.github/workflows/ci.yml:40` invoca el relay bajo `src/NOVORA`; sus builds usan `src/NOVORA` y `src/NOVORA.Android`, mientras la solución apunta a `src/NOVORA LINK` y `src/LINK Control`. `release.yml:79` también publica desde la ruta antigua. Esos comandos no pueden operar sobre este árbol. Corregir también CodeQL, análisis de dependencias, SecurityCodeScan y scripts asociados. El filtro push de CI no incluye `swiss-retheme`.

2. **Alta: el arranque del relay no confirma la identidad del listener.** `src/NOVORA LINK/LinkEngine/Network/LENetworkRelay.cs:282` retorna al detectar un listener; `IsPortListeningLE()` solo busca loopback y puerto. La comprobación separada de que el proceso hijo sigue vivo no vincula el socket con ese proceso. Un listener ajeno preexistente puede producir un éxito prematuro antes de que el hijo falle al enlazar. Pendiente ya registrado el 27 de octubre y aún presente. Requiere confirmación del relay recién iniciado y regresión con puerto ocupado por otro proceso. No se afirma explotación ni evasión del token de las conexiones DATA.

3. **Media: revocación Linux diferida y sin cierre de los motores.** `src/NOVORA-LINUX/Control/LinuxControlHost.cs:89` solo elimina la confianza del almacén. El servidor verifica la revocación después de leer la siguiente petición (`LinuxControlServer.cs:307-313`). Un cliente inactivo conserva el socket y el estado autorizado hasta entonces. La salida del handler tampoco detiene LinkEngine/VisionEngine. Revisar el contrato de revocación para cerrar inmediatamente la sesión afectada y sus recursos; verificar con captura activa. Hallazgo estático, sin reproducción física.

4. **Media: Windows no restaura el tema guardado.** `Application/NLApplicationApp.xaml.cs:229` carga settings pero aplica Light; `UI/NLUIWindowMain.xaml.cs:708` también asigna Light. La selección sí se guarda en settings. Restaurar `settings.Theme` con validación y probar reinicio en ambos temas.

5. **Media: Linux queda fuera del checkout reproducible y de la CI.** `git status` muestra `?? src/NOVORA-LINUX/`; `git ls-files src/NOVORA-LINUX` no devuelve archivos. La solución y el descubrimiento de suites en `scripts/Test-NOVORA-CI.ps1` no incluyen ese proyecto ni sus pruebas anidadas en src. Existe implementación Avalonia, control TLS y motores USB, pero un clon de HEAD no los contiene. Incorporar su seguimiento y verificación cuando se autorice confirmar cambios.

## Contraste de bitácoras

- **APK verificada localmente:** proyecto Android y descriptor declaran 124 / 1.4.124. SHA-256 calculado de `src/NOVORA LINK/Android/LinkControl.apk`: `81FACAD6D39D7C8BA6FFBF64C1B4F87A8757172367ECA3AD71091367EDC78210`, igual al descriptor y a las entradas del 31 de octubre. La instalación y publicación son evidencia histórica registrada; no se repitieron ni verificaron remotamente en esta sesión.
- **VE LAN:** la bitácora del 1 de noviembre corrige el checkpoint y registra prueba física del 31 de octubre. No debe describirse como nunca probado. Eso no acredita Linux ni una validación integral del estado actual.
- **Corrección de reloj presente:** `NLControlVeLanProtocol.cs` conserva `ClockSkewSeconds = 60`, coherente con el diagnóstico de aproximadamente 34 s de desfase y la versión 1.4.123.
- **Locks de latencia presentes:** NLAndroidVpnService adquiere y libera NLAndroidLowLatencyLock. Sigue sin evidencia en estas bitácoras de una medición válida posterior con el túnel activo. La serie de 60 pings sin LE activo fue explícitamente invalidada y no demuestra rendimiento de 1.4.124.
- **Cambios de interfaz presentes:** BitmapCache existe en el estilo de tarjetas; no aparecen BlurEffect en el XAML principal inspeccionado, coherente con la variante final sin brillos desenfocados.
- **Frontend:** la entrada del 27 de octubre que retiró su configuración fue corregida por otra entrada posterior que la restauró. El React actual y package.json son coherentes con esa corrección; no corresponde clasificarlos como huérfanos solo por la entrada anterior.
- **Resúmenes antiguos:** DOCUMENTACION-IA/Bitacora.md y bitacora_de_conversaciones.md tienen corte del 27 de octubre y aún reflejan 1.4.120 y pendientes de ese corte. Se deben leer como históricos.
- **Copias divergentes:** la bitácora raíz tiene 78 líneas frente a 75 de NOVORA WORKSPACE/Workspace/BITACORA_UNIVERSAL.md. La copia carece de las tres entradas finales del 1 de noviembre sobre Frame, Settings y Breeze. La nota Obsidian “Bitácora universal” también conserva conteos anteriores.
- **Informe previo del 4 de noviembre:** su afirmación de ausencia de implementación Linux ya no describe el disco actual. Sus 29 pruebas Rust aprobadas son un resultado de otra ejecución, no el resultado reproducido aquí.

## Verificación de esta sesión

| Comando o comprobación | Resultado |
|---|---|
| cargo test --release --locked --offline (relay Windows, ejecutado en Linux) | 24 aprobadas; 5 fallan con PermissionDenied / Operation not permitted al crear listeners. Restricción del entorno; no se concluye regresión del relay. |
| python3 scripts/validate_engine_naming.py | Falla por rutas antiguas de Windows y Android. |
| npm run lint | Bloqueado: falta @typescript/typescript-linux-x64. |
| npm run build | Bloqueado: binding nativo de Rolldown ausente. |
| dotnet run de NOVORA.FileProtocol.Tests | Restauración bloqueada por acceso denegado a NuGet. |
| Mismo proyecto con --no-restore | Assets previos apuntan al fallback de NuGet de Visual Studio en Windows, inexistente aquí. |
| SHA-256 APK | Coincide con descriptor y bitácora. |

No se ejecutó build Windows/Android ni la suite Linux completa. El entorno tiene SDK .NET 8 sin workloads Android; Android requiere .NET 10. No se reinstalaron dependencias. No se certifica la entrega integral a partir de esta revisión.

## Orden de corrección

Corregir rutas de automatización e identidad del relay; definir cierre inmediato por revocación Linux; restaurar tema; incorporar Linux a seguimiento y CI; preparar dependencias propias de Linux y ejecutar suites. Después validar físicamente reconexión LE LAN, latencia con túnel activo y congelamiento VE USB, pendientes explícitos de las bitácoras.

## Seguimiento Linux — 2026-11-04

- **Revocación inmediata:** resuelta en `src/NOVORA-LINUX`. El servidor asocia la sesión autenticada con el ID de confianza, cancela sus operaciones pendientes, cierra el socket y detiene VisionEngine y LinkEngine al revocar el teléfono. La revocación de otro dispositivo no desconecta la sesión activa. Se añadió una prueba de regresión para cierre inmediato y rechazo de reconexión.
- **Seguimiento y verificación:** `src/NOVORA-LINUX` y `.github/workflows/linux.yml` quedaron añadidos al índice de Git, sin commit ni push. La nueva workflow Linux compila la aplicación y ejecuta las pruebas de protocolo y smoke nativo en Ubuntu; SDL3 se construye desde el commit fijado `535d80badefc83c5c527ec5748f2a20d6a9310fe`.
- **Validación local:** build Release Linux limpio; suite de protocolo/confianza, revocación y smoke FFmpeg/SDL aprobada. ADB no detectó teléfono; la sesión física sigue pendiente. La ejecución remota de la workflow queda pendiente de que los cambios se confirmen y publiquen.
