# Modelos NVIDIA para audio y efectos de video

Consulta: 2026-09-14. Estado: documentación oficial consultada; modelos no descargados,
servicios no desplegados, APIs no ejecutadas y funciones no integradas en NOVORA.
Esta evaluación documenta propuestas; no modifica código de ejecución.

## Nemotron ASR Streaming

[Demo](https://build.nvidia.com/nvidia/nemotron-asr-streaming): transcripción de voz
continua; la página presenta inglés. La [documentación de despliegue](https://docs.nvidia.com/nim/speech/latest/asr/deploy-asr-models/nemotron-asr-streaming.html)
distingue variantes en-US y multi. La [matriz](https://docs.nvidia.com/nim/speech/latest/reference/support-matrix/asr.html)
incluye es-ES y es-US en multi, y marca Nemotron ASR Streaming sin soporte WSL2.
El requisito general del NIM es GPU NVIDIA con capacidad de cómputo >=8.0 y al menos
16 GB de VRAM; los consumos de perfiles concretos son otra medida. No se comprobó
un despliegue local ni precisión con español mexicano.

Valoración propia: primer candidato para subtítulos y transcripción opcionales de
VE. ASR produce texto; traducirlo o ejecutar comandos necesitaría lógica adicional.
Propuesta: consumir copias de bloques de audio con cola acotada, publicar resultados
por eventos y no detener reproducción si el servicio tarda. Un servicio remoto
permitiría usar la función desde PCs sin GPU NVIDIA, pero implicaría enviar audio
al servidor seleccionado. No se envió audio en esta evaluación.

## Nemotron VoiceChat

[Demo](https://build.nvidia.com/nvidia/nemotron-voicechat) y
[acceso anticipado](https://developer.nvidia.com/nemotron-voicechat-early-access).
NVIDIA lo describe como modelo de voz a voz de 12B parámetros que integra
reconocimiento, conversación y síntesis; admite escuchar y hablar simultáneamente.
El acceso anticipado proporciona recursos a desarrolladores elegibles.

Valoración propia: posible asistente opcional para ayuda y accesibilidad. No aporta
mejora directa de codec o FPS. Antes de proponer despliegue deben verificarse acceso,
idiomas, requisitos y latencia. Las respuestas del modelo no deben convertirse por
sí solas en órdenes de control del teléfono. No se probó voz ni ejecución de acciones.

## Relighting

[Demo](https://build.nvidia.com/nvidia/relighting),
[descripción](https://docs.nvidia.com/nim/maxine/relighting/latest/overview.html) y
[compatibilidad](https://docs.nvidia.com/nim/maxine/relighting/latest/support-matrix.html).
Aplica iluminación a personas usando entornos HDR y composición de fondo. Ofrece
procesamiento de archivos y streaming por fragmentos. La matriz enumera Linux,
Docker, NVIDIA Container Toolkit y hardware NVENC/NVDEC; no se verificó funcionamiento
en la computadora actual.

Valoración propia: pertinente para cámara, presentaciones o grabaciones con personas.
No se propone aplicarlo al espejo de juegos o interfaces, donde alteraría la imagen.
El streaming por fragmentos no demuestra latencia adecuada para control interactivo.
Una evaluación debe comparar fidelidad, coste GPU y demora con el efecto apagado.

## Decisión propuesta y conexiones

Prioridad funcional: ASR para subtítulos; VoiceChat si se decide construir un asistente;
Relighting si aparece un caso concreto de cámara o edición. Ninguno se considera una
optimización demostrada de FPS ni un requisito de arranque de NOVORA.

VE conservaría audio/video y sus alternativas comunes; NVIDIA alojaría adaptadores
específicos. El servicio de IA sería opcional e intercambiable, con cancelación y
resultados por eventos. Estas conexiones son propuestas, no código existente.

- [Índice de referencias NVIDIA](NLNVIDIASkills.md).
- [Contexto NVIDIA](NLNVIDIAContexto.md).
- [Rendimiento de VE](../../../Documentation/NLDocumentationVisionEnginePerformance.md).
- [NVDEC implementado previamente](NLNVIDIANvdec.md), independiente de estos modelos.
