# Evaluación de skills NVIDIA

Modelos evaluados: [ASR Streaming, VoiceChat y Relighting](NLNVIDIAModelosAudioVideo.md); propuestas de audio y efectos, sin integración ejecutada.

Estado posterior: [integración real de NVDEC mediante FFmpeg](NLNVIDIANvdec.md).
Los diagnósticos de ausencia de backend que aparecen debajo son históricos.

Selección específica y hallazgos actuales: [video, codificación y FPS de VisionEngine](NLNVIDIAVisionEngine.md).

Consulta: 2026-09-14. Alcance: lectura de los SKILL.md oficiales, sin instalación
ni ejecución de sus flujos. No se auditó el entorno local DOCA ni todo el catálogo
de skills instaladas. No se modifica el funcionamiento de NOVORA-LINK.

## nvidia-skill-finder

Fuente: https://github.com/NVIDIA/skills/blob/main/skills/nvidia-skill-finder/SKILL.md

Busca skills en el catálogo vigente según el producto y la tarea. Puede orientar
al asistente hacia recursos de NVIDIA y distinguir recomendaciones de instalaciones.
No activa aceleración ni aporta por sí mismo un componente de ejecución a NOVORA.

Valoración: candidato útil para el entorno de desarrollo. Mantener aquí su referencia;
copiar documentación a esta carpeta no instala una skill en Codex.

## doca-bench

Fuente: https://github.com/NVIDIA/skills/blob/main/skills/doca-bench/SKILL.md

Guía el uso de doca_bench para medir operaciones de bibliotecas DOCA. Declara como
requisitos Linux, DOCA SDK 2.7.0 o posterior y hardware BlueField o ConnectX.
No mide la latencia completa de la aplicación. Una GPU GeForce por sí sola no
satisface el requisito de hardware indicado.

Valoración: referencia para un laboratorio DOCA futuro, sin integración propuesta
en el escritorio Windows actual. Puede inspirar mediciones reproducibles de LinkEngine:
registrar comando, versión, dispositivo y entorno, y comparar ejecuciones estables.
Eso no equivale a ejecutar DOCA ni demostrar rendimiento PC-Android.

## omniverse-usd-performance-tuning

Fuente: https://github.com/NVIDIA/skills/blob/main/skills/omniverse-usd-performance-tuning/SKILL.md

Diagnostica y optimiza escenas USD: carga, memoria, estructura e interacción.
Sus etapas pueden necesitar Kit, Usd Optimize, validadores NVIDIA y USD Python.
Los FPS tratados corresponden a escenas y renderizado, no a decodificación de
la pantalla Android. Compara perfiles y validaciones antes y después.

Valoración: no se propone integrarla en VisionEngine para mejorar streaming.
Sería pertinente si NOVORA incorpora trabajo con escenas USD/Omniverse.
Su método de medir antes de optimizar sirve como referencia de ingeniería.

## physical-ai-video-data-augmentation

Fuente: https://github.com/NVIDIA/skills/blob/main/skills/physical-ai-video-data-augmentation/SKILL.md

Coordina aumento de datos de video y etiquetado automático sobre OSMO, incluyendo
flujos con superresolución. Requiere acceso OSMO configurado, almacenamiento,
un pool GPU disponible y acceso a modelos mediante Hugging Face según su ficha.
Es procesamiento de conjuntos de datos, no un decodificador de baja latencia.

Valoración: posible herramienta de laboratorio si se desarrolla entrenamiento
o evaluación de IA visual con datos aumentados. No se propone incorporarla al
recorrido de video en vivo. Un conjunto sintético no certifica rendimiento ni
fidelidad del flujo real PC-Android.

## Secciones relacionadas

- [Arquitectura NVIDIA](NLNVIDIAArquitectura.md): dependencias opcionales y conexiones.
- [Contexto previo](NLNVIDIAContexto.md): catálogo, Video Codec SDK y Jetson.
- LinkEngine/RelayCore: posible interés metodológico de benchmarks; no se cambió
  transporte ni se añadió DOCA, instrumentación o polling.
- VisionEngine: el buscador puede orientar futuras consultas específicas de video;
  ninguna de las cuatro skills evaluadas implementa el decodificador nativo pendiente.
- Omniverse/USD e IA visual: referencias futuras, sin subsistemas nuevos creados.

## Estado

Publicadas y consultadas como referencia. No instaladas ni ejecutadas en esta tarea.
No se afirma ausencia global de otras instalaciones. Sin benchmarks, pruebas de
hardware o mejoras de rendimiento verificadas. Revisión documental realizada;
no requiere nueva compilación porque solo se modifican documentos Markdown.
