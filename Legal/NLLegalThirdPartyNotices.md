# NOVORA — Third-Party Notices

Este archivo identifica componentes de terceros conocidos integrados o utilizados por NOVORA.

| Componente | Autor / proyecto | Licencia |
|---|---|---|
| scrcpy | Genymobile / Romain Vimont | Apache-2.0 |
| tcp_ip | rustp2p | Apache-2.0 |
| ADB | Android Open Source Project | Apache-2.0 en los componentes ADB aplicables |
| SDL3 | Simple DirectMedia Layer | zlib License |
| FFmpeg | FFmpeg project | Sujeto a la licencia del build distribuido (LGPL/GPL según configuración) |

Las copias o binarios de terceros deben conservar sus respectivos archivos `LICENSE`, `NOTICE`, avisos de copyright y demás textos exigidos por sus licencias.

NOVORA no reclama propiedad sobre esos componentes.

## Fuentes oficiales

- scrcpy: https://github.com/Genymobile/scrcpy
- tcp_ip: https://github.com/rustp2p/tcp_ip
- ADB: https://android.googlesource.com/platform/packages/modules/adb/
- SDL: https://github.com/libsdl-org/SDL
- FFmpeg: https://ffmpeg.org/

## FFmpeg compartido de VE — 2026-09-14

Se conservan avcodec-62, avutil-60 y swresample-6 del conjunto
BtbN `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`, versión real
`n8.1.2-52-g5a03dfa0f6-20260913`. Es un build compartido LGPLv3 (`--enable-version3`,
sin `--enable-gpl`); incluye NVDEC y mantiene decodificadores software.
La licencia del paquete se distribuye en `src/NOVORA/Tools/FFmpeg-LICENSE.txt`.
[Manifiesto, configuración completa y hashes](../src/NOVORA/Tools/FFmpeg-build.json).

Fuentes de FFmpeg: https://github.com/FFmpeg/FFmpeg/tree/5a03dfa0f6
Recetas y dependencias del proveedor: https://github.com/BtbN/FFmpeg-Builds
El enlace latest es mutable: el SHA-256 conservado identifica el paquete probado.
Esta validación técnica no constituye una auditoría de cumplimiento de todas las
licencias de dependencias estáticas para una futura distribución pública.


## Video Android y relay IP

VisionEngine recibe video, audio y control desde App Control. Los ejecutables de scrcpy ya no forman parte del proyecto. Se conserva el reconocimiento histórico de scrcpy hasta completar la revisión de procedencia del protocolo multimedia y del descriptor HID.

El relay IP de LinkEngine es código de NOVORA sobre el stack `tcp_ip` (Apache-2.0). El código derivado del relay anterior fue retirado del árbol activo. El reconocimiento histórico de Gnirehtet permanece en `NLLegalAcknowledgements.md` para registrar procedencia previa.
