# NVIDIA — contexto y seguimiento de NOVORA-LINK

Modelos evaluados: [ASR Streaming, VoiceChat y Relighting](NLNVIDIAModelosAudioVideo.md); propuestas de audio y efectos, sin integración ejecutada.

Estado posterior: [integración real de NVDEC mediante FFmpeg](NLNVIDIANvdec.md).
Los diagnósticos de ausencia de backend que aparecen debajo son históricos.

Estado ejecutado: [perfiles, mediciones y limitaciones](NLNVIDIAValidacion.md).

Evaluación adicional: [Skill Finder, DOCA Bench, Omniverse/USD y aumento de datos de video para IA](NLNVIDIASkills.md).

Fecha de recopilación: 2026-09-14.

## Propósito

Esta carpeta centraliza la investigación, referencias, decisiones,
propuestas y resultados relacionados con NVIDIA para NOVORA-LINK,
incluyendo lo conversado antes de crearla.

Proyecto local:
C:\Users\Aroon\Desktop\NL2\NOVORA-LINK

## Reglas acordadas

- NVIDIA debe ser opcional para ejecutar NOVORA-LINK.
- Mantener una base con alternativas para AMD, Intel y gráficos integrados.
- No afirmar compatibilidad o aceleración sin pruebas correspondientes.
- Usar eventos y esperas por datos; evitar polling continuo.
- Modificar código o repositorio requiere autorización previa.
- Publicar en GitHub requiere autorización explícita.
- Entregar cambios mediante PowerShell copiable, sin archivos .ps1.
- Revisar por segunda vez antes de entregar o aplicar código.
- Distinguir revisión de código, compilación, pruebas y ejecución real.
- Enlazar y actualizar las secciones afectadas por cada investigación,
  dentro del alcance autorizado.

## Referencias compartidas

Catálogo:
https://build.nvidia.com/skills?filters=domain%3Adomain_graphics_and_media%2Clibrary%3Alibrary_deepstream_sdk%2Clibrary%3Alibrary_video_codec

Repositorio oficial:
https://github.com/NVIDIA/skills

Video Codec SDK:
https://developer.nvidia.com/video-codec-sdk

DeepStream:
https://github.com/NVIDIA/DeepStream

Skill Jetson Video Pipeline:
https://github.com/NVIDIA/skills/tree/main/skills/jetson-video-pipeline

Instrucciones de esa skill:
https://github.com/NVIDIA/skills/blob/main/skills/jetson-video-pipeline/SKILL.md

## Conclusiones de la conversación

Las skills ayudan al asistente durante el desarrollo. Instalarlas no
activa aceleración gráfica ni incorpora automáticamente mejoras al programa.

Video Codec SDK es una referencia para estudiar codificación y
decodificación opcionales con NVIDIA en Windows.

DeepStream se consideró para necesidades concretas de análisis visual
con inteligencia artificial. No se acordó incorporarlo al núcleo.

Jetson Video Pipeline requiere ejecución sobre el Jetson de destino.
Se revisó su SKILL.md como referencia; no se instaló ni ejecutó y sus
scripts no fueron auditados.

Su método comprueba entradas y salidas, identidad de archivos, imágenes
procesadas y resultados parciales. No cubre captura, transporte,
presentación, latencia completa ni métricas de calidad PSNR/SSIM.

## Propuesta pendiente de implementación

1. Revisar arquitectura y recorrido real del video en la carpeta actual.
2. Diseñar una prueba propia para Windows, ejecutada bajo demanda.
3. Identificar el material de prueba y registrar el decodificador utilizado.
4. Comprobar imágenes producidas, errores y tiempos de procesamiento.
5. Informar cada etapa y conservar resultados parciales.
6. Comparar una ruta NVIDIA opcional con la alternativa de referencia.
7. Comprobar por separado las alternativas de otros fabricantes.

La huella de un archivo identifica el material: no demuestra calidad visual.
El tiempo de decodificación no equivale a la latencia Android-pantalla.
No calcular huellas por cada imagen durante el uso normal como consecuencia
de esta propuesta.

## Código existente localizado

Carpeta:
./

Archivos observados:
- NLNVIDIACapabilities.cs
- NLNVIDIAManager.cs
- NLNVIDIAPipeline.cs
- NLNVIDIAPolicy.cs
- NLNVIDIAProfile.cs
- NLNVIDIAStatus.cs

El código fue trasladado y sus referencias actualizadas. Véase NLNVIDIAArquitectura.md y el registro de validación para el estado actual.

## Antecedentes históricos

La memoria de trabajos en la carpeta anterior indicaba una política NVIDIA
opcional y una alternativa FFmpeg. Ese antecedente puede estar desactualizado
y debe contrastarse con el código actual antes de reutilizarlo como evidencia.

## Estado histórico de la consulta previa a la reorganización

- Referencias oficiales consultadas durante la conversación.
- SKILL.md de Jetson Video Pipeline leído como material de análisis.
- Sin instalación o ejecución de esa skill.
- Sin implementación de la propuesta de pruebas para Windows.
- Sin aceleración ni compatibilidad gráfica verificadas en esta tarea.
- Sin compilación o pruebas de ejecución realizadas para esta recopilación.

## Organización de futuras investigaciones

Cada investigación debe registrar:
- Problema y componente afectado.
- Fuentes consultadas y fecha.
- Propuesta y autorización correspondiente.
- Cambios realizados.
- Evidencia de compilación, pruebas y ejecución.
- Limitaciones y pendientes.
- Referencias a otras secciones afectadas.

La reorganización fue autorizada posteriormente y se documenta en NLNVIDIAArquitectura.md.
