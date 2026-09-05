# NOVORA-LINK 1.4

> **Español (principal) · English below**

**Windows y Android. Un solo sistema.**

NOVORA-LINK es una plataforma de integración **Windows ↔ Android** orientada a conectar, visualizar, controlar y extender dispositivos Android desde Windows. La versión 1.3 permanece como la base estable publicada; la línea 1.4 desarrolla una arquitectura más integrada alrededor de **LinkEngine** y **VisionEngine**.

## Estado del proyecto

- **1.3.x:** base estable y referencia de compatibilidad.
- **1.4:** desarrollo activo.
- **LinkEngine:** motor propio de conectividad y reverse tethering, inspirado técnicamente en Gnirehtet y desarrollado para integrarse directamente con NOVORA.
- **VisionEngine:** motor propio de pantalla, audio y control, inspirado técnicamente en scrcpy y desarrollado para integrarse directamente con NOVORA.
- **ADB:** se mantiene como infraestructura intencional para descubrimiento, autorización, provisioning, bootstrap, diagnóstico y recuperación.

```text
NOVORA-LINK
│
├── LinkEngine
│   └── Red / VPN / Reverse Tethering / Recovery / Métricas
│
├── VisionEngine
│   └── Pantalla / Video / Audio / Control / Exchange / Métricas
│
└── ADB
    └── Discovery / Authorization / Provisioning / Bootstrap
```

## De 1.3 a 1.4

```text
1.3                              1.4
NOVORA                           NOVORA
├── ADB                          ├── ADB
├── scrcpy                       ├── LinkEngine
└── Gnirehtet                    └── VisionEngine
```

La transición es progresiva. scrcpy y Gnirehtet pueden permanecer temporalmente como herramientas de compatibilidad, diagnóstico, comparación o fallback mientras los motores propios alcanzan la estabilidad requerida.

## LinkEngine

LinkEngine concentra la conectividad PC → Android y la administración del túnel. Su arquitectura contempla transporte, TCP/UDP, DNS, VPN Android, sesiones, recuperación, métricas, control de congestión y provisioning del cliente Android.

## VisionEngine

VisionEngine concentra captura, transporte, decodificación, renderizado, audio, entrada, portapapeles/intercambio, métricas y recuperación del stream. La meta es que la sesión de pantalla se ejecute integrada en NOVORA sin depender de una ventana externa de scrcpy en el flujo normal.

## Compilar

```powershell
pwsh -File .\scripts\Setup-Tools.ps1
dotnet restore .\NOVORA.sln
dotnet build .\NOVORA.sln -c Release
```

## Seguridad, privacidad y aspectos legales

Los documentos del repositorio están separados por responsabilidad y son bilingües, con español priorizado:

- [`SECURITY.md`](SECURITY.md) — seguridad y divulgación responsable.
- [`PRIVACY.md`](PRIVACY.md) — tratamiento de datos y privacidad.
- [`LICENSE.md`](LICENSE.md) — explicación bilingüe de la licencia de las partes originales de NOVORA.
- [`COPYRIGHT.md`](COPYRIGHT.md) — titularidad y alcance del copyright.
- [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) — licencias y avisos de terceros.
- [`ACKNOWLEDGEMENTS.md`](ACKNOWLEDGEMENTS.md) — créditos.
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — reglas para contribuir.
- [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) — normas de participación.

El archivo `LICENSE` existente no se modifica en este cambio documental.

NOVORA-LINK no reclama autoría sobre software de terceros. Las partes reutilizadas o adaptadas conservan las obligaciones de sus licencias y avisos correspondientes.

---

# English

**Windows and Android. One system.**

NOVORA-LINK is a **Windows ↔ Android** integration platform focused on connecting, displaying, controlling and extending Android devices from Windows. Version 1.3 remains the published stable base; the 1.4 line develops a more integrated architecture around **LinkEngine** and **VisionEngine**.

## Project status

- **1.3.x:** stable base and compatibility reference.
- **1.4:** active development.
- **LinkEngine:** NOVORA connectivity and reverse-tethering engine, technically inspired by Gnirehtet and designed for direct NOVORA integration.
- **VisionEngine:** NOVORA screen, audio and control engine, technically inspired by scrcpy and designed for direct NOVORA integration.
- **ADB:** intentionally retained for discovery, authorization, provisioning, bootstrap, diagnostics and recovery.

The 1.4 transition is progressive. scrcpy and Gnirehtet may temporarily remain available for compatibility, diagnostics, comparison or fallback until the NOVORA engines reach the required stability.

## Security, privacy and legal documents

Repository policies are separated by responsibility and are bilingual, with Spanish as the primary language:

- [`SECURITY.md`](SECURITY.md)
- [`PRIVACY.md`](PRIVACY.md)
- [`LICENSE.md`](LICENSE.md)
- [`COPYRIGHT.md`](COPYRIGHT.md)
- [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)
- [`ACKNOWLEDGEMENTS.md`](ACKNOWLEDGEMENTS.md)
- [`CONTRIBUTING.md`](CONTRIBUTING.md)
- [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md)

The existing `LICENSE` file is not modified by this documentation-only change.

NOVORA-LINK does not claim authorship of third-party software. Reused or adapted third-party portions remain subject to their original licenses and required notices.

---

**Copyright © 2026 Aaron Yair Galarza Valdes. Todos los derechos reservados sobre las partes originales de NOVORA, salvo donde se indique lo contrario. / All rights reserved over original NOVORA portions, except where otherwise stated.**
