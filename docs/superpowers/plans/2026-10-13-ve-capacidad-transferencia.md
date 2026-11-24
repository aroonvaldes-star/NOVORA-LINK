# VisionEngine Data Capacity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Proteger control/audio/video y aumentar la transferencia útil al reducir la demanda de captura y el coste de FILES.

**Architecture:** Medir por etapa; compartir budget solo con rutas LE coincidentes. Añadir bitrate vivo y pacing previo al encoder. FILES v2 binario y ventana pequeña conviven con v1; ambos sentidos se aíslan del control solo cuando negocian v2.

**Tech Stack:** .NET/WPF, Android MediaCodec/MediaProjection/EGL/SAF, TCP/TLS, SHA-256, TimeProvider y suites de consola.

**Spec:** [Diseño común](../specs/2026-10-13-le-ve-estabilidad-red-design.md).

Base de rutas: E:/NOVORA/NOVORA LINK/GITHUB/NL. Dependencia común: LE T1 y T2. Las tareas de métricas VE pueden revisarse en paralelo; cualquier cambio en NLControlProtocol/contratos/Android csproj se integra de forma secuencial.

## Global Constraints

- CONTROL Version=1, codec/framing multimedia y v1 FILES no cambian por defecto.
- Latencia estable tiene prioridad; reducir bulk antes de calidad; no modificar ajustes elegidos de forma permanente.
- FILES es íntegro: máximo 2 GiB, SHA-256/offsets/commit; no expulsar bloques para acelerar video.
- No reconstruir MediaProjection ni pedir permiso en cada ajuste de bitrate.
- VEObservationWindow permanece de solo lectura; controller adaptativo independiente.
- Mantener canales separados y medir por fuente; memoria de colas limitada en bytes/tiempo.
- Cambios de codec y resolución deben respetar grabación, privacidad y clientes antiguos.

## Review Focus

- MediaCodec acepta llamada pero ignora bitrate: no declarar aplicado confirmado.
- Cambio de sesión/rotación/stop con orden en vuelo: ACK viejo y permiso anterior no reutilizados.
- FILES a SAF lento o sin permiso: crédito/backpressure y limpieza sin caída del video.
- Dos Begin opuestos: un solo propietario del canal, sin lectores concurrentes.
- Grabador retiene buffers y cambia configuración: ownership correcto y cierre visible sin corrupción.

## Tarea VE T1 Medidas por etapa y capacidad negociada

**Files:**
- Create: src/NOVORA/VisionEngine/Metrics/VEStageMetrics.cs y VETransportTelemetry.cs; src/NOVORA.Android/AndroidVideo/NLAndroidVideoQueueMetrics.cs.
- Modify: VEMetricsCollector.cs y VEMetricsTransport.cs bajo VisionEngine/Metrics; VisionEngine/Video/VEVideoManager.cs; Renderer/VERendererMetrics.cs.
- Modify: src/NOVORA.Android/AndroidVideo/NLAndroidVideoEncoder.cs y NLAndroidVideoService.cs; AndroidService/NLAndroidServiceVeLanCapture.cs; AndroidStorage/NLAndroidLanFileReceiver.cs.
- Test: tests/NOVORA.LinkEngine.Tests/Program.cs; nueva suite pura tests/NOVORA.VisionEngine.Network.Tests/NOVORA.VisionEngine.Network.Tests.csproj y Program.cs.

**Interfaces:** VEStageSample(long Generation,double WindowSeconds,double ReadWaitP95Ms,double DecodeP95Ms,double RenderP95Ms,double WriteWaitP95Ms,double QueueAgeP95Ms,long QueueBytes,long UsefulBytes,long ProtocolBytes,long Drops); VEStageMetrics.Observe(VEStageSample,long nowTicks). Comparte NLNetworkSample del diseño; serializado transmite edad/ventana, no ticks entre máquinas.

