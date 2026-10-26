# Exchange de VisionEngine independiente del transporte Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hacer que todas las operaciones Exchange estén disponibles cuando VisionEngine se inicia por una sola ruta, USB o Wi-Fi/LAN.

**Architecture:** La sesión VE activa poseerá un contexto tipado con transporte, generación, control y un adaptador Exchange. El adaptador USB encapsulará ADB y el adaptador NativeLan usará FILES bidireccional autenticado; la UI llamará un contrato común y no confundirá endpoint LAN con serial. Clipboard/texto seguirá usando el control de la sesión, disponible por ambos transportes.

**Tech Stack:** C#/.NET 8, WPF Windows, AppControl Android, canales VE USB y NativeLan, protocolo compartido `NLControlFileTransferV2` y harnesses de consola existentes.

**Spec:** `docs/superpowers/specs/2026-10-16-ve-exchange-usb-wifi-design.md`

## Global Constraints

- Cada función debe operar en una sesión iniciada solo por USB o solo por Wi-Fi/LAN; ninguna exige que el otro medio también esté conectado.
- No migrar ni reanudar una operación en curso al cambiar de transporte.
- No entregar `LAN:<host>` ni otro endpoint LAN a ADB como serial.
- LAN solo opera dentro de una sesión AppControl autorizada/autenticada y su canal FILES negociado. Antes de pruebas físicas LAN, comprobar que la depuración ADB por Wi-Fi esté activa y responda `device`; esa comprobación es preparación/diagnóstico y no es dependencia funcional del Exchange NativeLan.
- Los archivos recibidos quedan en `NOVORA-Files`; solo se confirman como completos tras validar tamaño y SHA-256.
- Cancelación o cierre de generación elimina parciales; no se marca como completado un elemento fallido.
- Transferir un APK e instalarlo son operaciones distintas; la instalación respeta la confirmación/permisos de Android.
- Mantener las reglas de privacidad VE para USB y LAN y no crear fallback silencioso entre transportes.

## Review Focus

- Una ruta NativeLan sintética llega a ADB como si fuera serial: probar la selección de transporte con `LAN:<host>` y demostrar que nunca se ejecuta ADB.
- Android no abre manualmente Archivos antes del inicio VE: probar que FILES v2 queda asociado a la sesión y puede iniciar envío/recepción sin esa preparación.
- Pérdida de sesión o respuesta FILES parcial: probar cancelación, timeout, limpieza y rechazo de callbacks de una generación vieja.
- Capacidad DragDrop desactivada mientras FileTransfer sigue activa: probar que otras operaciones de archivos y clipboard continúan habilitadas de forma independiente.
- APK íntegro pero instalación denegada o pendiente por Android: probar que la UI distingue transferencia completada de instalación confirmada.

---

## File Map

