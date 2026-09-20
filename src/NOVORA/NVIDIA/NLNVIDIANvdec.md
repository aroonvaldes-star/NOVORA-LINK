# NVIDIA / VE: NVDEC real mediante FFmpeg

Fecha: 2026-09-14. Proyecto: C:\Users\Aroon\Desktop\NL2\NOVORA-LINK.
Cambios directos autorizados, sin backups ni publicación en GitHub.

## Resultado y recorrido

Ya existe decodificación H264 por NVDEC dentro de VE. No requiere el archivo
NOVORA.NVIDIA.Native.dll: el puente funcional lo proporciona FFmpeg compartido.
El conjunto mínimo anterior carecía de decoders CUVID y configuraciones hardware.
Se reemplazaron cuatro bibliotecas comunes por el build BtbN compartido LGPLv3,
versión `n8.1.2-52-g5a03dfa0f6-20260913` (familia ABI avcodec62/avutil60).
[Procedencia, configuración y hashes](../Tools/FFmpeg-build.json),
[licencia](../Tools/FFmpeg-LICENSE.txt), [avisos](../../../Legal/NLLegalThirdPartyNotices.md).
Las cuatro DLL ocupan aproximadamente 111 MiB; el conjunto anterior rondaba 10 MiB.

Android codifica H264 -> ADB/scrcpy transporta -> VE abre h264_cuvid -> NVDEC decodifica
-> FFmpeg entrega planos NV12 en CPU -> SDL/direct3d11 presenta. Hay transferencia
GPU-CPU y carga de textura: NO es zero-copy. No se modificó el codificador Android
ni se conectó NVENC, RTX Video o FRUC. AMF/QSV aparecen en el build FFmpeg, pero VE
no los selecciona ni se declara aceleración AMD/Intel implementada.

- Automatic y los otros cinco perfiles NVIDIA habilitados solicitan el mismo decoder.
  No hay diferencias de calidad/interpolación entre ellos todavía; la UI lo explica.
- Disabled abre h264 software directamente, sin la detección NVIDIA del administrador.
- Los perfiles de video VE sí aplican bitrate, tamaño y FPS distintos al iniciar.
- El estado activo requiere frames producidos; cargar DLLs o abrir un contexto no basta.
  Un cambio de perfil durante una sesión se aplica al siguiente inicio y no falsea
  el backend que sigue ejecutándose.
- H265/AV1/VP8/VP9 tienen selección por nombre y alternativa software en código;
  la validación física aquí es H264. No se afirma compatibilidad universal.

## Recuperación y latencia

El primer ensayo detectó `AVHWFramesContext is already initialized with incompatible
parameters` durante un cambio de dimensiones. Quedó registrado con siete muestras
correctas y una sin frames en
[el ensayo inicial](../../../Documentation/NLDocumentationNvdecProfiles.json).
No se presenta como una ronda aprobada.

La nueva configuración del stream reinicializa el contexto CUVID cuando cambia,
conservando vivos los clones en CPU que aún posee el renderer. La prueba nativa
verifica imágenes 320x240 y 240x320 en un mismo decoder. Si falta el decoder o falla
su apertura, se abre software. Si falla NVDEC durante decode, se cambia una sola
vez a software, se conserva/reenvía la configuración y se espera un keyframe; si es
necesario y el control está listo, se solicita ResetVideo una vez. El límite de 5 s
se comprueba al llegar paquetes, sin añadir polling. La recuperación ante pérdida
física de GPU/controlador no se ha provocado en este PC.

