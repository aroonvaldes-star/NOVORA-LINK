# VE Native LAN Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manually selected, authenticated native LAN transport that sends the Android screen and audio to VisionEngine without USB or ADB while preserving the existing USB/ADB path.

**Architecture:** The existing TLS control session negotiates a short-lived `NLControlVeLanOffer`. NOVORA PC accepts video, audio, and control on three pinned-TLS, one-shot channels and passes their streams to the existing `StartAppControlAsync` VisionEngine pipeline; NOVORA-LINK reuses the Android MediaProjection/MediaCodec components to produce those streams. The Android selector chooses either the unchanged `startVideo` USB action or the new `startVideoLan` flow and never falls back automatically.

**Tech Stack:** C# 13, .NET 8 WPF, .NET 10 Android, `SslStream` TLS 1.2/1.3, Android MediaProjection/MediaCodec, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-04-ve-native-lan-transport-design.md`

## Global Constraints

- USB/ADB remains available and behaviorally unchanged.
- Native LAN must start without an ADB device and without a USB cable.
- The first LAN trust enrollment always uses NOVORA's six-digit code; remembered trust may reconnect later.
- Transport selection is manual and persistent; there is no automatic fallback or switching.
- AppControl's package, services, preferences, and identity are not modified.
- Every LAN media channel uses pinned TLS plus a distinct one-use 64-hex-character token.
- Offers accept only private IPv4 addresses, expire after 30 seconds, and are never persisted or logged.
- Losing the authorized control session closes all VE LAN listeners, sockets, and capture resources.

## Review Focus

- A phone changes Wi-Fi address after receiving an offer: reject the stale offer and clean both sides without selecting USB; covered by Task 6 lifecycle tests.
- A token is replayed or presented to the wrong channel: reject before exposing a stream to VisionEngine; covered by Task 2 authentication tests.
- Video succeeds while optional audio fails: report `Degraded` and keep video/control alive; covered by Tasks 3 and 6.
- The user changes the selector while VE is running: keep the current session and require Stop before the new selection can start; covered by Task 5 selector tests.
- A remembered PC certificate changes: reject pin validation and require a new six-digit enrollment rather than trusting the new certificate; covered by Task 4 TLS-client tests.

---

### Task 1: VE LAN protocol and manual-selection model

**Files:**
- Create: `src/NOVORA/Control/NLControlVeLanProtocol.cs`
- Create: `src/NOVORA.LinkClient/NovoraVeTransportSelection.cs`
- Create: `src/NOVORA.LinkClient/NovoraVeTransportPreference.cs`
- Modify: `src/NOVORA/Control/NLControlCommands.cs`
- Modify: `src/NOVORA.LinkClient/NovoraLink.AndroidXml.csproj`
- Modify: `tests/NOVORA.Tests/NLProjectTests.csproj`
- Create: `tests/NOVORA.Tests/Test/NLTestVeLanProtocol.cs`

**Interfaces:**
- Produces: `enum NLControlVeLanChannel { Video, Audio, Control }`.
- Produces: `record NLControlVeLanEndpoint(int Port, string Token, string Channel)`.
- Produces: `record NLControlVeLanOffer(int Version, string SessionId, string Host, string Fingerprint, long ExpiresUnix, NLControlVeLanEndpoint Video, NLControlVeLanEndpoint Control, NLControlVeLanEndpoint? Audio, int Bitrate, int MaxSize, int Fps, bool MuteDeviceAudio)` with `Validate(DateTimeOffset now, bool allowLoopback = false)`.
- Produces: `record NLControlVeLanHello(int Version, string SessionId, string Channel, string Token)`.
- Produces: `enum NovoraVeTransportSelection { Usb, Lan }` and `NovoraVeTransportPolicy.Resolve(string? storedValue)` with `Usb` as the invalid/missing-value default.
- Produces: Android-only `NovoraVeTransportPreference.Load/Save` backed by package-private Android preferences.
- Extends accepted commands with `startVideoLan`.

- [ ] **Step 1: Write failing protocol tests**