| Archivo | Responsabilidad prevista |
|---|---|
| `src/NOVORA/VisionEngine/Exchange/IVEExchangeTransport.cs` (nuevo) | Contrato común de Exchange para operaciones que usan un transporte VE, capacidades independientes y valor tipado de ruta. |
| `src/NOVORA/VisionEngine/Exchange/VEExchangeSession.cs` (nuevo) | Contexto y propiedad de la sesión Exchange: generación, control, transporte y cierre. |
| `src/NOVORA/VisionEngine/Exchange/VEExchangeRouter.cs` (nuevo) | Vincula, consulta y limpia la sesión vigente de Exchange por generación. |
| `src/NOVORA/VisionEngine/Exchange/VEExchangeUsbTransport.cs` (nuevo) | Adaptador USB que delega operaciones ADB existentes y reporta resultados con el contrato común. |
| `src/NOVORA/VisionEngine/Exchange/IVEExchangeUsbBackend.cs` (nuevo) | Puerto inyectable con `PushAsync(string serial, string localPath, CancellationToken)`, `PullAsync(string serial, string remotePath, string localPath, CancellationToken)` y `PullFolderAsync(string serial, string remotePath, string localPath, CancellationToken)` para probar USB sin teléfono. |
| `src/NOVORA/VisionEngine/Exchange/VEExchangeLanTransport.cs` (nuevo) | Adaptador NativeLan para FILES v2 bidireccional y estado/recepción de archivos. |
| `src/NOVORA/Integration/NLIntegrationRuntime.cs` | Quitar la construcción permanente ADB-only de Files/Images/Media/Transfer; conservar integraciones no ligadas al transporte y políticas. |
| `src/NOVORA/UI/NLUIWindowMainVisionEngine.cs` | Asociar operaciones de Exchange y recepción NOVORA a la sesión activa; eliminar uso ADB al recibir por LAN; mostrar resultados y generación. |
| `src/NOVORA/UI/NLUIWindowMainAndroidVideoSource.cs` | Crear/liberar el contexto USB junto con la generación AppControl USB. |
| `src/NOVORA/UI/NLUIWindowMainAndroidVideoLan.cs` | Crear/liberar el contexto NativeLan junto con sus streams y retirar la dependencia de FILES v1/preparación manual. |
| `src/NOVORA/UI/NLUIWindowMainExperimentalFiles.cs` | Revisar la integración FILES v2 existente para que no duplique la propiedad del adaptador ni desatienda las recepciones. |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceFilesV2.cs`, `src/NOVORA.Android/AndroidStorage/NLAndroidFileStorageV2.cs`, `src/NOVORA/Control/NLControlFileTransferV2.cs` | Completar negociación, permisos, confinamiento y ciclo bidireccional solo donde el contrato o las pruebas señalen un hueco. |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceControl.cs` y archivos Android asociados (nuevos si se requieren) | Publicar recepción/resultado de instalación de APK mediante el flujo de Android permitido, sin instalación silenciosa. |
| `tests/NOVORA.VisionEngine.Exchange.Tests/` (nuevo) | Harness aislado para selector de ruta, adaptadores fake, capacidades y generaciones. |
| `tests/NOVORA.LinkEngine.Tests/Program.cs`, `tests/NOVORA.FileProtocol.Tests/` | Casos de protocolo bidireccional, integrity, cancelación, timeout, directorios y limpieza de parciales. |
| `tests/NOVORA.VisionEngine.Startup.Tests/` | Comprobación de asociación y liberación del Exchange durante inicios USB y NativeLan. |

## Task 1: Definir el contrato Exchange y el contexto de sesión

**Files:**
- Create: `src/NOVORA/VisionEngine/Exchange/IVEExchangeTransport.cs`
- Create: `src/NOVORA/VisionEngine/Exchange/VEExchangeSession.cs`
- Create: `src/NOVORA/VisionEngine/Exchange/VEExchangeRouter.cs`
- Create: `tests/NOVORA.VisionEngine.Exchange.Tests/NOVORA.VisionEngine.Exchange.Tests.csproj`
- Create: `tests/NOVORA.VisionEngine.Exchange.Tests/Program.cs`

**Interfaces:**
- Produce `VEExchangeRoute` con valores `UsbAppControl` y `NativeLan`; `VEExchangeCapabilities` con propiedades `Clipboard`, `SendFiles`, `ReceiveFiles`, `ReceiveFolder`, `DragDrop` e `InstallApk`; y `IVEExchangeTransport` con `VEExchangeRoute Route`, `VEExchangeCapabilities Capabilities`, `Task<VEExchangeResult> SendPathsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)`, `Task<VEExchangeResult> PullFileAsync(string remotePath, CancellationToken cancellationToken)`, `Task<VEExchangeResult> PullNovoraFolderAsync(CancellationToken cancellationToken)`, y `Task<VEExchangeApkInstallResult> InstallApkAsync(string androidPath, CancellationToken cancellationToken)`, y `ValueTask DisposeAsync()`. `VEExchangeApkInstallResult` distingue `Confirmed`, `Denied`, `Pending` y `Failed` y contiene un mensaje visible para el usuario.
- Produce `VEExchangeSession(long generation, VEControlManager control, IVEExchangeTransport transport)`; expone `Generation`, `Control`, `Transport` y dispose idempotente. Produce `VEExchangeRouter.Bind(VEExchangeSession session)`, `TryGet(long generation, out VEExchangeSession? session)` y `ClearAsync(long generation)`; una generación anterior no puede reemplazar ni limpiar la sesión nueva.
- `VEExchangeCapabilities` expone booleans independientes para clipboard, send, receive, folder receive, drag/drop e instalación APK; no inferir unas de otras.

