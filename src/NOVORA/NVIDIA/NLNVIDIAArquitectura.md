# NVIDIA: organización y conexiones

Estado actual: [NVDEC mediante FFmpeg y validación](NLNVIDIANvdec.md).
La política de detección no activa funciones por sí sola. VE informa frames reales
al administrador NVIDIA; sólo esa ruta puede marcar NVDEC activo. NVENC, RTX Video,
FRUC y zero-copy permanecen sin implementación.

Esta carpeta es la ubicación canónica del código propio específico de NVIDIA.
Namespace: `NOVORA.NVIDIA`. Prefijo de archivos y tipos: `NLNVIDIA`.

## Contenido

- `NLNVIDIAManager`: detección bajo demanda y publicación de estado.
- `NLNVIDIACapabilities`: capacidades detectadas; no demuestra decodificación activa.
- `NLNVIDIAPolicy`, `NLNVIDIAPipeline`, `NLNVIDIAProfile`: selección de política opcional.
- `NLNVIDIAStatus`: resultado de evaluación.
- `NLNVIDIAMetrics`: contrato de métricas, sin productor conectado identificado.
- `NLNVIDIADecoder`: nombres de decoders CUVID de FFmpeg para video.
- `NLNVIDIAPaths`: ruta histórica reservada, no requerida por el decoder actual.
- [Contexto previo y referencias](NLNVIDIAContexto.md).
- [Evaluación de skills](NLNVIDIASkills.md): Skill Finder, DOCA Bench, Omniverse/USD y aumento de datos de video para IA; utilidad y requisitos de laboratorio.

## Conexiones compartidas

VisionEngine/Core/VECoreRuntime.cs conserva la creación, evaluación y eventos del
administrador. UI consume los tipos de NOVORA.NVIDIA. Los controles XAML y las
propiedades de configuración continúan en UI, ViewModel y Service porque forman
parte de la interfaz y persistencia compartidas. La clave NvidiaProfile y los
valores del enum conservan su contrato. No se agregaron timers ni polling.

Las pruebas están en tests/NOVORA.Tests/Test/NLTestNvidiaTests.cs para ejecutarse
en el proyecto de pruebas y no incluir xUnit en la aplicación.

## Decodificación opcional

No se requiere `NOVORA.NVIDIA.Native.dll`. VE usa las funciones públicas de FFmpeg
con h264_cuvid/hevc_cuvid/av1_cuvid/vp8_cuvid/vp9_cuvid cuando se solicita NVIDIA.
Sólo H264 está comprobado con el teléfono en esta etapa. La salida de CUVID se
transfiere a CPU y SDL recibe NV12; no existe zero-copy ni encoder NVENC conectado.
La selección Disabled abre directamente el decoder software.

Las bibliotecas FFmpeg comunes están en Tools porque también sirven a audio,
scrcpy y equipos sin NVIDIA. Su procedencia y hashes están en Tools/FFmpeg-build.json.
Los controladores NVIDIA del sistema no se copian ni redistribuyen. El contenido
NVIDIA/Native y NLNVIDIAPaths quedan como contrato histórico opcional; no gobiernan
la activación ni producen mensajes de puente ausente en el recorrido actual.

## NVTX y RelayCore

LinkEngine/Network/Native/RelayCore/Cargo.toml conserva la dependencia nvtx 2.0.0
opcional y la feature nvtx. Cargo.lock conserva su resolución. Son metadatos del
proyecto Rust y no deben separarse de su manifiesto. No se identificaron llamadas
NVTX propias en el código Rust; no se afirma instrumentación activa.

## Alcance y evolución

Plan de trabajo: [skills y rendimiento de VisionEngine](NLNVIDIAVisionEngine.md).

NVIDIA debe permanecer opcional. No se instalaron SDK ni skills en esta migración.
Jetson Video Pipeline se conserva como referencia de validación; ejecutarlo requiere
su entorno Jetson. DeepStream y la prueba de video Windows siguen como propuestas.
No hay evidencia nueva de aceleración, latencia o compatibilidad mult fabricante.
Los resultados de reorganización se registran en Documentation/NLDocumentationValidation.md.