Add tests named `VeLanOfferAcceptsPrivateIpv4BeforeExpiry`, `VeLanOfferRejectsPublicLoopbackExpiredAndDuplicateTokens`, `VeLanHelloRequiresMatchingChannel`, and `VeTransportSelectionDefaultsToUsbAndAcceptsLan`. Assert protocol version `1`, expiry no later than 30 seconds, private IPv4-only hosts, three distinct tokens, and `Usb` as the missing/invalid-value default.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter FullyQualifiedName~NLTestVeLanProtocol`

Expected: FAIL because the VE LAN contracts and selection model do not exist.

- [ ] **Step 3: Implement the minimal contracts and validation**

Implement the exact interfaces above. Add `startVideoLan` to command validation with the same engine-state preconditions as `startAppVideo`; do not alter `startVideo`.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the command from Step 2. Expected: all `NLTestVeLanProtocol` tests pass.

- [ ] **Step 5: Commit**

Commit message: `feat: define native VE LAN protocol`

### Task 2: One-shot pinned-TLS channel listener

**Files:**
- Create: `src/NOVORA/VisionEngine/Transport/VETransportLanChannel.cs`
- Create: `tests/NOVORA.Tests/Test/NLTestVeLanChannel.cs`

**Interfaces:**
- Consumes: `NLControlVeLanEndpoint`, `NLControlVeLanHello`, `NLControlTrustStore` from Task 1.
- Produces: `VETransportLanChannel(IPAddress address, NLControlTrustStore trustStore, NLControlVeLanChannel channel, bool allowLoopback = false)`.
- Produces: `NLControlVeLanEndpoint PrepareVE(string sessionId, DateTimeOffset expiresAt)`.
- Produces: `Task<Stream> AcceptAsync(string sessionId, NLControlVeLanEndpoint endpoint, DateTimeOffset expiresAt, CancellationToken cancellationToken)`.
- Produces: `ValueTask DisposeAsync()`.

- [ ] **Step 1: Write failing listener tests**

Add tests `LanChannelAuthenticatesPinnedTlsAndReturnsStream`, `LanChannelRejectsWrongTokenChannelAndSession`, `LanChannelConsumesTokenOnce`, `LanChannelRejectsAfterExpiry`, and `LanChannelDisposeCancelsAccept`. Use loopback only through the test-only constructor flag.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter FullyQualifiedName~NLTestVeLanChannel`

Expected: FAIL because `VETransportLanChannel` does not exist.

- [ ] **Step 3: Implement the listener**

Bind an ephemeral port on the selected address, authenticate with the existing `NLControlTrustStore.Certificate`, accept TLS 1.2/1.3, read one framed `NLControlVeLanHello`, compare token bytes with `CryptographicOperations.FixedTimeEquals`, and consume the listener only after a valid hello. Reject malformed hex without logging token contents.

- [ ] **Step 4: Run tests and verify GREEN**

Run the command from Step 2. Expected: all channel tests pass.

- [ ] **Step 5: Commit**

Commit message: `feat: add secure VE LAN channels`

### Task 3: NOVORA PC negotiation and VisionEngine lifecycle

**Files:**
- Create: `src/NOVORA/UI/NLUIWindowMainAndroidVideoLan.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidEngines.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidControl.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidVideoSource.cs`
- Modify: `src/NOVORA/VisionEngine/Core/VECoreRuntime.cs`
- Modify: `tests/NOVORA.Tests/Test/NLTestAppControlVideoTransport.cs`
- Create: `tests/NOVORA.Tests/Test/NLTestVeLanLifecycle.cs`

**Interfaces:**
- Consumes: three `VETransportLanChannel` instances from Task 2.
- Produces: `Task<NLControlReply> PrepareVeLanVideoAsync(NLControlRequest request)`.
- Produces: `Task AcceptVeLanVideoAsync(NLControlVeLanOffer offer, CancellationTokenSource lifetime, long controlGeneration)`.
- Produces: `Task StopVeLanVideoAsync(string reason)`.
- Generalizes runtime source state to `VEExternalSourceKind.None | UsbAppControl | NativeLan` while keeping `StartAppControlAsync(string sourceId, Stream video, Stream control, Stream? audio, bool audioEnabled, CancellationToken)` compatible.