- [ ] **Step 1: Escribir casos de contrato fallidos** para adaptador fake: `Route_IsTyped`, `Capabilities_DoNotImplyEachOther`, `SessionDispose_IsIdempotent` y `Router_RejectsStaleGeneration`; afirmar ruta tipada, independencia de capacidades, cierre único y rechazo de operaciones obsoletas.
- [ ] **Step 2: Ejecutar el harness nuevo** con `dotnet run --project tests/NOVORA.VisionEngine.Exchange.Tests -c Release`; confirmar que fallan por contrato ausente.
- [ ] **Step 3: Implementar los tipos y el contexto** con la generación como identidad de vida y sin serial/host polimórfico.
- [ ] **Step 4: Ejecutar el harness** y exigir PASS en los casos de ruta, capacidades y cierre.
- [ ] **Step 5: Commit** `feat: define transport-aware VE Exchange session` con solo contrato, contexto y harness.

## Task 2: Adaptar las operaciones USB existentes

**Files:**
- Create: `src/NOVORA/VisionEngine/Exchange/VEExchangeUsbTransport.cs`
- Modify: `src/NOVORA/VisionEngine/Exchange/VEExchangeFile.cs`
- Modify: `src/NOVORA/VisionEngine/Exchange/VEExchangeTransfer.cs`
- Modify: `tests/NOVORA.VisionEngine.Exchange.Tests/Program.cs`

**Interfaces:** consume `IVEExchangeTransport`/`VEExchangeSession` de Task 1; implementa sus operaciones USB con serial validado del contexto USB.

- [ ] **Step 1: Añadir pruebas USB con ejecutor ADB falso** para `UsbPush_UsesCategorizedPath`, `UsbPull_CommitsIntoNovoraFiles`, `UsbFolderPull_PreservesRelativePaths`, `UsbOperations_PropagateCancellation` y `UsbBackend_RejectsLanEndpoint`; verificar ruta de destino `NOVORA-Files`, propagación de cancelación y que ningún endpoint LAN se acepta como serial.
- [ ] **Step 2: Ejecutar el harness Exchange** y confirmar que los casos nuevos fallan antes de cambiar el adaptador.
- [ ] **Step 3: Implementar `VEExchangeUsbTransport`** como adaptador del `VEExchangeFile` existente; separar el ejecutor ADB detrás de una interfaz inyectable para probar sin teléfono.
- [ ] **Step 4: Corregir gates de capacidad** de `VEExchangeTransfer` para que `FileTransfer` no dependa de `DragDrop`; conservar policy/privacy checks.
- [ ] **Step 5: Ejecutar el harness Exchange** y exigir PASS en operaciones, cancelación, privacidad y filtro de serial sintético.
- [ ] **Step 6: Commit** `feat: route VE Exchange operations over USB`.

## Task 3: Completar el adaptador LAN bidireccional sin preparación manual

**Files:**
- Create: `src/NOVORA/VisionEngine/Exchange/VEExchangeLanTransport.cs`
- Modify: `src/NOVORA/VisionEngine/Exchange/VEExchangeLanFileV2.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidVideoLan.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainExperimentalFiles.cs`
- Modify: `src/NOVORA.Android/AndroidService/NLAndroidServiceFilesV2.cs` y `src/NOVORA.Android/AndroidStorage/NLAndroidFileStorageV2.cs` solo para brechas demostradas
- Modify: `tests/NOVORA.LinkEngine.Tests/Program.cs` y `tests/NOVORA.FileProtocol.Tests/`

