# Datos de prueba propios

Archivos .txt con contenido base64 generado localmente usando el FFmpeg BtbN
identificado en src/NOVORA/Tools/FFmpeg-build.json; no proceden del teléfono.

- Frame: `-f lavfi -i testsrc2=size=320x240:rate=30 -frames:v 1 -c:v libopenh264 -f h264`.
- RotatedFrame: mismo comando con `size=240x320`.
- Opus: `-f lavfi -i sine=frequency=440:sample_rate=48000 -ac 2 -frames:a 1 -c:a libopus`;
  primer paquete multimedia extraído del contenedor Ogg, excluyendo OpusHead/OpusTags.

Permiten comprobar AVFrame, formato para renderer, cambio de dimensiones, propiedad
de los buffers durante fallback y conservación de audio con las DLL compartidas.
No miden latencia ni calidad perceptual.