- [ ] Escribir StageDiagnosisSeparatesNetworkDecodeStorage, CountersUseDeltas y StaleGenerationIgnored. Un drop de renderer aislado no marca pérdida de red; decode error histórico sin delta no mantiene congestión.
- [ ] Medir read/write waits, duración decode, edad desde enqueue y backlog en bytes, progreso/RTT de ACK FILES, tiempo de escritura SAF y eficiencia payload/protocolo con reloj local.
- [ ] Separar source FPS, compositor FPS, encoder queue drops y render drops. Mantener límite renderer=2; no agrandarlo como optimización.
- [ ] Anunciar LiveVideoBitrate/CaptureFramePacing/LiveGeometry/FilesWireVersions opcionales por la negociación de LE T1, distintos de confirmación de ejecución.
- [ ] Ejecutar dotnet run --project tests/NOVORA.VisionEngine.Network.Tests/NOVORA.VisionEngine.Network.Tests.csproj -c Release con pure types enlazados y TimeProvider de prueba; integrar suite en verifier. Esperado: todas las nuevas aserciones pasan tras el fallo inicial.

## Tarea VE T2 Pacing FILES v1 y presupuesto de la ruta

**Files:**
- Modify: src/NOVORA/VisionEngine/Exchange/VEExchangeLanFileSender.cs, src/NOVORA/Control/NLControlFileTransfer.cs, VisionEngine/Exchange/VEExchangeFile.cs.
- Modify: src/NOVORA.Android/AndroidUI/NLAndroidUIFilesActivity.cs y AndroidUI/NLAndroidUIShareActivity.cs; NLControlNetworkPolicy.cs y Android csproj solo si requiere enlace de pure helpers.
- Create: src/NOVORA/VisionEngine/Exchange/VEExchangeTrafficBudget.cs.
- Test: tests/NOVORA.LinkEngine.Tests/Program.cs y VisionEngine.Network.Tests/Program.cs.

**Interfaces:** VEExchangeTrafficBudget.WaitAsync(NLNetworkGrant,int protocolBytes,CancellationToken); usa NLNetworkPacer de LE T1. Reserva longitud serializada de request completo antes de SendRequestAsync; contabiliza wire observable y reporta coste no observado aparte. No utiliza una cola que descarte elementos bulk.

- [ ] Escribir PacingDoesNotStartResponseTimeout, CancelDuringBudgetWait, FilesDoNotStarveControl y UsbWifiBudgetsStaySeparate. Espera simulada de presupuesto 35 s seguida de reply inmediato no vence el timeout de request 30 s.
- [ ] Integrar waits cancelables antes de iniciar deadline de respuesta, fuera de locks de video/control. Cancel y cierre no esperan tokens bulk. Si una trama comenzó y se cancela, conservar cierre de canal por encuadre incierto.
- [ ] Aplicar remanente de budget de DeviceLink, con FIFO y progreso visible; no limitar automáticamente la WAN por los bytes FILES locales. En v1 Android→PC sigue el control actual y pacing antes de enviar cada chunk, sin agregar lectores concurrentes.
- [ ] Pasar las regresiones ya existentes de cancelación, dispose con cola, timeout, vacío/multibloque y SHA. Comprobar que un fallo FILES no termina video/control vivos.

## Tarea VE T3 Bitrate vivo y FPS antes del encoder

**Files:**
- Create: src/NOVORA/VisionEngine/Performance/VEAdaptiveCaptureController.cs; src/NOVORA/Control/NLControlCapturePolicy.cs.
- Modify: src/NOVORA/Control/NLControlProtocol.cs, src/NOVORA/VisionEngine/Control/VEControlType.cs, src/NOVORA/VisionEngine/Control/VEControlMessage.cs, src/NOVORA/VisionEngine/Control/VEControlRouter.cs y src/NOVORA/VisionEngine/Control/VEControlManager.cs.
- Modify: src/NOVORA.Android/AndroidVideo/NLAndroidVideoEncoder.cs, NLAndroidVideoCompositor.cs y NLAndroidVideoService.cs; AndroidService/NLAndroidServiceVeLanCapture.cs.
- Modify: src/NOVORA/UI/NLUIWindowMainAndroidVideoSettings.cs y NLUIWindowMainAndroidEngines.cs.
- Test: VisionEngine.Network.Tests y tests/NOVORA.VisionEngine.Startup.Tests/Program.cs con fake encoder adapter; enlace de pure capture policy.

**Interfaces:** NLControlCapturePolicy(long Generation,long Sequence,int TargetBitrate,int TargetFps,int? MaxSize=null); NLControlCapturePolicyAck(long Generation,long Sequence,bool Accepted,int RequestedBitrate,int? ObservedBitrate,string? RejectedReason=null). Comando input nuevo Type=23 ApplyCapturePolicy; respuestas Type=3 CapturePolicyAck y Type=4 NetworkEcho; conservar valores 0–22 y respuestas clipboard 1/2. Payload opcional CapturePolicy al final del comando/ACK al final de respuesta. Type=24 NetworkEcho solo negociado, eco Sequence y reloj del emisor. Estos valores se añaden después de comprobar que siguen libres al implementar.

