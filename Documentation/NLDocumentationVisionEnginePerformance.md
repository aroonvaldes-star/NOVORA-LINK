# VisionEngine: investigación de video y rendimiento

Modelos evaluados: [ASR Streaming, VoiceChat y Relighting](../src/NOVORA/NVIDIA/NLNVIDIAModelosAudioVideo.md); propuestas de audio y efectos, sin integración ejecutada.

Actualización NVIDIA/VE: [NVDEC opcional, recuperación y mediciones](../src/NOVORA/NVIDIA/NLNVIDIANvdec.md).

Estado posterior: [ajustes y mediciones de perfiles](../src/NOVORA/NVIDIA/NLNVIDIAValidacion.md).
El límite silencioso se retiró del flujo de la interfaz; se comprobaron parámetros
de salida y transmisión real. El texto siguiente conserva el diagnóstico previo.

La [evaluación y propuesta de integración](../src/NOVORA/NVIDIA/NLNVIDIAVisionEngine.md)
centraliza las fuentes NVIDIA y los hallazgos locales de 2026-09-14.

VE conserva las métricas, coordinación y alternativas compartidas. NVIDIA aloja
las funciones específicas del fabricante. La investigación detectó el límite
de 45 FPS/4 Mb/s aplicado por la interfaz y el decoder FFmpeg sin configuración
de hardware en el flujo inspeccionado. Son hallazgos de código, no mediciones.

Primer paso propuesto: medición por eventos del recorrido recepción-decode-render.
Después: perfiles comparables, backend hardware opcional y revisión de copias.
No se modificaron límites, colas, codificadores ni dependencias con esta investigación.
