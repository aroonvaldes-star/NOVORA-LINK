# 2026-11-24 — Contraste con el repositorio actual LINK-CONTROL

Repositorio examinado: <https://github.com/aroonvaldes-star/LINK-CONTROL>,
rama por defecto `novora-appcontrol`, HEAD `207f132`. Se clonó en
`/tmp/opencode/LINK-CONTROL` solo para lectura; no se modificó ni se hizo push.

## Qué contiene hoy LINK-CONTROL

- Es el repositorio del compañero Android de NOVORA-LINK. `src/NOVORA.Android`
  tiene 37 archivos C#; `src/NOVORA` solo contratos, utilidades y recursos
  enlazados para Android y sus pruebas.
- La fuente versionada es el snapshot del 19 de octubre de 2026:
  `1.4.113 / código 113` en `NLProjectAndroid.csproj`. La última publicación es
  `v1.4.128`, según el README.
- No hay proyectos de escritorio Windows/Linux, ni servicio de streaming, ni
  código RTMP/OBS WebSocket, ni `NLAccelerationProfile.cs`.
- El selector «Perfil de VisionEngine y NVIDIA» de `NLAndroidUIActivity.cs` se
  llena con `snapshot.Profiles`, que envía la PC. Android no decide las opciones;
  el texto ayuda dice que solo los perfiles distintos de Software permiten
  solicitar NVIDIA.
- El «monitoreo en vivo» que aparece en la UI es de tráfico VPN de LinkEngine
  (`NLLinkMonitorSample` en `NLAndroidVpnService`), no un monitor de emisión
  OBS/RTMP.
- CI: `.github/workflows/ci.yml` compila con `-t:Compile` y
  `AndroidBuildApplicationPackage=false`, ejecuta suites de contrato y no
  firma ni publica APK.

## Qué se hizo en esta copia

- Rama `linkcontrol-android-adaptaciones` de NOVORA-LINK, sobre fuente Android
  `1.4.128 / código 128` (51 archivos C# en el árbol Android, más Shared y
  Desktop).
- `NLAccelerationProfile.cs`: modos `Software`, `Automatic` y `Custom`;
  proveedores Nvidia, Intel, AMD y Apple; backends implementados
  `NvidiaNvdec`/`cuda`, `qsv`, `amf`/`vaapi`, `VideoToolbox`. Máximo dos opciones
  por perfil y perfil personalizado acotado a backends reales.
- Servicio de streaming de escritorio con RTMP y OBS WebSocket v5, opciones
  avanzadas y monitor mínimo de cuatro señales verificables.
- Home con LinkEngine en WPF y Avalonia; cliente Linux completo adaptado en la
  rama `linkcontrol-linux-adaptaciones`.

## Divergencia de archivos comunes

Comparación entre `src/Shared` de esta copia y `src/NOVORA` de LINK-CONTROL:

| Archivo | Líneas diferentes |
| --- | ---: |
| `Control/NLControlCapturePolicy.cs` | 491 |
| `Control/NLControlProtocol.cs` | 273 |
| `Network/NLNetworkPacer.cs` | 258 |
| `Network/NLNetworkBudget.cs` | 226 |
| `Control/NLControlNetworkPolicy.cs` | 56 |

`Control/NLControlMobileData.cs` no existe en LINK-CONTROL. La divergencia
confirma que no son el mismo corte de código: un cherry-pick directo desde esta
copia a LINK-CONTROL produciría conflictos y mezclaría versiones.

## Artefacto Android generado por el workflow

El run `45367818371` produjo APK desde la fuente modificada `1.4.128`:

| Archivo | Tamaño |
| --- | ---: |
| `Debug/net10.0-android/com.linkcontrol.novora-Signed.apk` | 52.352.803 bytes |
| `Release/net10.0-android/android-arm64/com.linkcontrol.novora-Signed.apk` | 14.408.478 bytes |

Datos del Release firmado:

- Paquete: `com.linkcontrol.novora`
- Versión: `1.4.128` / código `128`
- SDK mínimo: 30
- SHA-256: `a15d4999075539710d60bef33663f89386c8137a4c0f5b460328484e553c2f20`

**No es una publicación oficial.** Está firmada con la firma de depuración del
CI, no con la clave `CN=LINK Control` vigente desde `1.4.125`, y usa el mismo
número de versión que `v1.4.128` ya publicada. La política de LINK-CONTROL exige
versión `N+1`, firma compatible, `NLAndroidRelease.json` y `SHA256SUMS.txt`.
Para publicarla habría que subir a `1.4.129`, firmar con la clave autorizada y
generar esos artefactos.

## Conclusión

- Las adaptaciones de aceleración y streaming no procede migrarlas a
  LINK-CONTROL: viven en el árbol de escritorio de NOVORA-LINK y LINK-CONTROL
  solo consume las opciones que recibe de la PC.
- Cambio opcional y de bajo riesgo en LINK-CONTROL: actualizar el texto
  «Perfil de VisionEngine y NVIDIA» para mencionar también Intel, AMD y el
  perfil personalizado, o dejar el spinner tal cual porque ya refleja dinámicamente
  las opciones del PC.
- Antes de cualquier port entre repositorios hay que sincronizar la fuente
  Android de LINK-CONTROL (`1.4.113`) con el corte `1.4.128` de NOVORA-LINK.
