# Validación de perfiles VE y NVIDIA

Estado posterior: [integración real de NVDEC mediante FFmpeg](NLNVIDIANvdec.md).
Los diagnósticos de ausencia de backend que aparecen debajo son históricos.

Fecha: 2026-09-14. Trabajo aplicado directamente, sin backups ni publicación.

## Correcciones verificadas

- BuildVisionOptionsVE respeta los FPS, bitrate y tamaño del perfil de salida;
  no aplica el límite silencioso 45 FPS/4M. Se conserva ApplyStreamStabilityVE
  para uso explícito y sus pruebas originales.
- Selector de los cuatro perfiles de video existentes: rellena las opciones
  guardadas mediante NLServiceVideoProfile. Se aplican al iniciar/reconectar;
  no promete modificar en caliente el codificador de una sesión existente.
- Selector NVIDIA conectado a NvidiaProfile; estado conectado al administrador,
  con suscripción y retirada de eventos. Configuración cargada aplicada al runtime.
- Política NVIDIA devuelve alternativa software para todos los perfiles porque
  no existe backend conectado. DLL/API disponible no equivale a aceleración activa.
- Indicadores distinguen CUDA inicializada, API detectada y backend activo.
- Perfiles NVIDIA indefinidos se rechazan; la UI normaliza configuración inválida.

## Prueba física de video

Teléfono: SM-A566E, serial R5CY3118MEW. Animación/aplicación con movimiento elegida
por el usuario. PC: RTX 3050 Laptop (controlador 32.0.16.1692) e Intel UHD
(31.0.101.4255). La presencia de dos adaptadores no prueba cuál presenta SDL.

Ocho muestras: dos rondas de cuatro perfiles, esperando el primer frame, tres
segundos de calentamiento y doce segundos de medición. Audio/gamepad desactivados
para aislar video. H264, FFmpeg software, renderer reportado direct3d11.

| Perfil | Configuración | Tamaño observado | FPS decodificados, rango | Mb/s recibidos aprox. |
|---|---|---|---|---|
| Juego | 4M, 1280, 45 FPS | 1280x590 | 44.24–44.67 | 4.06–4.26 |
| Equilibrado | 5M, 1280, 45 FPS | 1280x590 | 44.00–44.33 | 5.10–5.28 |
| Video | 6M, 1600, 45 FPS | 1600x738 | 44.03–44.70 | 6.10–6.14 |
| Ahorro | 3M, 960, 30 FPS | 960x442 | 29.96–29.99 | 2.91–2.95 |

Las ocho muestras presentaron imágenes: cero errores decode/render y cero descartes
registrados. El caudal es variable; el bitrate solicitado no es una igualdad exacta.
FPS de presentación aproximados por snapshots publicados por eventos; sus límites
temporales pueden diferir ligeramente de la ventana de medición.

Datos: [muestras aisladas](../../../Documentation/NLDocumentationVideoProfilesIsolated.json).
El contenido no es una reproducción determinista: no se demuestra superioridad
de calidad, ahorro energético ni latencia total. Se demuestra funcionamiento de
video y efecto real en resolución/caudal/FPS para estos perfiles y este entorno.

Prueba manual adicional: Gaming con 60 FPS solicitados, dos muestras de doce
segundos, produjo 30.83 y 42.08 FPS decodificados. Hubo presentación y ningún error
decode/render. No demuestra 60 FPS sostenidos ni que el rendimiento supere 45;
las pruebas de argumentos confirman que el límite silencioso fue eliminado.
[Datos de 60 FPS solicitados](../../../Documentation/NLDocumentationVideoManualSixty.json).

## Hallazgos NVIDIA

Los siete perfiles se evaluaron: Automatic, Disabled, Competitive, Balanced,
VisionPlus, Smooth y Stream. Disabled no consulta bibliotecas; los otros detectaron
CUDA y APIs NVDEC/NVENC, pero ninguno encontró el puente nativo de NOVORA.
Todos mantuvieron UseNvdec/UseNvenc/UseZeroCopy/UseRtxVideo/UseFruc desactivados.

| Componente | Veredicto |
|---|---|
| Detección CUDA/NVDEC/NVENC | Ejecutada en este PC; APIs detectadas |
| Selector, guardado y estado | Conexiones corregidas; pruebas de UI y persistencia |
| Backend nativo NVIDIA | Ausente; sin decodificación/codificación NVIDIA activa |
| RTX Video/FRUC/zero-copy NVIDIA | No implementados |
| NLNVIDIAMetrics | Contrato sin productor conectado; no telemetría GPU real |
| NVTX en RelayCore | Dependencia opcional declarada; sin llamadas propias encontradas |
| Skills/SDK citados en documentos | Referencias, no instalaciones o integraciones de ejecución realizadas aquí |

Las duraciones de detección en el JSON son tiempos de consulta de APIs, no medidas
de rendimiento de los perfiles NVIDIA.

## Fallo real descubierto y pendiente

La primera ejecución válida, con gamepad habilitado, terminó tras siete muestras
al iniciar la octava. Windows Application 1000/1026 registró SDL3.dll_unloaded,
acceso inválido c0000005 en VEGamepadSdl.InitializeVE y luego c000041d.
No se corrigió el ciclo de vida nativo del gamepad en esta tarea. La repetición
con gamepad desactivado completó las ocho muestras; no prueba resuelto ese fallo.

[Muestras anteriores al fallo](../../../Documentation/NLDocumentationVideoProfileMeasurements.json).
Antes de esa ejecución hubo un intento del harness sin InitializeAsync: no llegó
a transmitir; se corrigió y se invalidó como medición.

## Pruebas y segunda revisión

- Regresión reproducida antes de corregir: seis perfiles anunciaban funciones por
  capacidades ficticias y la UI reducía 120 FPS/25M a 45 FPS/4M.
- 79 pruebas Windows aprobadas. Incluyen parámetros de los cuatro perfiles hasta
  argumentos del servidor, conservación de opciones manuales, selección/estado de
  los siete perfiles NVIDIA y rechazo de valores inválidos.
- Aplicación y harness de perfiles compilados Release, cero errores/advertencias.
- Segunda revisión de conexiones, configuración persistida, alternativas, eventos
  y resultados. Sin nuevo polling; el harness usa esperas acotadas de prueba.
- Sin historial Git en esta carpeta. No se afirma prueba física de otras GPU,
  otros teléfonos, audio/gamepad durante las muestras aisladas o latencia extremo a extremo.

## Repetición

Proyecto: tests/NOVORA.VisionEngine.ProfileHarness/NLProjectVisionEngineProfileHarness.csproj.
Argumentos del ejecutable: serial, ruta JSON, perfil opcional y FPS opcionales.
La ventana debe permanecer abierta y el teléfono mostrar movimiento comparable.
El informe se guarda tras cada muestra. No ejecutar a la vez que otra sesión VE.