- [ ] **Step 1: Write failing lifecycle tests**

Add tests `StartVideoLanRequiresAuthorizedLanControl`, `StartVideoLanReturnsThirtySecondPrivateOffer`, `FirstValidatedFrameMarksNativeLanRunning`, `AudioFailureMarksDegradedWithoutStoppingVideo`, and `ControlGenerationChangeDisposesEveryChannel`.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter "FullyQualifiedName~NLTestVeLanLifecycle|FullyQualifiedName~NLTestAppControlVideoTransport"`

Expected: new tests fail while existing AppControl transport tests remain green.

- [ ] **Step 3: Implement PC-side negotiation**

Route `startVideoLan` to `PrepareVeLanVideoAsync`. Choose the same private interface used by the authorized LAN control server, create a 30-second offer, accept mandatory video/control concurrently and optional audio independently, then pass accepted streams into the existing external-source pipeline. Report `Degraded` only for optional audio failure. Tie cancellation to `_androidControlGeneration`, window close, and explicit stop.

- [ ] **Step 4: Preserve USB behavior and cleanup**

Keep `startVideo` and `startAppVideo` behavior unchanged. Make `stopVideo`, restart, control-session loss, and application shutdown stop either external-source kind without disposing LinkEngine or ExInEngine.

- [ ] **Step 5: Run lifecycle and regression tests**

Run the command from Step 2. Expected: all new and existing tests pass.

- [ ] **Step 6: Commit**

Commit message: `feat: negotiate native VE LAN sessions`

### Task 4: Android pinned-TLS VE LAN client

**Files:**
- Create: `src/NOVORA.LinkClient/NovoraVeLanClient.cs`
- Modify: `src/NOVORA.LinkClient/NovoraLink.AndroidXml.csproj`
- Create: `tests/NOVORA.Tests/Test/NLTestVeLanClient.cs`

**Interfaces:**
- Consumes: `NLControlVeLanOffer`, `NLControlVeLanHello` from Task 1.
- Produces: `Task<NovoraVeLanStreams> ConnectAsync(NLControlVeLanOffer offer, CancellationToken cancellationToken)`.
- Produces: `record NovoraVeLanStreams(Stream Video, Stream Control, Stream? Audio) : IAsyncDisposable`.

- [ ] **Step 1: Write failing TLS-client tests**

Add tests `ClientPinsAdvertisedCertificateAndAuthenticatesAllChannels`, `ClientRejectsChangedCertificate`, `ClientRejectsExpiredOfferBeforeOpeningSocket`, and `ClientClosesPreviouslyOpenedStreamsWhenLaterChannelFails`.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter FullyQualifiedName~NLTestVeLanClient`

Expected: FAIL because `NovoraVeLanClient` does not exist.

- [ ] **Step 3: Implement the TLS client**

Validate the offer before networking. For each channel create an IPv4 `TcpClient`, wrap it in `SslStream`, accept only a SHA-256 certificate fingerprint equal to `offer.Fingerprint`, authenticate TLS 1.2/1.3, then send the channel-specific framed hello. Dispose all opened resources if any later step fails.

- [ ] **Step 4: Run tests and verify GREEN**

Run the command from Step 2. Expected: all client tests pass.

- [ ] **Step 5: Commit**

Commit message: `feat: connect Android to VE LAN securely`

### Task 5: Android MediaProjection service and manual selector