- [ ] Escribir OldPeerGetsNoNewCommand, ManagerSerializesCapturePolicyAndDispatchesAck, StaleAckIgnored, RuntimeBitrateNoProjectionRestart, IgnoredCodecNotConfirmed, KeyframeRequestsCoalesced y FramePacingConsumesLatestTexture; roundtrip por el manager/reader de producción, además del controlador puro.
- [ ] Extender VEControlManager.ToNativeCommandVE para copiar CapturePolicy y su RunNativeReaderVE para despachar ACK3/eco4 por el único lector existente; conservar clipboard1/2. Añadir dispatcher de comandos QoS antes de accesibilidad en LAN y USB; ResetVideo=17 solicita keyframe en ambos, sin alterar su valor. No enviar Type23/24 a peers sin capacidad; un solo request de capture policy pendiente, coalescer el siguiente.
- [ ] Implementar encoder.TrySetTargetBitrate(int bps) con un ejecutor serializado y MediaCodec.SetParameters(video-bitrate). ACK Accepted significa aceptación de la orden, no prueba de bitrate efectivo; ObservedBitrate procede de ventanas producidas y puede ser null. Si falla, mantener perfil vivo y reducir FILES; no usar reinicio UI como fallback automático.
- [ ] El controlador usa VideoRateFactor de LE T4 y señales propias VE. Baja 15 % después de presión persistente y cooldown 2 s; recupera 5 % tras 10 s estable, cada 5 s. No supera bitrate/fps configurados. El piso debe pertenecer a límites negociados, con mínimo inicial 250 kbps y máximo 100 Mbps; no ampliar rangos efectivos del codec/dispositivo.
- [ ] Pacing del compositor con TimeProvider local: actualizar textura pero presentar solo los frames debidos. Escalones 60→45→30 únicamente si negociados y sin elevar un target menor del usuario. Coalescer request-sync a uno pendiente con separación mínima 1 s y medir llegada IDR.
- [ ] Conservar audio/control/VirtualDisplay y continuidad PTS. Retirar pending policy al cambiar sesión/ruta. Mantener VEObservationWindow intacto; el controlador no termina al concluir el monitor de 20 minutos.
- [ ] Pasar suite pura/startup y compilar Android; confirmar con dispositivo real que cambia tasa sin pedir otro permiso. Si se graba, bitrate/FPS solo continúan cuando codec config permanece compatible; ante cambio de configuración finalizar grabación limpiamente y avisar.

## Tarea VE T4 FILES v2 binario y ventana acotada

**Files:**
- Create: src/NOVORA/Control/NLControlFileProtocolV2.cs; src/NOVORA/VisionEngine/Exchange/VEExchangeLanFileSenderV2.cs.
- Create: src/NOVORA.Android/AndroidStorage/NLAndroidLanFileReceiverV2.cs.
- Modify: src/NOVORA/Control/NLControlVeLanProtocol.cs; VisionEngine/Transport/VETransportLanSession.cs; AndroidVideo/NLAndroidVideoLanClient.cs; AndroidService/NLAndroidServiceVeLanCapture.cs; Android csproj.
- Test: nueva tests/NOVORA.FileProtocol.Tests/NOVORA.FileProtocol.Tests.csproj y Program.cs, net8.0 consola enlazando contrato/pure codec; tests/NOVORA.LinkEngine.Tests/Program.cs para streams reales.

**Interfaces:** NLControlFileProtocolV2.ReadFrameAsync(Stream,CancellationToken) y WriteFrameAsync(Stream,NLFileFrame,CancellationToken); NLFileFrame(NLFileKind Kind,Guid TransferId,ulong Offset,ReadOnlyMemory<byte> Payload). Header/limits/kinds exactos en diseño. SenderV2.SendAsync(Stream source,string name,long length,Stream channel,NLNetworkGrant grant,CancellationToken). Receptor SAF separa parse/ventana de la abstracción de almacenamiento.