Se configura `flags=+low_delay` mediante la API pública av_opt_set: CUVID usa una
espera de presentación de cero en lugar de su valor predeterminado. Esto no demuestra
latencia total menor. La referencia es
[el código CUVID de FFmpeg](https://github.com/FFmpeg/FFmpeg/blob/n8.1/libavcodec/cuviddec.c).
Se mantiene el aviso nativo `Invalid pkt_timebase, passing timestamps as-is`: VE no
propaga PTS al AVPacket; las métricas actuales son tiempos locales de llegada de
frames, no sincronización audiovisual ni latencia de extremo a extremo.

Se corrigió además el reenvío del mismo AVPacket después de EAGAIN y se liberan todos
los clones que no llegan a transferirse si falla un lote. No se cambió el descarte
de frames ni se añadió un bucle de sondeo.

## Medición física

SM-A566E / R5CY3118MEW, equipo con GPU NVIDIA. Dos rondas de cada perfil por backend,
primer frame como condición de inicio, 3 s de calentamiento y 12 s por muestra.
Contenido con movimiento controlado por el usuario, sin reproducción determinista.
Audio y gamepad desactivados para aislar video. Renderer reportado: direct3d11;
esto no identifica por sí solo el adaptador usado para presentación.

16/16 muestras aprobadas, todas con imágenes presentadas y cero errores de decode/render.
Ocho usaron h264_cuvid/NVDEC y ocho h264/software. Los JSON incluyen hashes del
ensamblado medido; el ajuste final de mensajes/limpieza de buffers se valida aparte.

| Perfil VE | Solicitado: Mb/s / tamaño / FPS | Tamaño observado | FPS NVDEC | FPS software | CPU proceso % NVDEC | CPU proceso % software |
|---|---|---|---|---|---|---|
| Gaming | 4M / 1280 / 45 | 1280x590 | 30.00–30.00 | 29.92–30.08 | 0.58–0.62 | 1.13–1.67 |
| Balanced | 5M / 1280 / 45 | 1280x590 | 30.00–30.08 | 30.00–30.00 | 0.49–0.73 | 1.32–1.71 |
| Video | 6M / 1600 / 45 | 1600x738 | 30.00–30.00 | 30.00–30.07 | 0.67–0.88 | 1.66–1.83 |
| Battery | 3M / 960 / 30 | 960x442 | 29.67–30.00 | 29.89–29.91 | 0.44–0.89 | 1.20–1.57 |

Datos: [NVDEC](../../../Documentation/NLDocumentationNvdecProfilesVerified.json) y
[software](../../../Documentation/NLDocumentationSoftwareProfilesVerified.json).
En estas muestras la carga CPU del proceso fue menor con NVDEC, pero no constituyen
una comparación controlada ni una medición de consumo energético. El contenido
entregó unos 30 FPS incluso solicitando 45. No se demuestra aumento de FPS, 60 FPS
sostenidos, ni una mejora visual. Las muestras anteriores a la integración alcanzaron
aproximadamente 44 FPS con otro tramo de contenido: no se mezclan como benchmark A/B.

## Verificación y límites

- 85 pruebas Windows aprobadas: selección y opciones de UI, política, frames H264
  nativos, apertura sin anunciar actividad, recuperación a software, clones,
  reconfiguración de dimensiones y audio Opus -> PCM estéreo 48 kHz.
- scrcpy --version carga el conjunto compartido y reporta las versiones esperadas.
- Build de escritorio y harnesses; verificación de nombres; segunda revisión de
  carga/descarga de DLL, propiedad de frames, EAGAIN, recuperación y publicación de estado.
- El test de audio es de decodificación; no demuestra reproducción audible en una
  sesión completa. No se probaron PCs AMD/Intel sin NVIDIA en esta etapa.
- Persiste el incidente previo de SDL3.dll_unloaded con gamepad en reinicios repetidos;
  [detalle histórico](NLNVIDIAValidacion.md). Esta integración no lo corrige.
- NLNVIDIAMetrics continúa sin productor de telemetría GPU. La actividad proviene del
  decoder y los contadores de VE; no se inventan medidas NVTX/FRUC/NVENC.
- [Skills NVIDIA evaluadas](NLNVIDIASkills.md) siguen siendo referencias de desarrollo;
  no fueron instaladas ni convertidas en requisitos de NOVORA.

Secciones relacionadas: [arquitectura NVIDIA](NLNVIDIAArquitectura.md),
[rendimiento VE](../../../Documentation/NLDocumentationVisionEnginePerformance.md),
[validación general](../../../Documentation/NLDocumentationValidation.md).

## Comprobación del ensamblado final

[Dos sesiones finales de Gaming](../../../Documentation/NLDocumentationNvdecFinalSmoke.json):
Completed=true y salida del proceso 0; 45.00 y 45.08 FPS decodificados con NVDEC,
1280x590, frames presentados, cero errores decode/render. El ensamblado del medidor
coincide byte a byte con el NOVORA.dll de salida de escritorio. El contenido volvió
a producir alrededor de 45 FPS; estas dos muestras se separan de la matriz anterior
que produjo alrededor de 30 FPS. No se atribuye esa variación a un cambio de límite.

Hubo un [intento final interrumpido](../../../Documentation/NLDocumentationNvdecFinalSmokeInterrupted.json):
el proceso terminó con código 1 después de imprimir una muestra, antes de guardarla;
el JSON quedó sin muestras. No se encontró un evento Windows Application 1000/1026
asociado en la ventana consultada y no se determinó su causa. No cuenta como prueba
aprobada ni se atribuye al fallo SDL anterior. El medidor ahora guarda inmediatamente
cada muestra, registra Completed al finalizar y emite si la ventana cerró antes de
terminar. La repetición anterior cerró con beforeRunCompleted=False.

Verificación final de nombres: PASS, 349 archivos propios. Enlaces locales y hashes
FFmpeg de Tools y salida de escritorio: coinciden. Las pruebas físicas aprobadas de
esta etapa son 16 en la matriz y 2 en la comprobación final; los intentos fallidos o
interrumpidos se mantienen separados.
