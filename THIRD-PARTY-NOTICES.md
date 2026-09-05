# NOVORA-LINK — Avisos de terceros / Third-Party Notices

> **Español (principal) · English below**

## Español

Este archivo identifica componentes de terceros conocidos utilizados, integrados, descargados, redistribuidos o tomados como referencia técnica por NOVORA-LINK. Su presencia no implica autoría, patrocinio ni respaldo de NOVORA por parte de sus autores originales.

### Componentes principales

| Componente | Referencia conocida | Proyecto / Autor | Licencia |
|---|---|---|---|
| scrcpy | 4.1 Windows x64 en la línea 1.3 | Genymobile / Romain Vimont | Apache-2.0 |
| Gnirehtet | 2.5.1 Rust Windows x64 en la línea 1.3 | Genymobile | Apache-2.0 |
| Android Debug Bridge (ADB) | Android Platform Tools / paquete compatible | Android Open Source Project | Licencias AOSP aplicables; Apache-2.0 en componentes ADB correspondientes |

### scrcpy

Copyright (C) 2018 Genymobile  
Copyright (C) 2018-2026 Romain Vimont

Proyecto oficial: https://github.com/Genymobile/scrcpy

scrcpy ha servido como referencia técnica y, cuando su licencia lo permite, como fuente de código reutilizable o adaptable para el desarrollo de VisionEngine. Cualquier porción derivada debe conservar los avisos, atribuciones y obligaciones exigidas por Apache License 2.0.

VisionEngine no debe presentarse como autoría original de NOVORA sobre las porciones que provengan de scrcpy. Las extensiones y arquitectura desarrolladas independientemente por NOVORA mantienen su titularidad propia.

### Gnirehtet

Copyright (C) 2017 Genymobile

Proyecto oficial: https://github.com/Genymobile/gnirehtet

Gnirehtet ha servido como referencia técnica y, cuando su licencia lo permite, como fuente de código reutilizable o adaptable para LinkEngine. Las partes derivadas mantienen sus atribuciones y obligaciones originales.

### Android Debug Bridge (ADB)

Android Open Source Project.

Fuente del módulo ADB: https://android.googlesource.com/platform/packages/modules/adb/

NOVORA-LINK no reclama propiedad sobre ADB ni sobre componentes Android/Google redistribuidos bajo sus propias licencias.

### Dependencias transitivas

Las builds de terceros pueden incorporar bibliotecas adicionales. La línea 1.3 ha identificado, entre otras, dependencias como FFmpeg, SDL3, dav1d y libusb dentro del ecosistema de scrcpy para Windows. Estas bibliotecas conservan sus propias licencias y obligaciones.

Cuando una release redistribuya una dependencia sujeta a requisitos adicionales —incluido copyleft débil— el proyecto deberá conservar los avisos, licencias, mecanismos de relink o código fuente correspondiente cuando legalmente aplique.

### Reglas de redistribución

NOVORA-LINK debe:

1. conservar avisos de copyright y licencia exigidos por cada componente;
2. incluir copias de las licencias cuando la licencia aplicable lo requiera;
3. identificar modificaciones cuando corresponda;
4. no eliminar atribuciones originales al reutilizar código en LinkEngine o VisionEngine;
5. revisar las obligaciones legales cada vez que cambie una versión, codec, DLL o conjunto de dependencias;
6. no describir como código original de NOVORA las porciones derivadas de terceros.

La existencia de este archivo no constituye una certificación jurídica de cumplimiento total. Cada release debe mantener su propia revisión de dependencias y redistribución.

---

## English

This file identifies known third-party components used, integrated, downloaded, redistributed or used as technical references by NOVORA-LINK. Their presence does not imply authorship, sponsorship or endorsement of NOVORA by the original authors.

### Main components

| Component | Known reference | Project / Author | License |
|---|---|---|---|
| scrcpy | 4.1 Windows x64 in the 1.3 line | Genymobile / Romain Vimont | Apache-2.0 |
| Gnirehtet | 2.5.1 Rust Windows x64 in the 1.3 line | Genymobile | Apache-2.0 |
| Android Debug Bridge (ADB) | Android Platform Tools / compatible package | Android Open Source Project | Applicable AOSP licenses; Apache-2.0 for relevant ADB components |

### scrcpy

Copyright (C) 2018 Genymobile  
Copyright (C) 2018-2026 Romain Vimont

Official project: https://github.com/Genymobile/scrcpy

scrcpy has served as a technical reference and, where its license permits, as a source of reusable or adaptable code for VisionEngine. Derived portions must retain notices, attribution and Apache License 2.0 obligations.

### Gnirehtet

Copyright (C) 2017 Genymobile

Official project: https://github.com/Genymobile/gnirehtet

Gnirehtet has served as a technical reference and, where permitted, as a source of reusable or adaptable code for LinkEngine. Derived portions retain their original attribution and licensing obligations.

### Android Debug Bridge (ADB)

Android Open Source Project.

ADB source: https://android.googlesource.com/platform/packages/modules/adb/

NOVORA-LINK does not claim ownership of ADB or Android/Google components distributed under their own licenses.

### Transitive dependencies

Third-party builds may include additional libraries. The 1.3 line identified dependencies including FFmpeg, SDL3, dav1d and libusb in the Windows scrcpy ecosystem. These libraries retain their own licenses and obligations.

When a release redistributes dependencies with additional requirements, including weak copyleft requirements, NOVORA-LINK must preserve required notices, licenses and corresponding source or relinking mechanisms where legally applicable.

### Redistribution rules

NOVORA-LINK must preserve required notices and licenses, identify modifications where required, retain original attribution in LinkEngine/VisionEngine derivative portions, review obligations whenever dependency versions change, and never describe third-party-derived portions as original NOVORA code.

This document is not a legal certification of complete compliance. Each release must maintain its own dependency and redistribution review.