- [ ] Escribir V2HeaderRoundTripAndFragmentedReads, LengthRejectedBeforeAllocation, V1Fallback, CreditBounds, CommitOnlyAfterHash, EmptyFileBeginAndCompleteTimeout, CreditZeroWatchdog, SafPauseSixtySecondsWithPendingData, CancelWindowInFlight y SourceLengthChanged; pruebas límite 2 GiB sin crear archivos grandes mediante streams sintéticos.
- [ ] Observar fallo e implementar header 36 bytes, Data≤65 536 B, metadata≤8192 B, UUID RFC4122 y enteros big-endian. Desconocidos/reservados/offset desordenado/overflow causan cierre FILES controlado, sin leer asignaciones arbitrarias.
- [ ] Negociar FilesWireVersions bilateralmente, escoger wire2 en oferta solo tras anuncio de ambos. Autenticar hello JSON antes de binario; peers v1 conservan hex/ACK y MaxFrame. Sin fallback dentro de un canal ambiguo ya iniciado.
- [ ] Implementar ventana inicial 256 KiB y techo 1 MiB, ACK acumulativo tras escritura contigua, crédito explícito. No contar un ACK como commit ni durabilidad; hash incremental una vez por byte y commit final tras cierre/flush y resultado seguro del provider.
- [ ] Crear deadlines de 30 s para respuesta a Begin, Complete tras Commit y Cancelled tras Cancel, además del progreso Data. Crédito0/pausa declarada exige Ack vivo cada 2 s; watchdog 6 s y máximo de pausa 120 s. Espera voluntaria del pacer queda fuera del deadline. Cancel/respuestas usan reserva independiente. Esperar Ack de todos los Data antes de Commit y declarar éxito exclusivamente al recibir Complete con id/longitud/hash coincidentes. Al cerrar, detener producers/readers, liberar owners/buffers y limpiar parcial; un error de limpieza se informa, nunca éxito.
- [ ] Suspender y reiniciar el reloj Data durante pausa declarada por Ack válido de crédito0; mantener watchdog6s/máximo120s y deadlines de Begin/Complete/Cancelled. Separar reader de frames, writer SAF serial y un único writer de respuestas, con cola Data≤crédito y≤1MiB. Cancel/keepalive deben seguir funcionando durante un write SAF bloqueado; no devolver crédito antes de liberar el bloque escrito ni publicar commit después de cierre/generación.
- [ ] Pasar tests empty/multiblock, corrupt hash, ACK excesivo/duplicado, disconnection, receiver lento, diskfull/permission revoked mediante storage fake y SHA en loopback. Demostrar cota bytes en vuelo y memory ownership.

## Tarea VE T5 FILES bidireccional y aislamiento del control

**Files:**
- Create: src/NOVORA/Control/NLControlFileTransferLease.cs; src/NOVORA/VisionEngine/Exchange/VEExchangeLanFileReceiverV2.cs; src/NOVORA.Android/AndroidStorage/NLAndroidLanFileSenderV2.cs.
- Modify: src/NOVORA.Android/AndroidUI/NLAndroidUIFilesActivity.cs y src/NOVORA.Android/AndroidUI/NLAndroidUIShareActivity.cs; src/NOVORA/UI/NLUIWindowMainAndroidControl.cs y src/NOVORA/VisionEngine/Exchange/VEExchangeFile.cs; src/NOVORA/Control/NLControlProtocol.cs y src/NOVORA/Control/NLControlCommands.cs.
- Test: FileProtocol.Tests, LinkEngine.Tests y fixtures peer antiguo.

**Interfaces:** PC arbitra solicitudes files.acquire/idempotentes por id/generación y concede NLFileLease(Guid TransferId,long Generation,NLNetworkDirection Direction). Un único multiplexer/reader por canal enruta frames a sender/receiver actual; la lease se libera por Complete/Error terminal/Cancelled confirmado o cierre de generación, nunca al enviar Commit/Cancel. Solicitudes pendientes FIFO; no preemptar activa.

- [ ] Escribir SimultaneousBeginsOneOwner, OneReaderPerChannel, LeaseHeldUntilComplete, LeaseReleasedOnlyOnCancelled, PendingRequestCancelledOnGeneration y OldAndroidUsesControlFiles. Simular flush/rename lento con una segunda solicitud pendiente: no obtiene propiedad antes de Complete/Error terminal.
- [ ] Implementar envío Android→PC por FILES v2 concedido, manteniendo selección SAF. Los clientes antiguos siguen file.* del control. No anunciar ambos sentidos hasta que existan los dos endpoints y el arbitraje.
- [ ] Mantener progreso, organización de carpetas, SHA/cotas y privacidad en ambos sentidos. La interfaz informa Pausada por estabilidad sin retener input o heartbeat.
- [ ] Comparar control p95 bajo transferencia en cada sentido y controlar bytes en memoria. Extender verifier con FileProtocol.Tests y todas las suites anteriores.

