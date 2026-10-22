# Definiciones incorporadas desde el entorno experimental — 2026-10-15

Inventario de tipos públicos e internos declarados en los archivos nuevos (sin rastrear) incorporados a `GITHUB\NL` desde `Laboratorio\NL`. Se agrupa por área. Cada fila indica archivo, línea y declaración. No sustituye al código: las firmas completas están en los archivos indicados.

## Verificación de compilación y pruebas

- Build `src/NOVORA/NLProjectDesktop.csproj -c Release`: **0 errores, 0 advertencias**.
- Usings y llamadas: el build confirma que todas las llamadas de la UI experimental (`NLUIWindowMainExperimentalNetwork.cs`) resuelven contra `LENetworkRelay`, `VEControlManager` y el XAML de la ventana principal, tras aplicar la base del entorno experimental.
- Cambio de referencia: `tests/NOVORA.VisionEngine.Optimization.Tests/NOVORA.VisionEngine.Optimization.Tests.csproj` referenciaba una DLL de Control fuera del repositorio (`../../../Control/Revision-Laboratorio-20261014/...`). Pasa a `ProjectReference` al proyecto de escritorio, igual que el resto de suites. Corrige el error CS0246 `NOVORA` al compilar.

| Suite | Resultado |
| --- | --- |
| AndroidVideoEncoder.Tests | 25 PASS / 0 FAIL |
| CaptureControl.Tests | PASS |
| CapturePolicy.Tests | 16/16 |
| DynamicAddress.Tests | PASS |
| ExInHid.Tests | PASS (512 muestras) |
| ExInOutput.Tests | 3 PASS / 0 FAIL |
| ExInRuntime.Tests | 21 PASS / 0 FAIL |
| ExInSession.Tests | 7 PASS / 0 FAIL |
| FileProtocol.Tests | PASS |
| LinkEngine.Tests | PASS |
| NetworkControlProbe.Tests | PASS |
| NetworkPolicy.Tests | 24/24 |
| NetworkSession.Tests | PASS |
| RelayPolicy.Tests | PASS (9 comprobaciones) |
| VisionEngine.Presentation.Tests | 21/21 |
| VisionEngine.Startup.Tests | PASS |
| VisionEngine.Optimization.Tests | 5 de 8 comprobaciones pasan; 3 fallan por entorno (ver abajo) |
| PhysicalRelayHarness | No ejecutado: requiere IP de PC, ruta de ADB y serial de un dispositivo real |

**Pendiente de entorno, no de código:** las tres comprobaciones fallidas de `VisionEngine.Optimization.Tests` (inicio NativeLan real) dicen que faltan `adb.exe`, `AdbWinApi.dll` y `AdbWinUsbApi.dll`. Los tres existen en `src/NOVORA/Tools/`, pero no se copian al directorio de salida de la suite. Requiere revisar cómo la suite localiza sus herramientas o copiar `Tools` al ejecutar.

## Android