**Files:**
- Create: `src/NOVORA.LinkClient/NovoraVeLanCaptureService.cs`
- Create: `src/NOVORA.LinkClient/NovoraVeCaptureCoordinator.cs`
- Modify: `src/NOVORA.LinkClient/EnginesActivity.cs`
- Modify: `src/NOVORA.LinkClient/NovoraViewState.cs`
- Modify: `src/NOVORA.LinkClient/Resources/layout/activity_engines.xml`
- Modify: `src/NOVORA.LinkClient/Properties/AndroidManifest.xml`
- Modify: `src/NOVORA.LinkClient/NovoraLink.AndroidXml.csproj`
- Link without modifying: `src/NOVORA.Android/AndroidVideo/NLAndroidVideoEncoder.cs`
- Link without modifying: `src/NOVORA.Android/AndroidVideo/NLAndroidVideoCompositor.cs`
- Link without modifying: `src/NOVORA.Android/AndroidVideo/NLAndroidAudioCapture.cs`
- Create: `tests/NOVORA.Tests/Test/NLTestVeCaptureCoordinator.cs`

**Interfaces:**
- Consumes: `NovoraVeLanClient`, `NLControlVideoSourceOffer`-compatible capture parameters, and the existing `VEProtocolWriter`.
- Produces: `Task<NovoraVeStartDecision> BeginAsync(NovoraVeTransportSelection selection, NLControlSessionState state)`.
- Produces: Android service actions `com.novora.linkclient.VE_LAN_START` and `com.novora.linkclient.VE_LAN_STOP`.
- Produces: selector values `USB` and `LAN`, persisted by `NovoraVeTransportPreference`.

- [ ] **Step 1: Write failing coordinator tests**

Add tests `UsbSelectionSendsStartVideoWithoutProjection`, `LanSelectionSendsStartVideoLanThenRequestsProjection`, `RunningSessionRequiresStopBeforeSelectionChange`, `ProjectionDenialStopsRemotePreparation`, and `NoFallbackOccursAfterLanFailure`.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter FullyQualifiedName~NLTestVeCaptureCoordinator`

Expected: FAIL because the coordinator does not exist.

- [ ] **Step 3: Add the selector and projection handshake**

Add a two-option control to `activity_engines.xml`. Disable it while VE is running or changing state. USB sends unchanged `startVideo`; LAN sends `startVideoLan`, parses and validates the returned offer, requests MediaProjection, and starts the foreground service only after Android returns `Result.Ok`. On denial, send `stopVideo` once and show an actionable state.

Persist a stopped-session selection through `NovoraVeTransportPreference`; verify the saved value by recreating `EnginesActivity` in the physical test. Do not write the selection while a VE session is active.

- [ ] **Step 4: Implement the foreground capture service**

Adapt the existing encoder/compositor/audio loop to write into `NovoraVeLanStreams`. Keep package-specific actions, notification channel, and service identity under `com.novora.linkclient`. Stop on projection callback, control disconnect, socket failure, explicit command, or process shutdown. Never persist an offer or token.

- [ ] **Step 5: Run coordinator tests and build Android**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter FullyQualifiedName~NLTestVeCaptureCoordinator`

Run: `dotnet build src/NOVORA.LinkClient/NovoraLink.AndroidXml.csproj --no-restore -c Debug`

Expected: tests pass; Android build has 0 errors and 0 warnings.

- [ ] **Step 6: Commit**

Commit message: `feat: capture Android screen over native VE LAN`

### Task 6: State synchronization, reconnect boundaries, and cleanup

**Files:**
- Modify: `src/NOVORA/Control/NLControlProtocol.cs`
- Modify: `src/NOVORA.LinkClient/NovoraViewState.cs`
- Modify: `src/NOVORA.LinkClient/BaseActivity.cs`
- Modify: `src/NOVORA.LinkClient/EnginesActivity.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidEngines.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidControl.cs`
- Create: `tests/NOVORA.Tests/Test/NLTestVeLanState.cs`

**Interfaces:**
- Extends `NLControlEngines` with `string VideoTransport`, `string VideoPhase`, and `bool VideoDegraded` using backward-compatible defaults.
- Produces stable phases: `Disconnected`, `Ready`, `AwaitingPermission`, `Preparing`, `Connecting`, `Streaming`, `Degraded`, `Error`, `Stopping`.

- [ ] **Step 1: Write failing state tests**

