# Auditoría de fiabilidad, seguridad y optimización de NOVORA-LINK

**Fecha:** 10 de octubre de 2026  
**Objeto:** árbol local actual de `E:\NOVORA\NOVORA LINK`, incluidos cambios sin confirmar.  
**Método:** revisión estática focalizada, pruebas existentes offline y comprobaciones locales no destructivas. La revisión de seguridad cubrió superficies seleccionadas de un inventario de 516 archivos; no representa una auditoría exhaustiva de cada archivo.

## Dictamen

La aplicación tiene controles relevantes de emparejamiento, TLS, almacenamiento de credenciales y límites de colas. Aun así, dos entradas malformadas pueden detener componentes de LinkEngine, y una entrada de archivo puede cerrar el canal FILES de VisionEngine en Android. Las pruebas locales disponibles pasan, pero no demuestran fiabilidad ni rendimiento durante una sesión real PC-Android. **No hay evidencia suficiente para declarar la versión lista por fiabilidad, seguridad y rendimiento de extremo a extremo.**

## Hallazgos priorizados

| Prioridad | Área | Resultado y alcance |
| --- | --- | --- |
| P1 | Seguridad y fiabilidad de LE | Un paquete IPv4 que declara longitud menor al encabezado mínimo puede causar `panic` en RelayCore. El listener DATA acepta una conexión en loopback sin autenticación propia; el mapeo ADB reverse puede ampliar el acceso desde Android. Impacto confirmado por flujo de código: caída del relay y del túnel activo. Acceso real desde una app Android ajena: pendiente de prueba. |
| P1 | Seguridad y fiabilidad de LE LAN | Un saludo DATA de 64 caracteres no hexadecimales pasa la comprobación de longitud y hace que `Convert.FromHexString` lance una excepción no capturada. La tarea del gateway termina y la oferta queda retenida. Requiere acceso al puerto efímero del gateway durante una oferta LAN. |
| P2 | Fiabilidad VE LAN | `NLAndroidLanFileReceiver` captura sólo `IOException`, `InvalidDataException` e `InvalidOperationException`. JSON o datos hexadecimales malformados pueden terminar su tarea; el servicio registra el fallo pero no reabre FILES durante esa sesión. El par ya debe estar autenticado, por lo que se clasifica aquí como defecto de fiabilidad. |
| P2 | Rendimiento LE | La reserva de espacio para UDP pequeño reduce presión de cola, pero el tráfico sigue en una sola FIFO. Un paquete sensible puede esperar detrás de datos ya encolados. Además, durante dos segundos después de detectar UDP pequeño, el límite para tráfico bulk baja a 64 KiB. Es una compensación de diseño que requiere medición de latencia y caudal, no una mejora demostrada. |
| Resuelto en fuente | Integración VE y release | Se retiraron el ejecutable y servidor heredados; VisionEngine inicia video desde App Control. Falta validar la captura en un teléfono físico y generar un nuevo instalador. |

### Evidencia principal

- **Relay:** `src/NOVORA/LinkEngine/Network/Native/RelayCore/src/relay/LERelayTunnelServer.rs:76-79,96-158`; `LERelayIpv4PacketBuffer.rs:40-61`; `LERelayIpv4Header.rs:49-60`; `LERelayClient.rs:638-667`; `src/NOVORA/LinkEngine/Transport/LETransportData.cs:61-91`.
- **Gateway DATA LAN:** `src/NOVORA/Control/NLControlLanDataGateway.cs:44-79,91-99`; `src/NOVORA/UI/NLUIWindowMainAndroidEngines.cs:296-315`.
- **FILES Android:** `src/NOVORA.Android/AndroidStorage/NLAndroidLanFileReceiver.cs:42-68,71-76,108-114`; `src/NOVORA.Android/AndroidService/NLAndroidServiceVeLanCapture.cs:176-182`.
- **Cola LE:** `src/NOVORA/LinkEngine/Network/Native/RelayCore/src/relay/LERelayTrafficEngine.rs:26-71`; `LERelayClient.rs:128-175`.
- **Dependencia legacy:** `AGENTS.md:3-8`; `scripts/Build-UnifiedInstaller.ps1:22-35`; `src/NOVORA/VisionEngine/Server/VEServerManager.cs:13-14,63-98`.

## Controles y verificaciones favorables