| Archivo | Línea | Declaración |
| --- | --- | --- |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceControlNetwork.cs` | 6 | `public sealed partial class NLAndroidServiceControl` |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceFilesV2.cs` | 9 | `internal static class NLAndroidServiceFilesV2` |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceFilesV2.cs` | 11 | `internal sealed class Attachment(NLControlFileTransferV2 engine, long generation)` |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceFilesV2.cs` | 62 | `public sealed partial class NLAndroidServiceControl` |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceFilesV2.cs` | 66 | `internal sealed record FileAllocation(long Revision, long Generation, string LinkKey, NLNetworkDirection Direction,` |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceFilesV2.cs` | 165 | `internal sealed class AndroidFilesBudget` |
| `src/NOVORA.Android/AndroidService/NLAndroidServiceLanCodeBootstrap.cs` | 5 | `internal static class NLAndroidServiceLanCodeBootstrap` |
| `src/NOVORA.Android/AndroidStorage/NLAndroidFileStorageV2.cs` | 9 | `internal sealed class NLAndroidFileStorageV2` |
| `src/NOVORA.Android/AndroidStorage/NLAndroidLanFileReceiver.cs` | 12 | `internal sealed class NLAndroidLanFileReceiver` |
| `src/NOVORA.Android/AndroidStorage/NLAndroidStorageNovoraFolder.cs` | 9 | `internal static class NLAndroidStorageNovoraFolder` |
| `src/NOVORA.Android/AndroidUI/NLAndroidUILanCodeActivity.cs` | 12 | `public sealed class NLAndroidUILanCodeActivity` |
| `src/NOVORA.Android/AndroidVideo/NLAndroidVideoEncoderConfiguration.cs` | 4 | `internal static class NLAndroidVideoEncoderConfiguration` |
| `src/NOVORA.Android/AndroidVideo/NLAndroidVideoPacketQueue.cs` | 7 | `internal sealed record NLAndroidVideoPacket(byte[] Payload, long? PresentationTimeUs, bool Configuration, bool KeyFrame,` |
| `src/NOVORA.Android/AndroidVideo/NLAndroidVideoPacketQueue.cs` | 16 | `internal sealed class NLAndroidVideoPacketQueue` |
| `src/NOVORA.Android/AndroidVideo/NLAndroidVideoProjectionRequest.cs` | 5 | `internal enum NLAndroidVideoProjectionResult` |
| `src/NOVORA.Android/AndroidVideo/NLAndroidVideoProjectionRequest.cs` | 7 | `internal sealed record NLAndroidVideoProjectionGrant<T>(T Data, long Generation, bool Lan) where T` |
| `src/NOVORA.Android/AndroidVideo/NLAndroidVideoProjectionRequest.cs` | 10 | `internal sealed class NLAndroidVideoProjectionRequest<T> where T` |

## Control

| Archivo | Línea | Declaración |
| --- | --- | --- |
| `src/NOVORA/Control/NLControlCapturePolicy.cs` | 3 | `public sealed record NLControlCapturePolicy(long Generation, long Sequence, int TargetBitrate,` |
| `src/NOVORA/Control/NLControlCapturePolicy.cs` | 5 | `public sealed record NLControlCapturePolicyAck(long Generation, long Sequence, bool Accepted,` |
| `src/NOVORA/Control/NLControlCapturePolicy.cs` | 7 | `public sealed record NLControlCaptureTelemetry(long Generation, long Sequence, double WindowSeconds,` |
| `src/NOVORA/Control/NLControlCapturePolicy.cs` | 12 | `public sealed class NLControlCaptureTelemetrySequencer` |
| `src/NOVORA/Control/NLControlCapturePolicy.cs` | 20 | `public sealed class NLControlCapturePolicySession` |
| `src/NOVORA/Control/NLControlCapturePolicy.cs` | 87 | `public sealed class NLControlCaptureFramePacer` |
| `src/NOVORA/Control/NLControlCapturePolicy.cs` | 135 | `public sealed class NLControlKeyframeGate(TimeProvider? clock = null)` |
| `src/NOVORA/Control/NLControlCapturePolicy.cs` | 159 | `public sealed class NLControlCaptureTelemetryWindow` |
| `src/NOVORA/Control/NLControlFileLeaseCoordinatorV2.cs` | 9 | `public sealed record NLFileLeaseRequestV2(Guid TransferId, long Generation, NLFileTransferDirectionV2 Direction);` |
| `src/NOVORA/Control/NLControlFileLeaseCoordinatorV2.cs` | 10 | `public sealed record NLFileLeaseGrantV2(bool Granted, Guid TransferId, long Generation, NLFileTransferDirectionV2 Direction);` |
| `src/NOVORA/Control/NLControlFileLeaseCoordinatorV2.cs` | 13 | `public sealed class NLControlFileLeaseCoordinatorV2` |
| `src/NOVORA/Control/NLControlFileProtocolV2.cs` | 11 | `public enum NLFileFrameKindV2` |
| `src/NOVORA/Control/NLControlFileProtocolV2.cs` | 16 | `public sealed record NLFileMetadataV2(string Name, long Length, string? RelativePath = null);` |
| `src/NOVORA/Control/NLControlFileProtocolV2.cs` | 17 | `public sealed record NLFileFrameV2(NLFileFrameKindV2 Kind, Guid TransferId, long Offset, byte[] Payload);` |
| `src/NOVORA/Control/NLControlFileProtocolV2.cs` | 20 | `public static class NLControlFileProtocolV2` |
| `src/NOVORA/Control/NLControlFileStorageV2.cs` | 10 | `public sealed class NLControlFileStorageV2` |
| `src/NOVORA/Control/NLControlFileTransferLease.cs` | 9 | `public enum NLFileTransferDirectionV2` |
| `src/NOVORA/Control/NLControlFileTransferLease.cs` | 10 | `public sealed record NLFileLease(Guid TransferId, long Generation, NLFileTransferDirectionV2 Direction);` |
| `src/NOVORA/Control/NLControlFileTransferLease.cs` | 14 | `public sealed class NLControlFileTransferLease` |
| `src/NOVORA/Control/NLControlFileTransferV2.cs` | 10 | `public sealed record NLFileTransferResultV2(Guid TransferId, long Length, byte[] Sha256, string? Destination = null);` |
| `src/NOVORA/Control/NLControlFileTransferV2.cs` | 13 | `public interface INLFileReceiveStorageV2` |
| `src/NOVORA/Control/NLControlFileTransferV2.cs` | 22 | `public interface INLFileReceiveTargetV2` |
| `src/NOVORA/Control/NLControlFileTransferV2.cs` | 31 | `public sealed class NLControlFileTransferV2` |
| `src/NOVORA/Control/NLControlLanCodeHandoff.cs` | 6 | `public sealed record NLControlLanCodeHandoff(` |
| `src/NOVORA/Control/NLControlNetworkAllocation.cs` | 4 | `public sealed record NLControlNetworkAllocation(long Revision, long Generation, string LinkKey,` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 5 | `public enum NLNetworkDomain` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 6 | `public enum NLNetworkDirection` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 7 | `public enum NLNetworkTrafficClass` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 8 | `public enum NLNetworkPolicyMode` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 9 | `public enum NLNetworkSampleSource` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 10 | `public sealed record NLNetworkPolicy(long Revision, long Generation, string LinkKey,` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 13 | `public sealed record NLNetworkSample(long Generation, string LinkKey, NLNetworkDomain Domain,` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 18 | `public sealed record NLNetworkGrant(long Revision, long Generation, string LinkKey,` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 21 | `public sealed record NLNetworkBaseline(long Generation, string LinkKey,` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 24 | `public sealed record NLControlNetworkCapabilities(int Version = 1,` |
| `src/NOVORA/Control/NLControlNetworkPolicy.cs` | 27 | `public sealed record NLNetworkDecision(NLNetworkPolicy AppliedPolicy,` |

## ExInEngine e Integración

| Archivo | Línea | Declaración |
| --- | --- | --- |
| `src/NOVORA/ExInEngine/ExInPaddleMappings.cs` | 3 | `public enum ExInPaddle` |
| `src/NOVORA/ExInEngine/ExInPaddleMappings.cs` | 12 | `public sealed record ExInPaddleMappings(` |
| `src/NOVORA/ExInEngine/ExInPointerSensitivity.cs` | 3 | `internal static class ExInPointerSensitivity` |
| `src/NOVORA/Integration/ExInConnectionSupervisor.cs` | 4 | `internal sealed class ExInConnectionSupervisor(` |
| `src/NOVORA/Integration/ExInHidOutput.cs` | 9 | `internal sealed class ExInHidOutput` |
| `src/NOVORA/Integration/ExInHidProtocol.cs` | 6 | `public static class ExInHidProtocol` |

## LinkEngine

| Archivo | Línea | Declaración |
| --- | --- | --- |
| `src/NOVORA/LinkEngine/Metrics/LESpeedTestServer.cs` | 9 | `public sealed record LESpeedTestResult(DateTimeOffset SampledAtUtc, string Transport,` |
| `src/NOVORA/LinkEngine/Metrics/LESpeedTestServer.cs` | 12 | `public sealed record LEInternetSpeedTestResult(DateTimeOffset SampledAtUtc, string Transport,` |
| `src/NOVORA/LinkEngine/Metrics/LESpeedTestServer.cs` | 18 | `public sealed class LESpeedTestServer` |
| `src/NOVORA/LinkEngine/Network/ILENetworkRelay.cs` | 7 | `internal interface ILENetworkRelay` |
| `src/NOVORA/LinkEngine/Network/LERelayPolicyProtocol.cs` | 7 | `public sealed record LERelayReady(int Version, bool Shaping, bool FairQueues, bool QueueTelemetry);` |
| `src/NOVORA/LinkEngine/Network/LERelayPolicyProtocol.cs` | 8 | `public sealed record LERelayPolicyAcknowledgement(long Revision, long Generation, bool Accepted, string? Reason, string Json);` |
| `src/NOVORA/LinkEngine/Network/LERelayPolicyProtocol.cs` | 9 | `public sealed record LERelayQosSnapshot(long Revision, long Generation, string LinkKey, long Sequence,` |
| `src/NOVORA/LinkEngine/Network/LERelayPolicyProtocol.cs` | 13 | `public static class LERelayPolicyProtocol` |
| `src/NOVORA/LinkEngine/Network/LERelayPolicySession.cs` | 8 | `public sealed class LERelayPolicySession` |
| `src/NOVORA/LinkEngine/Transport/ILETransportData.cs` | 8 | `internal interface ILETransportData` |

## Red

| Archivo | Línea | Declaración |
| --- | --- | --- |
| `src/NOVORA/Network/NLExperimentalInternetProbe.cs` | 13 | `public sealed class NLExperimentalInternetProbe` |
| `src/NOVORA/Network/NLExperimentalNetworkSession.cs` | 11 | `public sealed class NLExperimentalNetworkSession` |
| `src/NOVORA/Network/NLNetworkBudget.cs` | 9 | `public sealed class NLNetworkBudget` |
| `src/NOVORA/Network/NLNetworkBudget.cs` | 109 | `internal static class NLNetworkTime` |
| `src/NOVORA/Network/NLNetworkControlProbe.cs` | 6 | `public sealed class NLNetworkControlProbe` |
| `src/NOVORA/Network/NLNetworkPacer.cs` | 10 | `public sealed class NLNetworkPacer` |
| `src/NOVORA/Network/NLNetworkSharedLinkBudget.cs` | 5 | `public sealed record NLNetworkSharedLinkAllocation(long VeFromAndroid,long VeToAndroid,long RelayFromAndroid,long RelayToAndroid);` |
| `src/NOVORA/Network/NLNetworkSharedLinkBudget.cs` | 7 | `public static class NLNetworkSharedLinkBudget` |
| `src/NOVORA/Network/NLNetworkStabilityController.cs` | 9 | `public sealed class NLNetworkStabilityController` |

## UI

| Archivo | Línea | Declaración |
| --- | --- | --- |
| `src/NOVORA/UI/NLAndroidInstallDevicePolicy.cs` | 3 | `internal static class NLAndroidInstallDevicePolicy` |
| `src/NOVORA/UI/NLUIWindowMainExInRoute.cs` | 3 | `public partial class NLUIWindowMain` |
| `src/NOVORA/UI/NLUIWindowMainExperimentalFiles.cs` | 8 | `public partial class NLUIWindowMain` |
| `src/NOVORA/UI/NLUIWindowMainExperimentalNetwork.cs` | 11 | `public partial class NLUIWindowMain` |
| `src/NOVORA/UI/NLUIWindowMainLinkSpeedTest.cs` | 11 | `public partial class NLUIWindowMain` |

## VisionEngine

| Archivo | Línea | Declaración |
| --- | --- | --- |
| `src/NOVORA/VisionEngine/Exchange/VEExchangeLanFileSender.cs` | 7 | `internal sealed class VEExchangeLanFileSender` |
| `src/NOVORA/VisionEngine/Exchange/VEExchangeLanFileV2.cs` | 7 | `internal sealed class VEExchangeLanFileV2` |
| `src/NOVORA/VisionEngine/Metrics/VEObservationWindow.cs` | 7 | `internal sealed class VEObservationWindow` |
| `src/NOVORA/VisionEngine/Optimizacion/VEDynamicLatencyOptimizer.cs` | 12 | `public sealed class VEDynamicLatencyOptimizer` |
| `src/NOVORA/VisionEngine/Optimizacion/VEDynamicLatencyOptimizer.cs` | 80 | `public sealed record VELatencyOptimizationSnapshot(` |
| `src/NOVORA/VisionEngine/Renderer/VERendererPresentationTiming.cs` | 9 | `public sealed class VERendererPresentationTiming` |

## Pruebas

| Archivo | Línea | Declaración |
| --- | --- | --- |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 5 | `public class IllegalArgumentException(string message = "Unsupported format")` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 6 | `public class UnsupportedOperationException(string message = "Unsupported option")` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 10 | `public sealed class Surface` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 18 | `public sealed class Bundle` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 24 | `public static class Build` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 26 | `public static class VERSION` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 31 | `public static class Log` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 40 | `public enum MediaCodecConfigFlags` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 41 | `public enum BitrateMode` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 42 | `public enum MediaCodecErrorCode` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 43 | `public sealed class MediaFormat` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 59 | `public sealed class MediaCodecInfo` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 63 | `public sealed class CodecCapabilities` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 69 | `public sealed class EncoderCapabilities` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 74 | `public sealed class ByteBuffer(byte[] bytes)` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 81 | `public sealed class MediaCodec` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 83 | `public abstract class Callback` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 90 | `public sealed class BufferInfo` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/AndroidBoundary.cs` | 97 | `public sealed class CodecException(string message, bool recoverable = false,` |
| `tests/NOVORA.AndroidVideoEncoder.Tests/PortableQueueTests.cs` | 5 | `internal static class PortableQueueTests` |
| `tests/NOVORA.ExInOutput.Tests/BoundaryFixtures.cs` | 2 | `public sealed class NLServiceNovoraPaths` |
| `tests/NOVORA.ExInOutput.Tests/BoundaryFixtures.cs` | 7 | `public sealed class NLServiceADB` |
| `tests/NOVORA.ExInRuntime.Tests/NativeBoundary.cs` | 7 | `public sealed class NLServiceNovoraPaths` |
| `tests/NOVORA.ExInRuntime.Tests/NativeBoundary.cs` | 17 | `public sealed class ExInSdl` |
| `tests/NOVORA.ExInRuntime.Tests/Program.cs` | 379 | `public sealed record Block(ushort Id)` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 5 | `public sealed class NLServiceNovoraPaths` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 6 | `public sealed record Device(string Serial, bool Connected, bool IsWifiConnection);` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 7 | `public sealed class NLServiceADB` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 25 | `public enum ExInInputMode` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 26 | `public sealed record ExInModeResult(bool Success, ExInInputMode Mode, int Devices, string Message, string? Code = null);` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 27 | `public sealed class ManagerFixture` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 28 | `public sealed class ExInCoreEngine` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 48 | `public sealed class ExInHidOutput` |
| `tests/NOVORA.ExInSession.Tests/BoundaryFixtures.cs` | 59 | `internal sealed class ControlledTime` |
| `tests/NOVORA.FileProtocol.Tests/TestSupport.cs` | 54 | `public sealed class Target(MemoryStorage owner, Guid id, long generation, NLFileMetadataV2 metadata)` |
| `tests/NOVORA.LinkEngine.Tests/Program.cs` | 16 | `internal static class LinkEngineTransportDataTests` |
| `tests/NOVORA.LinkEngine.Tests/VeStartupRegressionTests.cs` | 3 | `internal static class VeStartupRegressionTests` |
| `tests/NOVORA.NetworkPolicy.Tests/ManualClock.cs` | 1 | `internal sealed class ManualClock` |
| `tests/NOVORA.VisionEngine.Startup.Tests/Program.cs` | 21 | `public partial class NLUIWindowMain` |
| `tests/NOVORA.VisionEngine.Startup.Tests/ProjectionConsentTests.cs` | 4 | `internal static class ProjectionConsentTests` |