**Interfaces:** consume el contrato de Task 1; envuelve la sesión FILES v2 existente; reporta entradas recibidas como `VEExchangeResult` a la sesión propietaria.

- [ ] **Step 1: Añadir pruebas de protocolo** para `SendPaths_PreservesRelativeTree`, `Receive_CommitsOnlyAfterLengthAndSha256`, `Receive_RejectsEscapingPath`, `CancelledTransfer_RemovesPartial`, `TimedOutPeer_ClosesGeneration` y `OldGeneration_CannotPublishReceivedEvent`; cubrir PC→Android y Android→PC con archivo vacío, multibloque, nombre conflictivo, timeout y cierre.
- [ ] **Step 2: Ejecutar `dotnet run --project tests/NOVORA.LinkEngine.Tests -c Release` y `dotnet run --project tests/NOVORA.FileProtocol.Tests -c Release`**; observar los fallos nuevos de recepción/seguridad antes de corregirlos.
- [ ] **Step 3: Implementar el adaptador LAN** sobre el canal FILES v2 negociado; iniciar/adjuntar el canal en la sesión VE, sin esperar que el usuario abra Archivos o escoja `/sdcard/NOVORA`.
- [ ] **Step 4: Conectar el evento `Received` a la UI/runtime** con generación y resultado visibles; no considerar success hasta commit de longitud/hash.
- [ ] **Step 5: Ejecutar los dos harnesses** y exigir PASS en las direcciones, límites de ruta, autenticación, cancelación y parciales.
- [ ] **Step 6: Commit** `feat: route VE Exchange operations over LAN`.

## Task 4: Enlazar la sesión Exchange al inicio y cierre de VisionEngine

**Files:**
- Modify: `src/NOVORA/Integration/NLIntegrationRuntime.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidVideoSource.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidVideoLan.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainVisionEngine.cs`
- Modify: `tests/NOVORA.VisionEngine.Startup.Tests/Program.cs`
- Modify: `tests/NOVORA.VisionEngine.Exchange.Tests/Program.cs`

**Interfaces:** consume USB/LAN adapters de Tasks 2–3; el runtime expone la sesión Exchange vigente; los handlers resuelven operaciones mediante ella.

- [ ] **Step 1: Añadir comprobaciones de inicio/cierre** para USB AppControl y NativeLan: `StartUsb_BindsUsbAdapter`, `StartNativeLan_BindsLanAdapter`, `StopSession_DisposesAdapter` y `NewGeneration_RejectsOldCallbacks`; comprobar ruta/endpoint correctos, reemplazo de generación y rechazo de callbacks de la anterior.
- [ ] **Step 2: Ejecutar startup y Exchange harnesses**; confirmar fallo si `LAN:<host>` se trata como serial ADB o si un handler carece de sesión.
- [ ] **Step 3: Asociar cada adaptador** con la generación del correspondiente inicio VE; invalidarlo/cancelarlo al detener o reemplazar la fuente.
- [ ] **Step 4: Cambiar envío, recepción de carpeta NOVORA, drag/drop y pegar archivos** para que usen `IVEExchangeTransport`; eliminar el branching UI ADB-vs-LAN y el prefijo `LAN:` como selector.
- [ ] **Step 5: Ejecutar ambos harnesses** y build Release de `src/NOVORA/NLProjectDesktop.csproj`; exigir PASS/código 0 sin advertencias nuevas.
- [ ] **Step 6: Commit** `feat: bind VE Exchange to active source session`.

## Task 5: Unificar capacidades, portapapeles y flujo APK

