# VisionEngine: selección de skills y propuesta de rendimiento

Estado posterior: [integración real de NVDEC mediante FFmpeg](NLNVIDIANvdec.md).
Los diagnósticos de ausencia de backend que aparecen debajo son históricos.

Actualización posterior: [validación de perfiles](NLNVIDIAValidacion.md).
Se retiró el límite silencioso de 45 FPS/4M del constructor de opciones de la UI;
ApplyStreamStabilityVE permanece como política explícita, no aplicada a toda selección.
Los hallazgos y propuestas siguientes documentan el estado anterior a esa corrección.

Fecha: 2026-09-14. Investigación y revisión de fuentes locales; no se modificó
código de ejecución, no se instalaron skills ni se midió rendimiento real.

## Evidencia del código actual

- VisionEngine/Server/VEServerOptions.cs: valores predeterminados H264, 4 Mb/s,
  tamaño máximo 1280 y 45 FPS. ApplyStreamStabilityVE limita a 45 FPS y 4 Mb/s.
- UI/NLUIWindowMainVisionEngine.cs llama ApplyStreamStabilityVE al construir opciones.
- VisionEngine/Video/VEVideoDecoder.cs abre el decoder FFmpeg por nombre; no configura
  un dispositivo de decodificación por hardware en el flujo inspeccionado.
- VisionEngine/Video/VEVideoManager.cs recibe paquetes, decodifica y entrega frames
  al renderer. Existen eventos y contadores para aprovechar en mediciones.
- VisionEngine/Renderer contiene cola, métricas y presentación SDL. La capacidad
  del decodificador aislado no demuestra FPS presentados ni latencia completa.

## Selección

Catálogo consultado: https://raw.githubusercontent.com/NVIDIA/skills/main/skills.sh.json

1. nvidia-skill-finder: candidato de desarrollo para descubrir recursos vigentes.
   Instalación pendiente; no forma parte de la aplicación distribuida.
2. jetson-video-benchmark: mide capacidad de codificación/decodificación en Jetson.
   Referencia para calentamiento, repeticiones y separación de capacidad/latencia;
   no ejecutar como si fuera un benchmark Windows de VE.
3. jetson-video-recipe: expresa configuraciones de codec de forma reproducible.
   Referencia de diseño; no es configuración directamente compatible con VE.
4. deepstream-profile-pipeline: diagnóstico con Nsight Systems para DeepStream.
   Condicional a un laboratorio DeepStream; no justifica migrar VE a DeepStream.

En la selección revisada no se encontró una skill que conecte directamente
NVDEC con nuestro decoder FFmpeg/renderer SDL en Windows.

Fuentes oficiales:
- https://github.com/NVIDIA/skills/blob/main/skills/jetson-video-benchmark/SKILL.md
- https://github.com/NVIDIA/skills/blob/main/skills/jetson-video-recipe/SKILL.md
- https://github.com/NVIDIA/skills/blob/main/skills/deepstream-profile-pipeline/SKILL.md
- https://developer.nvidia.com/nsight-systems
- https://developer.nvidia.com/video-codec-sdk
- https://www.ffmpeg.org/ffmpeg.html
- https://github.com/Genymobile/scrcpy/blob/master/doc/video.md

## Integración propuesta, por orden

1. Medición propia de VE bajo demanda: configuración efectiva, backend real,
   frames recibidos/decodificados/presentados, descartes, profundidad de cola,
   tiempo de decode y regularidad de presentación. Aprovechar eventos existentes.
   Usar reloj monotónico local; no restar relojes Android/PC sin sincronización.
2. Comparación reproducible de 30/45/60 FPS, codecs y resolución bajo condiciones
   equivalentes. Para 60 FPS es necesario revisar el límite actual; no eliminarlo
   sin comprobar estabilidad, recursos, temperatura y pérdidas.
3. Evaluar decodificación por hardware con FFmpeg y una ruta Windows compartida
   (por ejemplo D3D11VA según soporte real), junto a una alternativa por software.
   Evaluar ruta NVIDIA opcional con Video Codec SDK/NVDEC donde aporte beneficio.
4. Revisar transferencia y presentación de frames: los frames GPU requieren
   interoperabilidad o transferencia compatible con SDL; no basta cambiar un nombre
   de decoder. Comparar el recorrido completo, incluidas copias y sincronización.
5. Codificación de pantalla Android: ajustar opciones compatibles del servidor/
   MediaCodec, verificadas en el teléfono. NVENC del PC no acelera ese codificador.
   Reservar NVENC para una función futura que codifique video en el PC.

No agregar polling permanente. El módulo NVIDIA conserva código exclusivo del
fabricante; métricas y contratos compartidos pertenecen a VE. Nsight Systems es
una herramienta externa opcional, no una skill ni requisito de ejecución.

## Validación necesaria

Revisión doble, compilación Windows y pruebas de estado/configuración antes de
sesiones reales. Medir misma escena, teléfono, conexión, resolución y duración.
Separar FPS solicitados de reales y latencia de capacidad máxima de procesamiento.
No prometer más FPS o menos retraso hasta obtener evidencia comparable.

## Estado y conexiones

Propuesta documentada, implementación e instalación pendientes de autorización.
Referencia desde Documentation/NLDocumentationVisionEnginePerformance.md y desde
NLNVIDIASkills.md. No se requieren DOCA, Omniverse ni aumento de datos para este plan.
