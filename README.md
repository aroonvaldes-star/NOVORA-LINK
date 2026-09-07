# NOVORA-LINK 1.3

NOVORA-LINK es una aplicación para conectar, visualizar y controlar dispositivos Android desde Windows.

> **Versión estable actual:** NOVORA-LINK 1.3  
> **Próxima versión:** NOVORA-LINK 1.4  
> **Precio:** NOVORA-LINK 1.4 continuará siendo **GRATIS**, igual que desde el inicio del proyecto.

## Estado del proyecto

**NOVORA-LINK 1.3 es la base estable y pública actual del repositorio.**

El desarrollo de **NOVORA-LINK 1.4** continúa de forma separada sobre nuestra base de trabajo local/experimental. La rama estable no se utilizará para subir código incompleto de 1.4.

La solución estable principal está en:

- `NOVORA.sln`
- `src/NOVORA/NOVORA.csproj`

## NOVORA-LINK 1.4 — Próximamente

La versión 1.4 representa una evolución importante de NOVORA-LINK y está centrada en sustituir progresivamente funciones que antes dependían de herramientas externas por motores propios de NOVORA.

### LinkEngine

Nuevo motor de conectividad y reverse tethering de NOVORA-LINK.

Objetivos principales en desarrollo:

- Motor de red propio para PC ↔ Android.
- Cliente/APK Android de NOVORA.
- Transporte CONTROL/DATA propio.
- Gestión de TCP, UDP y DNS.
- Recovery selectivo del túnel y de las sesiones.
- Métricas reales de conectividad y tráfico.
- Menor cantidad de procesos externos.
- Arquitectura preparada para varios dispositivos.
- Reducción agresiva del polling innecesario.

### VisionEngine

Nuevo motor de visualización, audio y control de Android desde Windows.

Objetivos principales en desarrollo:

- Pipeline de video administrado directamente por NOVORA.
- Decoder y renderer de baja latencia.
- Audio integrado.
- Mouse, teclado, touch y gamepad.
- Ventana dedicada de VisionEngine.
- Modo ventana y pantalla completa.
- Selección de monitor y salida de audio.
- Clipboard e intercambio de archivos/imágenes.
- Recovery independiente del motor de conectividad.
- Métricas y control de rendimiento del stream.

### Rendimiento

NOVORA-LINK 1.4 está siendo diseñada con una regla central:

> **El mínimo polling posible; preferir eventos, señales de estado y trabajo bajo demanda.**

Durante sesiones de pantalla completa, la prioridad será mantener únicamente los componentes necesarios para video, audio, control, transporte, heartbeat y recovery. Las consultas informativas de la interfaz que no sean visibles se suspenderán cuando corresponda.

### Arquitectura propia

La dirección de 1.4 es que las funciones principales de NOVORA-LINK pasen a depender cada vez más de componentes propios:

```text
NOVORA-LINK 1.4
│
├── LinkEngine
│   ├── Core
│   ├── Device
│   ├── Transport
│   ├── Network
│   ├── Tunnel
│   ├── Recovery
│   ├── Metrics
│   ├── Performance
│   └── Protocol
│
├── VisionEngine
│   ├── Core
│   ├── Device
│   ├── Transport
│   ├── Video
│   ├── Audio
│   ├── Control
│   ├── Exchange
│   ├── Recovery
│   ├── Metrics
│   ├── Performance
│   └── Protocol
│
└── NOVORA Android Component
```

La versión 1.4 permanece **en desarrollo**. Las funciones descritas aquí pueden evolucionar antes de la publicación final.

➡️ Consulta [`ROADMAP_1.4.md`](ROADMAP_1.4.md) para el seguimiento público de las novedades.

## Estructura de la versión estable 1.3

- `src/NOVORA/` — aplicación WPF y servicios.
- `src/NOVORA/Tools/` — herramientas de ejecución restauradas por `scripts/Setup-Tools.ps1`.
- `Installer/` — instalador Inno Setup.
- `scripts/` — preparación reproducible de dependencias.
- `docs/` — arquitectura y estructura técnica.
- `.github/workflows/` — compilación y publicación de releases.

## Compilar NOVORA-LINK 1.3

```powershell
pwsh -File .\scripts\Setup-Tools.ps1
dotnet restore .\NOVORA.sln
dotnet build .\NOVORA.sln -c Release
```

## Releases

- **NOVORA-LINK 1.3** — versión estable disponible actualmente.
- **NOVORA-LINK 1.4** — próxima gran actualización, actualmente en desarrollo.

NOVORA-LINK 1.4 continuará siendo **gratuita**.

Las releases oficiales usan tags de dos componentes, por ejemplo `v1.3` o `v1.4`. GitHub Actions publica el instalador y su SHA-256 cuando una versión se encuentra lista para distribución.

## Créditos y licencias

La versión estable 1.3 utiliza componentes de terceros de acuerdo con sus respectivas licencias. Consulta `LICENSE`, `COPYRIGHT.md`, `ACKNOWLEDGEMENTS.md` y `THIRD-PARTY-NOTICES.md`.

El desarrollo de 1.4 está orientado a ampliar los motores y componentes propios de NOVORA-LINK y a reducir las dependencias externas en las funciones principales del producto.

---

**NOVORA © 2026 Aaron Yair Galarza Valdes — All Rights Reserved.**