**Files:**
- Modify: `src/NOVORA/Integration/NLIntegrationRuntime.cs`
- Modify: `src/NOVORA/VisionEngine/Exchange/VEExchangeClipboard.cs`, `VEExchangeApk.cs`, `VEExchangeImage.cs`, `VEExchangeMedia.cs`, `VEExchangePaste.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainVisionEngine.cs` y controles XAML asociados solo si se requiere reportar el estado
- Modify: `src/NOVORA.Android/AndroidService/NLAndroidServiceControl.cs`, manifiesto/actividad Android e instalador PC solamente para instalar un APK transferido mediante el flujo visible del sistema Android
- Modify: `tests/NOVORA.VisionEngine.Exchange.Tests/Program.cs`

**Interfaces:** consume la sesión de Task 4; clipboard opera sobre `VEControlManager` de la sesión activa; imágenes/medios conservan categorización vía `SendPathsAsync`; instalar APK es operación distinta de enviar.

- [ ] **Step 1: Añadir casos** para `Clipboard_UsesActiveSessionControl`, `Capabilities_DoNotCoupleDragDropAndTransfer`, `ImageMediaApk_KeepFileClassification`, `ApkTransfer_DoesNotMeanInstalled` e `InstallApk_ReturnsSystemOutcome`; comprobar estados separados de transferencia e instalación.
- [ ] **Step 2: Ejecutar Exchange harness** y confirmar fallos en gates mezclados y estado de instalación ambiguo.
- [ ] **Step 3: Usar el control de la sesión actual** para texto/clipboard; hacer que imagen/media/drag-drop/pegar consulten sus capacidades propias y el mismo enrutador de archivos.
- [ ] **Step 4: Implementar solo instalación APK interactiva** para APK ya transferido: usar PackageInstaller Android, reflejar permisos/confirmación y devolver éxito únicamente desde el resultado del sistema; no usar ruta ADB requerida por la sesión LAN.
- [ ] **Step 5: Ejecutar Exchange harness y build Android Release target `Compile`**; exigir PASS/código 0 y distinguir transferencia de instalación.
- [ ] **Step 6: Commit** `feat: unify VE Exchange capabilities and APK status`.

## Task 6: Validación de dos rutas y registro de evidencia

**Files:**
- Modify: `docs/informes/2026-10-16-ve-exchange-usb-wifi-validation.md`
- Modify: `E:\NOVORA\Bitacora.md`

- [ ] **Step 1: Ejecutar pruebas locales**: `dotnet run --project tests/NOVORA.VisionEngine.Exchange.Tests -c Release`, los harnesses LinkEngine/FileProtocol/Startup existentes y builds Windows y Android Release. Registrar salidas y alcance; no inferir sesión física.
- [ ] **Step 2: Validar el teléfono primero por USB solamente**: registrar versión/app y confirmar portapapeles, archivos/carpetas en ambas direcciones, imágenes, medios y APK transferido/instalación consentida.
- [ ] **Step 3: Validar el teléfono por Wi-Fi/LAN solamente**: comprobar la sesión AppControl LAN autorizada sin cable USB; verificar primero ADB por Wi-Fi como preparación y después desconectar su ruta ADB en la PC, confirmando que NativeLan Exchange sigue operando sin un serial ADB válido ni abrir Archivos.
- [ ] **Step 4: Probar cierres/cancelación** por cada ruta; registrar parciales, reintento en sesión nueva y resultados sin declarar éxito cuando haya rechazo.
- [ ] **Step 5: Escribir resultados observados** en el informe y añadir entrada por carpeta/proyecto en Bitácora; separar compilado, loopback, instalado y probado físicamente.
- [ ] **Step 6: Commit** `docs: record USB and LAN VE Exchange validation`.

## Orden y criterio de cierre

Las Tasks 1→2→3 establecen contratos y adapters; Task 4 integra la vida de sesión; Task 5 cierra funciones de usuario; Task 6 valida independientemente USB y Wi-Fi. No declarar terminado el objetivo hasta que ambas rutas completen la matriz física de la Task 6. Si no hay un teléfono disponible, reportar las comprobaciones locales y dejar la validación física pendiente, sin atribuirla al build o loopback.
