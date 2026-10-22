# NOVORA — Agradecimientos y contribuciones de terceros

NOVORA-LINK se apoya en herramientas y bibliotecas mantenidas por terceros.
Este documento conserva el reconocimiento de los componentes actuales y de
aquellos usados históricamente. La autoría y las licencias de terceros se
mantienen separadas de la propiedad intelectual original de NOVORA.

## Componentes actuales

### FFmpeg

**Proyecto FFmpeg** — decodificación y conversión de audio y video.

Se distribuye un build compartido LGPLv3 de BtbN, sin componentes GPL.
Incluye NVDEC y decodificadores software.

- Proyecto: https://ffmpeg.org/
- Builds: https://github.com/BtbN/FFmpeg-Builds
- Licencia: LGPL-3.0

### SDL3

**Sam Lantinga y colaboradores de SDL** — capa de entrada y compatibilidad
de mandos en Windows.

- Proyecto: https://github.com/libsdl-org/SDL
- Licencia: zlib

### Android Debug Bridge (ADB)

**Android Open Source Project (AOSP)** — puente de depuración para las
funciones de conexión y desarrollo.

- Proyecto: https://android.googlesource.com/platform/packages/modules/adb/
- Licencia: Apache-2.0 en los componentes aplicables

### tcp_ip

**rustp2p** — pila de paquetes IPv4 usada por `NovoraRelay`.

- Proyecto: https://github.com/rustp2p/tcp_ip
- Licencia: Apache-2.0

### QRCoder

**Raffael Herrmann y Shane Krueger** — generación de códigos QR para el
emparejamiento LAN.

- Proyecto: https://github.com/codebude/QRCoder
- Licencia: MIT

### Microsoft .NET y paquetes asociados

**Microsoft Corporation y .NET Foundation** — runtime, SDK y paquetes
`Microsoft.Extensions.Logging.Abstractions` y
`System.Security.Cryptography.ProtectedData`.

- Proyecto: https://github.com/dotnet/runtime
- Licencia: MIT

### Ecosistema Rust

**Tokio Contributors, Serde Contributors y autores de `bytes`** — runtime
asíncrono, serialización y buffers del relay.

- Tokio: https://github.com/tokio-rs/tokio — MIT
- Serde: https://github.com/serde-rs/serde — MIT OR Apache-2.0
- bytes: https://github.com/tokio-rs/bytes — MIT

## Componentes históricos

### scrcpy

**Genymobile / Romain Vimont (rom1v)** — herramienta para mostrar y
controlar dispositivos Android.

Uso histórico; el ejecutable y el servidor ya no se distribuyen. El
reconocimiento se conserva por la procedencia del protocolo y del
descriptor HID.

- Proyecto: https://github.com/Genymobile/scrcpy
- Licencia: Apache-2.0
- Copyright: (C) 2018 Genymobile, (C) 2018-2026 Romain Vimont

### Gnirehtet

**Genymobile** — reverse tethering para Android mediante ADB.

Uso histórico; el relay derivado fue sustituido por `NovoraRelay` y ya no
se distribuye.

- Proyecto: https://github.com/Genymobile/gnirehtet
- Licencia: Apache-2.0
- Copyright: (C) 2017 Genymobile

## Herramientas de desarrollo

Agradecemos también a los proyectos que hacen posible la compilación,
verificación y publicación:

- Inno Setup
- GitHub Actions
- CodeQL, DevSkim, SecurityCodeScan y Codacy
- Rust, Cargo y el ecosistema de crates
- .NET SDK y workload de Android
- PowerShell y Python

## Reconocimiento

NOVORA agradece expresamente a las personas y proyectos que mantienen estas
herramientas por el trabajo de ingeniería que hace posible integrar captura
de pantalla, audio, control, archivos, VPN y mandos en aplicaciones como
NOVORA-LINK.

Este reconocimiento no implica que Genymobile, Romain Vimont, el Android
Open Source Project, la .NET Foundation, Microsoft, rustp2p ni los demás
autores sean creadores, propietarios, patrocinadores o responsables de
NOVORA-LINK.

Para el inventario con versiones y textos de licencia, consulte
`Legal/NLLegalThirdPartyNotices.md` y `Legal/LICENSES/`.