## Tarea VE T6 Geometría y optimizaciones condicionadas al perfil

**Files:**
- Modify: NLAndroidServiceVeLanCapture.cs, NLAndroidVideoService.cs, NLAndroidVideoCompositor.cs; VEAdaptiveCaptureController.cs, VEVideoManager.cs, VEProtocolReader.cs, VEMediaRecorder.cs.
- Test: VisionEngine.Network.Tests, VisionEngine.Startup.Tests y harness físico.

**Interfaces:** Consume MaxSize opcional de capture policy y LiveGeometry negociado. Nuevo estado prepare/apply/rollback conserva una MediaProjection/VirtualDisplay viva; canal USB y LAN usan misma semántica, no el teardown frecuente de feed. Parámetros adaptativos elegidos en intersección de límites negociados (no inferidos de LAN positivo solamente).

- [ ] Escribir OneVirtualDisplayPerConsent, RotationDuringAdaptation, InvalidGeometryKeepsOldProfile y RecordingEndsCleanlyOnConfigChange. Una orden antigua tras onStop no recrea captura.
- [ ] Validar dimensiones/capacidad antes de reemplazar encoder/surface; usar resize/setSurface del VirtualDisplay existente y rollback seguro si falla. Publicar Session→CodecConfig→Keyframe en orden, PTS monotónico, sin cerrar audio/control.
- [ ] Limitar cambios de geometría a 10 s y recuperación tras 30 s sanos. No iniciar cambio si no hay capacidad o si el usuario canceló captura; requerir permiso nuevo solo para una sesión nueva.
- [ ] Medir audio PCM (1,536 Mbps útiles a 48kHz/16bit estéreo), CPU/GC y read/decode waits. Crear experimentos separados para codec audio negociado y pooling solo si consumen parte material del cuello.
- [ ] Para pools, probar ownership con grabador/observer reteniendo referencias, cancelación y doble release; no ampliar límites multimedia 64 MiB ni renderer2. Para separación read/decode, cola acotada en bytes/tiempo y dependencia de frames comprimidos preservada.
- [ ] Decidir mantener o descartar cada experimento con baseline reproducible, sin atribuir NVDEC/NVENC/CUDA activo por detección de GPU.

## Tarea VE T7 Validación y presentación

**Files:**
- Modify: NLUIWindowMainAndroidEngines.cs, NLUIWindowMainAndroidVideoSettings.cs, AndroidUI/NLAndroidUIActivity.cs; scripts/Verify-NOVORA-Repository.ps1 y documentación de entrega.
- Test: suites existentes/nuevas y matriz del plan general.

- [ ] Mostrar bitrate configurado/solicitado/observado, motivo de adaptación, pausa FILES y salud por etapa. Mantener Principal/Configuración y no añadir ajustes ambiguos a ExIn.
- [ ] Ejecutar dotnet run de NetworkPolicy.Tests, VisionEngine.Network.Tests, FileProtocol.Tests y suites actuales; cargo test --locked y verifier integrado. Cero errores requeridos para candidato, sin convertirlos en prueba física.
- [ ] Completar comparación v1/v2 con RTT artificial 0/20/50 ms, archivos vacío, 64KiB±1 y multibloque; 3 repeticiones de goodput, CPU y memoria con presupuesto igual. V2 debe mejorar goodput cargado bajo RTT20/50 sin perder integridad ni empeorar controles.
- [ ] Verificar dispositivo USB/LAN, encoder que ignora bitrate, SAF real, audio/control/privacidad y grabación antes de paquete; registrar ruta/hash de todos los binarios usados.
- [ ] Preparar futura entrega con n+1 y firma original solo después del gate, sincronizar APK/JSON/Windows/relay/instalador/hashes y documentar rollback a modo Manual/Observe o FILES v1 por una sesión nueva.
- [ ] Revisar y versionar únicamente archivos propios de cada tarea, conservando cambios preexistentes; añadir Bitacora con pruebas, limitaciones y artefactos.
