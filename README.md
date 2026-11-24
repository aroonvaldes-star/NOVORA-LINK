# LINKCONTROL — copia de los clientes Android y Linux

Copia local y autocontenida de los módulos Android y Linux de **LINK Control**
extraídos de [NOVORA-LINK](https://github.com/aroonvaldes-star/NOVORA-LINK).

No es una ingeniería inversa de un APK: procede del código fuente público del
repositorio indicado. La copia incluye el proyecto Android y los archivos
compartidos que el proyecto enlazaba desde `src/WINDOWS`, reubicados en
`src/Shared` para que el proyecto pueda compilarse por separado.

## Datos del proyecto

| Campo | Valor |
| --- | --- |
| Nombre de aplicación | LINK Control |
| ID de paquete | `com.linkcontrol.novora` |
| Versión | `1.4.128` (`ApplicationVersion` 128) |
| Framework | `net10.0-android` |
| API mínima | Android 30 |
| Configuración Release | `android-arm64` |
| Proyecto | `src/Android/NLProjectAndroid.csproj` |

## Estructura

```
LINKCONTROL-Android/
├─ src/
│  ├─ Android/                 # Proyecto .NET para Android (ANTES src/ANDROID)
│  │  ├─ AndroidService/
│  │  ├─ AndroidStorage/
│  │  ├─ AndroidUI/
│  │  ├─ AndroidVideo/
│  │  ├─ AndroidVpn/
│  │  ├─ Properties/
│  │  ├─ Resources/
│  │  └─ NLProjectAndroid.csproj
│  ├─ Shared/                  # Código compartido enlazado por el csproj
│  │  ├─ Asset/
│  │  ├─ Control/
│  │  ├─ Network/
│  │  └─ VisionEngine/Protocol/
│  ├─ Desktop/                 # Adaptaciones locales de escritorio (Windows/Linux)
│  │  ├─ Streaming/            # Servicio compartido de RTMP y OBS WebSocket
│  │  ├─ Windows/UI/           # XAML y lógica de Streaming de WPF
│  │  └─ Linux/UI/             # Home, LinkEngine y Streaming de Avalonia
│  ├─ Linux/                   # Aplicación NOVORA LINK Linux (Avalonia)
│  │  ├─ LinkRelay/            # Relay Rust de LinkEngine
│  │  ├─ tests/                # Pruebas de protocolo
│  │  └─ NOVORA.Linux.csproj
│  └─ LinuxShared/             # Fuentes compartidas enlazadas por el csproj Linux
├─ docs/                       # Bitácoras, informes y documentación del repo original
│  ├─ informes/
│  ├─ legal/
│  └─ superpowers/
├─ apk/                        # APK pública descargada y su verificación SHA-256
└─ LINKCONTROL-Android.sln
```

## Requisitos para compilar

- .NET 10 SDK y .NET 8 SDK (el cliente Linux targeta `net8.0`)
- Workload de Android: `dotnet workload install android`
- Android SDK con API 30 o superior y JDK 17 o superior
- Para Linux: Rust/Cargo y un compilador C del sistema para el relay Rust;
  en ejecución también se necesitan `adb`, `ffmpeg` y SDL3

## Compilación

```bash
# Depuración (universal, adecuado para emulador)
dotnet build src/Android/NLProjectAndroid.csproj -c Debug

# Release (ARM64, teléfono físico)
dotnet build src/Android/NLProjectAndroid.csproj -c Release

# Cliente Linux (Debug y Release)
dotnet build src/Linux/NOVORA.Linux.csproj -c Debug
dotnet build src/Linux/NOVORA.Linux.csproj -c Release
```

Los scripts `build.sh` y `build-linux.sh` hacen `Rebuild` de Debug y Release
para Android y Linux respectivamente. Los `.desktop` de `~/Desktop` abren esos
scripts o ejecutan directamente el cliente Linux.

Para generar el APK firmado se necesita además una configuración de firma
propia; el repositorio de origen no incluye material de firma privado. El
proyecto original renombra el APK resultante a `LinkControl.apk` mediante el
target `LinkControlNameSignedApk`.

## APK incluida

`apk/NOVORA-Android-1.4.8-experimental.apk` es una APK pública del pre-release
`v1.4.0-experimental.1` del repositorio de origen. Su SHA-256 fue verificado
contra el `SHA256SUMS.txt` del publicador. **No es un build de esta copia**: la
fuente copiada declara `1.4.128`, mientras que esa APK es `1.4.8` experimental.

## Adaptaciones locales

### Aceleración simplificada

Cada perfil ofrece como máximo dos opciones: `Automático · <backend>` y
`Software`. Los perfiles móviles/ahorro y los equipos sin proveedor conocido
ofrecen solo `Software`. El perfil personalizado permite elegir proveedor y
backend, limitado a los backends implementados por plataforma; al guardarlo, el
selector vuelve a tener dos opciones: la personalizada y `Software`.

El contrato compartido está en
`src/Shared/Control/NLAccelerationProfile.cs`, con puentes para Linux y Windows.

### Streaming y Home

- La página de Streaming (WPF y Avalonia) incluye un recuadro de monitorización
  con **cuatro** señales: FPS, bitrate, duración y salud (velocidad de ffmpeg o
  congestión de OBS). No se muestran ceros inventados: `—` significa que la
  fuente no reporta ese dato.
- RTMP usa ffmpeg; OBS usa WebSocket v5 con `StartStream`/`StopStream`, escena
  opcional y opciones avanzadas de codificación/audio/filtros.
- La conexión/preparación de LinkEngine pasó al Home; la página LinkEngine
  conserva estado y mediciones técnicas de la sesión.

La telemetría del monitor es deliberadamente mínima. La tabla de origen de cada
dato y los campos descartados están documentados en
`docs/informes/2026-11-24-streaming-aceleracion-home.md`.

### NOVORA LINK Linux

El cliente Linux completo está en `src/Linux`; sus fuentes compartidas
mínimas están en `src/LinuxShared`. Las mismas adaptaciones de Home,
Streaming y aceleración se publicaron en la rama
`linkcontrol-linux-adaptaciones` del repositorio original. El detalle, los
resultados de compilación y la limitación del relay Rust están en
`docs/informes/2026-11-24-novora-link-linux.md`.

## Estado de esta copia

- Código fuente copiado íntegramente desde el repo de origen.
- Rutas relativas ajustadas para que el proyecto sea independiente.
- Bitácoras e informes del repositorio copiados en `docs/`.
- Registro cronológico de esta copia en `BITACORA.md`.
- Registro de esta operación en
  `docs/informes/2026-11-24-copia-android-linkcontrol.md`.
- Adaptaciones de escritorio documentadas en
  `docs/informes/2026-11-24-streaming-aceleracion-home.md`.
- `src/Desktop` contiene los archivos adaptados, no un proyecto WPF/Avalonia
  completo. Los XAML y partial classes modificados deben integrarse en el árbol
  original antes de compilar.
- El cliente Linux (`src/Linux`) sí compila en este entorno: Debug y Release
  terminaron con 0 errores y 0 advertencias.
- El SDK de Android quedó instalado, pero la compilación local de Android no
  puede terminar en esta máquina ARM64: el workload de .NET trae `aapt2` y
  `libZipSharpNative` compilados para x86-64. Se añadió el workflow
  `.github/workflows/android-rebuild.yml` para recompilar en un runner x86-64 y
  descargar los artefactos. El detalle está en
  `docs/informes/2026-11-24-compilacion-y-verificacion.md`.
- **No se generó una APK nueva localmente** y no se validó una sesión RTMP/OBS
  real. La APK incluida en `apk/` sigue siendo la pública original.

## Licencia

El código se distribuye bajo la licencia del proyecto original
**NOVORA-LINK PROPRIETARY LICENSE (NLPL) v1.1**. Se permite descargar,
estudiar y modificar localmente; la redistribución de versiones modificadas y
el uso comercial requieren autorización escrita. Consulta `docs/legal/`.

Fuente: <https://github.com/aroonvaldes-star/NOVORA-LINK>
