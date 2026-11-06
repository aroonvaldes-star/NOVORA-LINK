# VisionEngine Linux — implementación y verificación local

Implementación realizada sobre Dev_Local conservando el trabajo local anterior.
La pestaña VE conecta perfiles y ajustes persistentes, aceleración comprobable,
monitor/audio, teclado/ratón/touchpad, grabación y transferencia FILES V2 a un
runtime común de USB y LAN.

## Resultado por función

| Área | Evidencia nueva | Límite |
|---|---|---|
| Perfiles | IDs traducidos, presets, validación atómica, persistencia, concurrencia y revisiones PASS | Cambios aplicados a la siguiente sesión |
| Aceleración | Probes reales; software PASS; forzado indisponible falla explícitamente | Ningún backend hardware funcional en este host |
| Touchpad/teclado | Bytes JSON, Unicode, HiDPI/rotación, arrastre, scroll XY fraccionario y foco PASS; eventos SDL reales sin nuevos frames PASS | No se probó la interacción física laptop→Android |
| Salidas | Enumeración repetida y apertura de monitor/audio concretos PASS; recuperación por nombre tras ID cambiado | Selección física multimonitor y hotplug no certificados |
| Grabación | FFprobe/FFmpeg: H264+PCM, timestamps, rotación/resize, segmentos, sin audio, fallos/partial y límite de cola PASS | Grabación con teléfono pendiente |
| Archivos | V2, SHA, leases/generación, rutas, symlink y no sobrescritura PASS; presupuesto compatible con Android | Canal Android USB añadido en fuente; APK no reconstruida |
| Runtime | Sockets de video/audio → FFmpeg → SDL, petición keyframe, MKV y cierre normal por evento SDL (sin cancelar externamente) PASS | Simulación local; no reemplaza USB/LAN físicos |
| Interfaz | Construcción nativa, acciones y render PASS | Captura local en `/tmp/novora-ve-ui.png` |

## Comandos y resultado final

- `dotnet build src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release --no-restore`: exit 0, 0 advertencias/errores.
- `dotnet run --project src/NOVORA-LINUX/tests/NOVORA.Linux.ProtocolTests.csproj -c Release --no-build`: exit 0, suite completa aprobada.
- Modos separados `--input-native`, `--outputs-native`, `--ve-runtime-native` y `--ve-ui`: exit 0. Los tres primeros se ejecutaron también con drivers SDL dummy y renderer software.
- Android `dotnet build 'src/LINK Control/NLProjectAndroid.csproj' -c Release --no-restore`: bloqueado NETSDK1139; entorno con .NET8 sin SDK/plataforma Android requerida por net10.0-android. Las reglas puras de edición Android sí se ejecutaron dentro de las pruebas Linux.
- `adb devices -l`: sin dispositivos. No se generó, instaló ni publicó APK.

Los avisos Mesa/Zink del smoke real no impidieron las pruebas. No se afirma
aceleración GPU por el nombre del renderer. Un timeout previo del smoke FFmpeg
fue investigado en la salida de frames pequeños por pipe; se añadió flush de
paquetes de salida y las comprobaciones final y de runtime completaron.

## Revisión independiente

Se corrigieron los hallazgos sobre limpieza tras fallos de grabación, callbacks
de progreso que accedían a un archivo dispuesto, cancelación por fallo de audio,
contrato batch Android, negociación de presupuesto FILES, caducidad de IDs SDL,
configuración concurrente y limpieza si falla la creación de renderer. La revisión
de las correcciones no dejó hallazgos materiales abiertos en ese alcance.

Para completar la aceptación física aún se requiere un APK de esta fuente y un
teléfono autorizado: probar USB/LAN, entrada real por touchpad, archivos en ambos
sentidos, grabación/audio, rotación, reconexión y revocación. No se declara cierre
integral de hardware ni una release a partir de estas pruebas locales.

La regresión adicional de cierre por ventana se observó fallar con TaskCanceledException y pasó tras tratar ese cierre como parada normal, manteniendo los fallos de workers como errores y ejecutando limpieza independiente. La suite completa y build Linux se repitieron tras esa corrección: aprobados, build sin advertencias ni errores.
