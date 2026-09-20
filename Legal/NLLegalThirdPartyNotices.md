# NOVORA — Third-Party Notices

Este archivo identifica componentes de terceros conocidos integrados o utilizados por NOVORA.

| Componente | Autor / proyecto | Licencia |
|---|---|---|
| scrcpy | Genymobile / Romain Vimont | Apache-2.0 |
| Gnirehtet | Genymobile | Apache-2.0 |
| ADB | Android Open Source Project | Apache-2.0 en los componentes ADB aplicables |
| SDL3 | Simple DirectMedia Layer | zlib License |
| FFmpeg | FFmpeg project | Sujeto a la licencia del build distribuido (LGPL/GPL según configuración) |

Las copias o binarios de terceros deben conservar sus respectivos archivos `LICENSE`, `NOTICE`, avisos de copyright y demás textos exigidos por sus licencias.

NOVORA no reclama propiedad sobre esos componentes.

## Fuentes oficiales

- scrcpy: https://github.com/Genymobile/scrcpy
- Gnirehtet: https://github.com/Genymobile/gnirehtet
- ADB: https://android.googlesource.com/platform/packages/modules/adb/
- SDL: https://github.com/libsdl-org/SDL
- FFmpeg: https://ffmpeg.org/

## FFmpeg compartido de VE — 2026-09-14

Se sustituyeron avcodec-62, avutil-60, avformat-62 y swresample-6 por el conjunto
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


## Backend temporal de VisionEngine

VisionEngine conserva temporalmente `scrcpy-server` como backend Android compatible mientras NOVORA desarrolla y valida una sustitución propia. Su uso no se presenta como código original de NOVORA. Consulte `Documentation/ThirdParty/NLDocumentationScrcpyServerProvenance.md`.
