# NOVORA — Third-Party Notices

Este archivo identifica componentes de terceros conocidos integrados, descargados o redistribuidos por NOVORA-LINK. La presencia de un componente aquí no implica autoría, patrocinio ni respaldo de NOVORA por parte de sus autores originales.

## Componentes directos

| Componente | Versión fijada en NOVORA-LINK 1.3 | Autor / proyecto | Licencia |
|---|---:|---|---|
| scrcpy | 4.1 Windows x64 | Genymobile / Romain Vimont | Apache-2.0 |
| Gnirehtet | 2.5.1 Rust Windows x64 | Genymobile | Apache-2.0 |
| Android Debug Bridge (ADB) | incluida por el paquete Windows de scrcpy | Android Open Source Project | Apache-2.0 en los componentes aplicables; preservar NOTICE |

### scrcpy

Copyright (C) 2018 Genymobile  
Copyright (C) 2018-2026 Romain Vimont

Proyecto oficial: https://github.com/Genymobile/scrcpy  
Etiqueta revisada: https://github.com/Genymobile/scrcpy/tree/v4.1

### Gnirehtet

Copyright (C) 2017 Genymobile

Proyecto oficial: https://github.com/Genymobile/gnirehtet  
Versión revisada: https://github.com/Genymobile/gnirehtet/tree/v2.5.1

### Android Debug Bridge (ADB)

Android Open Source Project.

Fuente del módulo ADB: https://android.googlesource.com/platform/packages/modules/adb/

NOVORA-LINK no reclama propiedad sobre ADB ni sobre los binarios Android/Google que acompañan al paquete upstream.

## Dependencias transitivas del paquete Windows de scrcpy 4.1

La build oficial de scrcpy 4.1 incorpora/copia dependencias adicionales. Las versiones verificadas en los scripts upstream de la etiqueta v4.1 son:

| Componente | Versión | Licencia principal | Fuente |
|---|---:|---|---|
| FFmpeg | 8.1.2 | LGPL-2.1-or-later bajo la configuración usada por scrcpy v4.1 sin `--enable-gpl` | https://ffmpeg.org/ |
| SDL3 | 3.4.12 | zlib | https://www.libsdl.org/ |
| dav1d | 1.5.3 | BSD-2-Clause | https://code.videolan.org/videolan/dav1d |
| libusb | 1.0.30 | LGPL-2.1-or-later | https://libusb.info/ |

Estas dependencias no se convierten en código propietario de NOVORA por estar incluidas dentro del instalador.

## Reglas de redistribución

NOVORA-LINK debe:

1. conservar los archivos `LICENSE`, `NOTICE`, `COPYING` y avisos de copyright exigidos por las licencias aplicables;
2. entregar una copia de Apache License 2.0 cuando redistribuya scrcpy, Gnirehtet o componentes ADB sujetos a esa licencia;
3. identificar los archivos modificados cuando una licencia upstream lo exija;
4. no eliminar atribuciones originales al reutilizar código en LinkEngine o ScreenEngine;
5. revisar las obligaciones de dependencias transitivas, especialmente FFmpeg y libusb bajo LGPL;
6. proporcionar el código fuente correspondiente u otra vía jurídicamente válida cuando una licencia copyleft débil lo requiera para los binarios distribuidos;
7. repetir la revisión cuando cambie una versión, build, codec o conjunto de DLLs.

## Estado de cumplimiento

La autoría y licencias directas de scrcpy, Gnirehtet y ADB han sido verificadas contra sus fuentes upstream. También se identificaron las principales dependencias de la build Windows de scrcpy 4.1.

La existencia de este archivo por sí sola **no equivale a una certificación jurídica de cumplimiento total**. En particular, la redistribución de FFmpeg/libusb requiere mantener una estrategia de licencia y fuente correspondiente. Consulta `docs/COPYRIGHT-COMPLIANCE.md` para el estado detallado y acciones pendientes.

## Documentos relacionados

- `LICENSE` — licencia propietaria de las partes originales de NOVORA.
- `COPYRIGHT.md` — titularidad del proyecto original.
- `ACKNOWLEDGEMENTS.md` — créditos de terceros.
- `docs/COPYRIGHT-COMPLIANCE.md` — revisión técnica de cumplimiento.
- `SECURITY.md` — política de seguridad.
- `PRIVACY.md` — política de privacidad.