- El control LAN usa emparejamiento/token y TLS con huella fijada; Windows guarda credenciales con DPAPI y Android usa AndroidKeyStore AES-GCM. Evidencia: `src/NOVORA/Control/NLControlTrustServer.cs:97-150`, `NLControlClient.cs:23-41,74-91`, `NLControlTrustStore.cs:23-55`, `src/NOVORA.Android/AndroidStorage/NLAndroidStorageTrustedPcs.cs:14-49`.
- El renderer VE limita su cola a dos fotogramas y conserva el más reciente. La grabación usa una cola acotada. Evidencia: `src/NOVORA/VisionEngine/Renderer/VERendererManager.cs:21-35,321-385`, `VERendererQueue.cs:26-58`, `src/NOVORA/VisionEngine/Video/VEMediaRecorder.cs:57-68`.
- El APK local y `NLAndroidRelease.json` coinciden por SHA-256; versión declarada `1.4.86`, código `86`, consistentes con `NLProjectAndroid.csproj:10-11`. Esto valida la copia local, no la instalación del teléfono ni los activos remotos.
- El binario canónico `Tools\LinkEngine\LENetworkRelay.exe` y la salida actual de Cargo tienen el mismo SHA-256 y tamaño. No había proceso `LENetworkRelay` activo que permitiera confirmar su ruta efectiva en ejecución.

## Pruebas ejecutadas

| Comprobación | Resultado | Qué demuestra |
| --- | --- | --- |
| `cargo test --release --offline` en RelayCore | 38 pasadas, 1 ignorada | Lógica cubierta por las pruebas Rust existentes. No cubre la trama IPv4 malformada ni la calidad física del túnel. |
| `dotnet run` Release de `NOVORA.LinkEngine.Tests` con `--no-restore` | PASS | Contratos de ADB simulado, limpieza de reverse y casos específicos de LE. Aviso `NU1900`: sin metadatos de vulnerabilidades NuGet por falta de acceso al índice. |
| `dotnet run` Release de `NOVORA.ExInHid.Tests` con `--no-restore` | PASS | Serialización y límites del protocolo HID. No valida un mando físico ni entradas recibidas en Android. |
| `dotnet run` Release de `NOVORA.DynamicAddress.Tests` con `--no-restore` | PASS | Identidad LAN y destino ADB variable según el ejecutable de pruebas. |
| `dotnet build` Android Release con `--no-restore` | No completó: `MSB4216`/`MSB4027` en host MSBuild/ILLink | El entorno impidió validar el APK desde esta sesión; no se atribuye el fallo al código Android. |
| `dotnet build` Android Debug con `--no-restore` | Falló sin diagnóstico de compilador | No aporta una confirmación adicional del código. |
| `adb devices -l` con ADB incluido | No inició el daemon | No hubo prueba de dispositivo ni de túnel/VPN/VE/ExIn activos. |

## Plan de corrección y validación

1. Validar longitud total, IHL y tamaño del encabezado antes de construir un paquete IPv4; convertir entradas inválidas en cierre del cliente, sin terminar RelayCore. Añadir pruebas negativas de encabezados truncados.
2. Validar el formato hexadecimal de `NLControlLinkHello.Token` antes de convertirlo; hacer que un saludo inválido cierre sólo esa conexión y comprobar que la siguiente conexión válida funciona.
3. Tratar `JsonException` y `FormatException` de FILES como rechazo de la transferencia, limpiar el archivo parcial y definir recuperación del canal sin reiniciar VE.
4. Medir en teléfono físico por separado: continuidad del túnel Android-PC, Internet por VPN, latencia bajo carga, pérdida, caudal, video y entradas ExIn. Registrar versión instalada, transporte, permisos, ruta/hash del relay en ejecución y ventanas de tiempo comparables.
5. Validar en un teléfono físico la captura USB y LAN de App Control y revisar el contenido del nuevo instalador.
6. Repetir compilación Android Release en un entorno MSBuild funcional y comprobar instalación/arranque de la APK empaquetada. Contrastar el instalador local con los activos publicados por separado.

## Límites de esta auditoría

No se modificó código de producción. No se ejecutaron cargas destructivas contra un relay o gateway activo. No se probaron PC y Android conectados, ni se pudo consultar el estado de dependencias NuGet por red. La revisión de seguridad es parcial y sus hallazgos se documentan por separado en el reporte de Codex Security.
