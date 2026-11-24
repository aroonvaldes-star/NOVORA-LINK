# 2026-11-24 — Compilación y verificación en el entorno local

## Resultados

| Proyecto | Comando | Resultado |
| --- | --- | --- |
| Linux (copia local) | `dotnet build src/Linux/NOVORA.Linux.csproj -c Debug -t:Rebuild` | 0 errores, 0 advertencias |
| Linux (copia local) | `dotnet build src/Linux/NOVORA.Linux.csproj -c Release -t:Rebuild` | 0 errores, 0 advertencias |
| Linux (rama de origen) | `dotnet build src/LINUX/NOVORA.Linux.csproj -c {Debug,Release} -t:Rebuild` | 0 errores, 0 advertencias |
| Android (copia local) | `dotnet build src/Android/NLProjectAndroid.csproj -c Debug -t:Rebuild` | Bloqueado por herramientas x86-64 del workload en host ARM64 |
| Relay Rust | `cargo build --release --manifest-path src/Linux/LinkRelay/Cargo.toml` | Bloqueado: falta `cc` |

Los ensamblados Linux se generaron en:

- `src/Linux/bin/Debug/net8.0/NOVORA-LINK.Linux.dll`
- `src/Linux/bin/Release/net8.0/NOVORA-LINK.Linux.dll`

## Android: instalación y bloqueo

1. El destino `dotnet build -t:InstallAndroidDependencies` falló con
   `System.DllNotFoundException: Mono.Unix`. El instalador de .NET no encontró
   esa librería nativa en el workload.
2. Se instaló el Android SDK con `sdkmanager` de Google (API 36, Build-Tools
   36.0.0, platform-tools) bajo `~/.local/lib/android-sdk`, con JDK 21 en
   `~/.local/lib/jvm/jdk-21.0.13.1+2`.
3. El build avanzó hasta `aapt2`, pero el `aapt2` del workload y el de
   Build-Tools son ELF x86-64 (`e_machine=62`) mientras la máquina es ARM64.
4. Se descargó `aapt2` ARM64 desde la release v1.1.0 de `ReVanced/aapt2`,
   verificado con SHA-256
   `7e5ae2e1f62fc24cab14072555ffd0a1a7e1ce27e82cc1008967b835b6d8df5b`.
   Pasando `-p:Aapt2ToolPath=$HOME/.local/bin` (directorio) el build superó
   AAPT2 y llegó a `Javac`.
5. En `Javac` apareció el mismo problema con
   `libZipSharpNative-3-3.so` (también x86-64), por lo que el APK no se pudo
   producir localmente.

Para compilar en un entorno x86-64 se añadió
`.github/workflows/android-rebuild.yml`. El workflow instala .NET 10 y el
workload Android, ejecuta `Rebuild` de Debug y Release con
`AndroidBuildApplicationPackage=true` y publica los resultados como artefacto
`linkcontrol-android-rebuild`. Se puede lanzar manualmente con
`workflow_dispatch` o automáticamente al hacer push a
`linkcontrol-android-adaptaciones`.

## Relay Rust

El caché Git de Cargo guardaba referencias temporales inválidas
(`refs/commit/.l2s.…`). Se sorteó clonando `tcp_ip` en `/tmp/opencode/tcp_ip` y
aplicando un `patch` temporal de Cargo. Después la compilación se detuvo en
`proc-macro2`, `quote`, `libc` y otros crates porque no hay linker C (`cc`) en
el sistema. El `csproj` de la copia solo incluye/copia el relay cuando el
binario existe, así que esto no bloquea la compilación administrada.

## Sin validar

No se conectó un teléfono, un servidor RTMP ni una sesión OBS real. Las cuatro
señales del monitor se implementaron contra los campos documentados de OBS
WebSocket v5 (`GetStreamStatus`/`GetStats`) y se muestran como `—` cuando el
backend no reporta el dato.
