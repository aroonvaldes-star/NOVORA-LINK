# NOVORA — Avisos de Terceros

**Actualizado:** 21 de octubre de 2026  
**Alcance:** árbol de `NOVORA-LINK` 1.4 y binarios distribuidos por el
instalador.

Este documento identifica los componentes de terceros integrados o
utilizados por NOVORA-LINK. NOVORA no reclama propiedad sobre ellos y
conserva sus licencias, avisos y atribuciones.

## 1. Inventario de componentes distribuidos

| Componente | Versión / procedencia | Autor / proyecto | Licencia | Texto |
|---|---|---|---|---|
| QRCoder | 1.8.1 | Raffael Herrmann, Shane Krueger | MIT | `Legal/LICENSES/MIT.txt` |
| Microsoft.Extensions.Logging.Abstractions | 8.0.2 | Microsoft Corporation | MIT | `Legal/LICENSES/MIT.txt` |
| System.Security.Cryptography.ProtectedData | 8.0.0 | Microsoft Corporation | MIT | `Legal/LICENSES/MIT.txt` |
| .NET Runtime | 8.0 / 10.0 | .NET Foundation, Microsoft | MIT | `Legal/LICENSES/MIT.txt` |
| tcp_ip | 0.2.2, rev. `6ebaaad5b7379dba266fd36b3726798f70e57810` | rustp2p | Apache-2.0 | `Legal/LICENSES/Apache-2.0.txt` |
| tokio | 1.53.2 (resuelto en `Cargo.lock`; `Cargo.toml` declara 1.42) | Tokio Contributors | MIT | `Legal/LICENSES/MIT.txt` |
| serde | 1.0.229 | Serde Contributors | MIT OR Apache-2.0 | `Legal/LICENSES/MIT.txt`, `Legal/LICENSES/Apache-2.0.txt` |
| serde_json | 1.0.154 | Serde Contributors | MIT OR Apache-2.0 | `Legal/LICENSES/MIT.txt`, `Legal/LICENSES/Apache-2.0.txt` |
| bytes | 1.12.1 | Alice Ryhl, Carl Lerche, Sean McArthur | MIT | `Legal/LICENSES/MIT.txt` |
| FFmpeg | `n8.1.2-52-g5a03dfa0f6-20260913` (build BtbN `win64-lgpl-shared`) | FFmpeg project | LGPL-3.0 | `Legal/LICENSES/LGPL-3.0.txt` |
| Android Debug Bridge (ADB) | AOSP `platform-tools` | Android Open Source Project | Apache-2.0 | `Legal/LICENSES/Apache-2.0.txt` |
| SDL3 | 3.x | Sam Lantinga / SDL Contributors | zlib | `Legal/LICENSES/Zlib.txt` |

## 2. Componentes históricos

| Componente | Estado | Autor | Licencia |
|---|---|---|---|
| scrcpy | Ya no se distribuye; se conserva el reconocimiento por procedencia histórica | Genymobile / Romain Vimont | Apache-2.0 |
| Gnirehtet | Relay derivado sustituido por `NovoraRelay`; ya no se distribuye | Genymobile | Apache-2.0 |

El reconocimiento histórico no implica que esos componentes formen parte
de las versiones actuales ni que sus autores respalden NOVORA.

## 3. Herramientas de compilación

Las siguientes herramientas se usan para compilar o empaquetar, pero no se
distribuyen como parte del producto:

- Inno Setup 6/7 — Inno Setup License.
- GitHub Actions, CodeQL, DevSkim, SecurityCodeScan, Codacy.
- Rust toolchain y Cargo.
- .NET SDK y workload de Android.
- PowerShell y Python (scripts de verificación).

## 4. Cumplimiento por licencia

### 4.1 Apache-2.0

Aplica a `tcp_ip`, ADB y componentes históricos. Se conserva el texto
íntegro de la licencia y los avisos de copyright. La licencia no concede
derechos sobre las marcas de los licenciantes.

### 4.2 MIT

Aplica a QRCoder, paquetes de Microsoft, .NET, tokio, serde y bytes. Se
conservan el aviso de copyright y el texto de la licencia.

### 4.3 zlib

Aplica a SDL3. Se conserva el aviso de copyright y se prohíbe presentar
versiones alteradas como originales.

### 4.4 LGPL-3.0 (FFmpeg)

FFmpeg se distribuye como bibliotecas compartidas (`avcodec-62.dll`,
`avutil-60.dll`, `swresample-6.dll`) cargadas dinámicamente. El build es
LGPLv3 (`--enable-version3`, sin `--enable-gpl`).

Medidas de cumplimiento:

- Texto íntegro de la LGPL-3.0 en `Legal/LICENSES/LGPL-3.0.txt` y en
  `src/NOVORA/Tools/FFmpeg-LICENSE.txt`.
- Manifiesto y configuración del build en
  `src/NOVORA/Tools/FFmpeg-build.json`.
- Código fuente correspondiente incluido en el instalador como
  `Tools\Legal\Sources\ffmpeg-8.1.2.tar.xz`.
- Fuente upstream:
  https://github.com/FFmpeg/FFmpeg/tree/5a03dfa0f6
- Recetas del proveedor:
  https://github.com/BtbN/FFmpeg-Builds
- Las DLL son reemplazables por el usuario sin recompilar NOVORA-LINK.

Si en el futuro se modifica FFmpeg, deberá publicarse el código fuente
modificado conforme a la LGPL-3.0.

### 4.5 ADB

Se distribuye `adb.exe` proveniente de AOSP. Se conserva el texto
Apache-2.0 y la atribución al Android Open Source Project. ADB se usa
únicamente para funciones de desarrollo y conexión autorizada.

## 5. Limitaciones de este inventario

- No es un SBOM completo ni sustituye una auditoría de cumplimiento.
- Las dependencias transitivas de NuGet y Cargo pueden incorporar licencias
  adicionales; deben revisarse al actualizar versiones.
- Las licencias de los paquetes se conservan en sus respectivos metadatos y
  repositorios oficiales.
- La mención de un componente no implica patrocinio, afiliación ni
  respaldo.

## 6. Cómo verificar el inventario

```powershell
# Paquetes NuGet directos y transitivos
dotnet list src/NOVORA/NLProjectDesktop.csproj package --include-transitive

# Árbol de dependencias Rust
cargo tree --manifest-path `
  src/NOVORA/LinkEngine/Network/Native/NovoraRelay/Cargo.toml

# Binarios incluidos
Get-ChildItem src/NOVORA/Tools -Recurse -File |
  Where-Object { $_.Extension -in '.dll', '.exe' } |
  Select-Object FullName, Length
```

## 7. Contacto

Si detecta un componente omitido o una atribución incorrecta, escriba a
**aroonvaldes@gmail.com** con el asunto "Terceros NOVORA-LINK".