Add tests `LanPhasesMapToVisibleSpanishStates`, `WifiLossStopsLanWithoutSelectingUsb`, `ControlLossCancelsOfferAndCapture`, `AddressChangeRejectsStaleOffer`, and `AudioFailurePublishesDegradedWhileVideoRemainsRunning`.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter FullyQualifiedName~NLTestVeLanState`

Expected: FAIL because the state fields and mappings do not exist.

- [ ] **Step 3: Implement synchronized state and cleanup**

Publish phases from actual listener, projection, and VisionEngine transitions. On LAN/control loss, cancel the LAN capture and PC listeners, stop only the native VE source, keep the selection `LAN`, and expose a retry action. Do not call the USB preparation path.

- [ ] **Step 4: Run focused and full control tests**

Run the command from Step 2, then:

`dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter "FullyQualifiedName~NLTestVeLan|FullyQualifiedName~NLTestLinkClientViewState|FullyQualifiedName~NLTestAppControlVideoTransport|FullyQualifiedName~NLTestAndroidUsbAutomatic"`

Expected: all tests pass.

- [ ] **Step 5: Commit**

Commit message: `feat: synchronize VE LAN lifecycle states`

### Task 7: End-to-end builds and physical USB/LAN verification

**Files:**
- Modify only if a verified defect is found in Tasks 1-6.
- Record evidence: `Documentation/NLDocumentationVeLanVerification.md`

**Interfaces:**
- Consumes the completed PC and Android implementations.
- Produces a repeatable verification record with versions, device serial, network path, observed states, and failure checks; excludes tokens and secrets.

- [ ] **Step 1: Run automated regression suites**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj --no-restore --filter "FullyQualifiedName~NLTestVeLan|FullyQualifiedName~NLTestLinkClientViewState|FullyQualifiedName~NLTestAppControlVideoTransport|FullyQualifiedName~NLTestAndroidUsbAutomatic"`

Expected: 0 failed.

- [ ] **Step 2: Build both products**

Run: `dotnet build src/NOVORA/NLProjectDesktop.csproj --no-restore -c Release`

Run: `dotnet build src/NOVORA.LinkClient/NovoraLink.AndroidXml.csproj --no-restore -c Debug`

Expected: both builds finish with 0 errors and 0 warnings.

- [ ] **Step 3: Verify fresh six-digit LAN enrollment**

Reinstall NOVORA-LINK, keep USB disconnected after installation, generate a LAN code on PC, pair once, and record that the control state becomes `Ready` without an ADB device.

- [ ] **Step 4: Verify native LAN video**

Select `VE LAN`, start VE, approve MediaProjection, and verify a decoded moving frame, real `Streaming` state, audio when enabled, control input, stop, and restart. Confirm `adb devices` is empty during the session.

- [ ] **Step 5: Verify LAN failure behavior**

Disable Wi-Fi during streaming. Verify both sides clean up, selection remains LAN, no USB/ADB command runs, and retry succeeds after reconnecting and restoring the remembered control trust.

- [ ] **Step 6: Verify USB regression path**

Connect USB, select `VE USB`, start and stop the existing ADB VisionEngine path, and verify it behaves as before. Change selector while running and confirm the UI requires Stop first.

- [ ] **Step 7: Write the verification record and commit**

Document commands and outcomes in `Documentation/NLDocumentationVeLanVerification.md` without credentials.

Commit message: `test: verify native VE LAN and USB transports`

### Task 8: Final review and release handoff

**Files:**
- Review all files changed by Tasks 1-7.
- Update: `Documentation/NLDocumentationAndroidXmlLayouts.md` only if the selector changes its documented screen contract.

**Interfaces:**
- Produces no new runtime interface; verifies the complete feature against the spec.

- [ ] **Step 1: Review the complete branch diff**

Check specifically for AppControl modifications, token/certificate logging, public-address binding, automatic fallback, missing cancellation paths, and accidental USB behavior changes. Expected: none.

- [ ] **Step 2: Run final verification**

Run the automated tests and both build commands from Task 7. Expected: all tests pass and both builds have 0 errors and 0 warnings.

- [ ] **Step 3: Commit any review-only corrections**

If corrections are required, commit them as `fix: address VE LAN final review`; otherwise create no empty commit.
