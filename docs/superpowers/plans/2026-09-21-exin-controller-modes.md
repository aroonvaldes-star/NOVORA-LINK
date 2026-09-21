# ExInEngine Controller Modes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver reliable Xbox and DualShock 4 Game/UI modes, per-controller calibration and diagnostics, pointer control, battery alerts, and repeatable cross-game continuity on PC 1.4 and Android 1.4.25.

**Architecture:** ExInEngine remains the sole owner of physical input and persistent virtual outputs. Focused identity, profile, diagnostic, translation, and HID components feed one revisioned state shared by WPF and Android; VisionEngine never owns or restarts ExIn.

**Tech Stack:** .NET 8 WPF, .NET 10 Android, C#, SDL3 interop, scrcpy-compatible UHID, JSON, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-21-exin-controller-modes-design.md`

## Global Constraints

- PC remains `1.4`; Android becomes `ApplicationVersion 25` and `ApplicationDisplayVersion 1.4.25`.
- ExInEngine, VisionEngine, LinkEngine, and STEngine keep independent ownership and recovery.
- Use SDL events and channel readiness; no foreground-app, controller, or battery polling.
- One active Android phone per PC; ExIn owns a persistent control-only session.
- Never claim universal Play Store support from finite tests.
- Touchpad failure must not stop gamepad output; ExIn failure must not stop other engines.
- Do not package or call Android 1.4.25 ready until the physical matrix passes.
- Check `Documentation/NLDocumentationNamingMap.json` before new production filenames.

## Review Focus

- Identical controller models without serials need collision-safe profile keys; Task 2 tests path/instance fallback.
- A mode switch with held buttons must neutralize old output and reject late events; Task 5 tests generation fencing.
- Battery values may be unknown or regress while charging; Task 4 tests unknown values and alert rearming.
- Malformed touchpad finger ordering must leave gamepad input alive; Task 6 tests isolated reset.
- Channel recovery during a menu must recreate exactly one family-correct device; Task 7 tests idempotence.

---

### Task 1: Controller Domain and Family Identification

**Files:**
- Create: `src/NOVORA/ExInEngine/ExInControllerFamily.cs`
- Create: `src/NOVORA/ExInEngine/ExInControllerIdentity.cs`
- Create: `src/NOVORA/ExInEngine/ExInInputMode.cs`
- Modify: `src/NOVORA/ExInEngine/ExInDevice.cs`
- Modify: `src/NOVORA/ExInEngine/ExInSdl.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInIdentity.cs`

**Interfaces:**
- Produces: `ExInControllerIdentity CreateVE(uint instanceId, ushort vendorId, ushort productId, Guid guid, string name, string? serial, string? path)`, `ExInControllerFamily { Xbox, DualShock4, Generic }`, and `ExInInputMode { Game, Ui }`.

- [ ] **Step 1: Write failing identity tests**

```csharp
[Theory]
[InlineData(0x045E, 0x028E, ExInControllerFamily.Xbox)]
[InlineData(0x054C, 0x05C4, ExInControllerFamily.DualShock4)]
[InlineData(0x054C, 0x09CC, ExInControllerFamily.DualShock4)]
[InlineData(0x1234, 0x5678, ExInControllerFamily.Generic)]
public void Identity_classifies_family(ushort vendor, ushort product, ExInControllerFamily expected)
{
    var value = ExInControllerIdentity.CreateVE(1, vendor, product, Guid.Empty, "Pad", null, "path-a");
    Assert.Equal(expected, value.Family);
}
```

- [ ] **Step 2: Run the test and verify missing types fail**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --filter FullyQualifiedName~NLTestExInIdentity --no-restore`

- [ ] **Step 3: Implement classification and stable keys**

```csharp
public enum ExInControllerFamily { Generic, Xbox, DualShock4 }
public enum ExInInputMode { Game, Ui }

public sealed record ExInControllerIdentity(string ProfileKey, ExInControllerFamily Family,
    ushort VendorId, ushort ProductId, Guid Guid, string Name, string? Serial, string? Path);
```

The factory uses VID/PID for family and serial, then path, then instance ID as discriminator. Bind optional SDL vendor/product/GUID/type/path/serial exports and degrade to unavailable data without mislabeling.

- [ ] **Step 4: Run focused tests and PC build**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --filter FullyQualifiedName~NLTestExInIdentity --no-restore`
Run: `dotnet build src/NOVORA/NLProjectDesktop.csproj -c Release --no-restore`

- [ ] **Step 5: Commit**

```powershell
git add src/NOVORA/ExInEngine tests/NOVORA.Tests/Test/NLTestExInIdentity.cs
git commit -m "feat(exin): identify controller families"
```

### Task 2: Persistent Per-Controller Calibration

**Files:**
- Create: `src/NOVORA/ExInEngine/ExInCalibrationProfile.cs`
- Create: `src/NOVORA/ExInEngine/ExInProfileStore.cs`
- Modify: `src/NOVORA/ExInEngine/ExInManager.cs`
- Modify: `src/NOVORA/Service/NLServiceNovoraPaths.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInProfiles.cs`

**Interfaces:**
- Consumes: Task 1 profile keys.
- Produces: `LoadVE(string)`, `SaveVE(ExInCalibrationProfile)`, and raw/corrected calibration results.

- [ ] **Step 1: Write failing isolation, collision, corrupt-file, and failed-calibration tests**

```csharp
[Fact]
public void Profiles_are_isolated_by_controller()
{
    using var folder = new NLTestTemporaryFolder();
    var store = new ExInProfileStore(folder.Path);
    store.SaveVE(ExInCalibrationProfile.ValidVE("xbox-a", 900));
    store.SaveVE(ExInCalibrationProfile.ValidVE("ds4-a", -700));
    Assert.Equal(900, store.LoadVE("xbox-a")!.Center.LeftX);
    Assert.Equal(-700, store.LoadVE("ds4-a")!.Center.LeftX);
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --filter FullyQualifiedName~NLTestExInProfiles --no-restore`

- [ ] **Step 3: Implement versioned atomic JSON storage**

```csharp
public sealed record ExInCalibrationProfile(int SchemaVersion, string ProfileKey,
    ExInState Center, ExInState Minimum, ExInState Maximum,
    ExInAxisDeadzone Deadzone, DateTimeOffset CalibratedAtUtc);
```

Write temporary, flush, and replace. Validate schema/key/ranges. Store no input history. Preserve the previous valid profile when a new calibration has insufficient travel.

- [ ] **Step 4: Run full tests and commit**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
git add src/NOVORA/ExInEngine src/NOVORA/Service/NLServiceNovoraPaths.cs tests/NOVORA.Tests/Test/NLTestExInProfiles.cs
git commit -m "feat(exin): persist calibration per controller"
```

### Task 3: Event-Driven Health Diagnostics

**Files:**
- Create: `src/NOVORA/ExInEngine/ExInDiagnosticEngine.cs`
- Create: `src/NOVORA/ExInEngine/ExInDiagnosticStatus.cs`
- Modify: `src/NOVORA/ExInEngine/ExInManager.cs`
- Modify: `src/NOVORA/ExInEngine/ExInStatus.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInDiagnostics.cs`

**Interfaces:**
- Produces: classifications `Healthy`, `CorrectableByCalibration`, `CalibrationIncomplete`, `ReviewRecommended`, and `ProbableHardwareFault`.

- [ ] **Step 1: Write failing table tests for drift, noise, reduced travel, trigger return, stuck button, and single-spike immunity**

```csharp
[Theory]
[InlineData(0, 120, ExInHealth.Healthy)]
[InlineData(2400, 120, ExInHealth.CorrectableByCalibration)]
[InlineData(9000, 4000, ExInHealth.ProbableHardwareFault)]
public void Rest_window_classifies_offset(short center, short noise, ExInHealth expected)
{
    var engine = new ExInDiagnosticEngine();
    for (int index = 0; index < 120; index++)
    {
        short value = checked((short)(center + (index % 2 == 0 ? noise : -noise)));
        engine.ObserveVE(new ExInState(value, 0, 0, 0, 0, 0, ExInButtons.None),
            TimeSpan.FromMilliseconds(index * 8));
    }
    Assert.Equal(expected, engine.StatusVE.Classification);
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --filter FullyQualifiedName~NLTestExInDiagnostics --no-restore`

- [ ] **Step 3: Implement fixed-size aggregate windows**

Keep count/min/max/mean/variance and held durations, not history. Expose affected controls, raw/corrected values, severity, explanation, and `CalibrationCanCorrect`. Warnings never become engine failures.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
git add src/NOVORA/ExInEngine tests/NOVORA.Tests/Test/NLTestExInDiagnostics.cs
git commit -m "feat(exin): diagnose controller health"
```

### Task 4: Battery Events and Alerts

**Files:**
- Create: `src/NOVORA/ExInEngine/ExInBatteryStatus.cs`
- Create: `src/NOVORA/ExInEngine/ExInBatteryAlerts.cs`
- Modify: `src/NOVORA/ExInEngine/ExInEvent.cs`
- Modify: `src/NOVORA/ExInEngine/ExInTypeEvent.cs`
- Modify: `src/NOVORA/ExInEngine/ExInSdl.cs`
- Modify: `src/NOVORA/ExInEngine/ExInManager.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInBattery.cs`

**Interfaces:**
- Produces: `Low15`, `Critical5`, and `Full100` transition alerts per profile key.

- [ ] **Step 1: Write failing threshold/deduplication tests**

```csharp
[Fact]
public void Alerts_cross_once_and_rearm()
{
    var alerts = new ExInBatteryAlerts();
    Assert.Null(alerts.UpdateVE(OnBattery(16)));
    Assert.Equal(ExInBatteryAlertKind.Low15, alerts.UpdateVE(OnBattery(15))!.Kind);
    Assert.Null(alerts.UpdateVE(OnBattery(14)));
    Assert.Equal(ExInBatteryAlertKind.Critical5, alerts.UpdateVE(OnBattery(5))!.Kind);
    Assert.Equal(ExInBatteryAlertKind.Full100, alerts.UpdateVE(Charged(100))!.Kind);
}
```

Also test percent `-1`, unknown, charging regression, duplicate 100%, and rearming.

- [ ] **Step 2: Bind `SDL_EVENT_JOYSTICK_BATTERY_UPDATED` and one initial `SDL_GetGamepadPowerInfo` read**

Do not add a timer. Unknown stays unavailable.

- [ ] **Step 3: Run tests and commit**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
git add src/NOVORA/ExInEngine tests/NOVORA.Tests/Test/NLTestExInBattery.cs
git commit -m "feat(exin): report controller battery alerts"
```

### Task 5: Atomic Game/UI Modes and HID Families

**Files:**
- Create: `src/NOVORA/ExInEngine/ExInOutputGeneration.cs`
- Create: `src/NOVORA/ExInEngine/ExInOutputProfile.cs`
- Create: `src/NOVORA/ExInEngine/ExInDualShock4Descriptor.cs`
- Modify: `src/NOVORA/ExInEngine/ExInDescriptor.cs`
- Modify: `src/NOVORA/ExInEngine/ExInManager.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInModes.cs`

**Interfaces:**
- Produces: `Task<ExInModeResult> SetModeAsync(ExInInputMode, CancellationToken)`.
- Every output report carries a monotonically increasing generation.

- [ ] **Step 1: Write failing fake-output tests**

```csharp
[Fact]
public async Task Switch_neutralizes_destroys_and_recreates()
{
    var output = new RecordingExInOutput();
    var owner = ExInOutputGeneration.ForTest(output, XboxIdentity());
    await owner.SetModeAsync(ExInInputMode.Ui, default);
    Assert.Equal(new[] { "neutral:3", "destroy:3", "create:mouse", "neutral:mouse" }, output.Operations);
}
```

Cover rollback, held buttons, late events, Xbox identity, DS4 identity, and generic fallback.

- [ ] **Step 2: Implement family descriptors and serialized generation fencing**

Keep verified Xbox mapping. Build DS4 descriptor/report from public HID specifications and validate classification physically; do not copy unlicensed code. On failure destroy partial output and restore last valid mode.

- [ ] **Step 3: Run tests and commit**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
git add src/NOVORA/ExInEngine tests/NOVORA.Tests/Test/NLTestExInModes.cs
git commit -m "feat(exin): add atomic game and UI modes"
```

### Task 6: DualShock Touchpad and Xbox UI Pointer

**Files:**
- Create: `src/NOVORA/ExInEngine/ExInPointerTranslator.cs`
- Create: `src/NOVORA/ExInEngine/ExInMouseDescriptor.cs`
- Create: `src/NOVORA/ExInEngine/ExInMouseReport.cs`
- Modify: `src/NOVORA/ExInEngine/ExInSdl.cs`
- Modify: `src/NOVORA/ExInEngine/ExInEvent.cs`
- Modify: `src/NOVORA/ExInEngine/ExInManager.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInPointer.cs`

**Interfaces:**
- Consumes: SDL touchpad events and calibrated stick states.
- Produces: relative mouse X/Y, wheel, horizontal wheel, and left button.

- [ ] **Step 1: Write failing pointer tests**

```csharp
[Fact]
public void Xbox_maps_left_to_pointer_and_right_to_scroll()
{
    var value = new ExInPointerTranslator(0.08);
    Assert.Equal(2, value.FromXboxVE(6000, 0, 0, 0, false).X);
    Assert.Equal(-1, value.FromXboxVE(0, 0, 0, 12000, false).Wheel);
}
```

Cover A click, B Back, progressive acceleration, drift suppression, DS4 one-finger motion, physical click, two-finger scroll, finger-order reversal, and touchpad exception isolation.

- [ ] **Step 2: Bind SDL touchpad down/motion/up events**

Validate touchpad/finger indexes. Malformed input resets only gesture state.

- [ ] **Step 3: Implement mouse HID and UI translation**

Xbox left stick uses calibrated deadzone/acceleration, right stick scrolls, D-pad navigates, A clicks, B goes Back. DS4 touchpad works in both modes; L2/R2 remain triggers.

- [ ] **Step 4: Run tests and commit**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
git add src/NOVORA/ExInEngine tests/NOVORA.Tests/Test/NLTestExInPointer.cs
git commit -m "feat(exin): add controller UI pointer input"
```

### Task 7: Persistent Recovery and Reactivation

**Files:**
- Modify: `src/NOVORA/ExInEngine/ExInControlSession.cs`
- Modify: `src/NOVORA/ExInEngine/ExInCoreEngine.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainVisionEngine.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInRecovery.cs`

**Interfaces:**
- Produces: `Task<ExInModeResult> ReactivateAsync(CancellationToken)`.
- Preserves one ExIn control session independent of VE.

- [ ] **Step 1: Write failing lifecycle tests**

```csharp
[Fact]
public async Task Vision_and_five_menu_cycles_do_not_reenumerate()
{
    var harness = new ExInRecoveryHarness();
    await harness.StartGameModeAsync();
    for (int i = 0; i < 5; i++) { await harness.VisionStartStopAsync(); await harness.MenuCycleAsync(); }
    Assert.Equal(1, harness.Output.CreateCount);
    Assert.Equal(0, harness.Output.DestroyCount);
}
```

Also test real disconnect recovery creates exactly one device and manual reactivation preserves mode/calibration.

- [ ] **Step 2: Implement readiness/failure callbacks and idempotent recovery**

Recover only from transport termination or physical removal. Idle, congestion, `WouldBlock`, queue depth, and VE state are not failures.

- [ ] **Step 3: Run tests and commit**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
git add src/NOVORA/ExInEngine src/NOVORA/UI/NLUIWindowMainVisionEngine.cs tests/NOVORA.Tests/Test/NLTestExInRecovery.cs
git commit -m "fix(exin): preserve controllers across game sessions"
```

### Task 8: Shared Protocol and PC UI

**Files:**
- Modify: `src/NOVORA/Control/NLControlProtocol.cs`
- Modify: `src/NOVORA/Control/NLControlCommands.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMainAndroidControl.cs`
- Modify: `src/NOVORA/UI/NLUIWindowMain.xaml`
- Modify: `src/NOVORA/UI/NLUIWindowMain.xaml.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInProtocol.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestAndroidEngines.cs`

**Interfaces:**
- Adds revisioned `exin.mode` and `exin.reactivate` actions.
- Extends `NLControlExIn` with identity, mode, transition, capabilities, raw/corrected state, diagnostics, calibration, and battery.

- [ ] **Step 1: Write failing validation and round-trip tests**

```csharp
[Theory]
[InlineData("Game")]
[InlineData("Ui")]
public void Mode_accepts_known_values(string mode)
{
    Assert.Null(NLControlCommands.Validate(Request("exin.mode", mode), StateWithController()));
}
```

Test invalid mode, stale revision, busy transition, no-controller reactivation, and legacy optional fields.

- [ ] **Step 2: Extend protocol and route commands**

Increment snapshot revision after accepted mode/calibration/reactivation changes. Reject stale writes.

- [ ] **Step 3: Build PC controls**

Add Game/UI segmented control, identity, raw/corrected values, calibration flow, health, battery, capabilities, and Reactivate with busy/error states. Update by events only.

- [ ] **Step 4: Run tests/build and commit**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
dotnet build src/NOVORA/NLProjectDesktop.csproj -c Release --no-restore
git add src/NOVORA/Control src/NOVORA/UI tests/NOVORA.Tests/Test/NLTestExInProtocol.cs tests/NOVORA.Tests/Test/NLTestAndroidEngines.cs
git commit -m "feat(exin): share modes and diagnostics with PC"
```

### Task 9: Android Bubble and Notifications

**Files:**
- Create: `src/NOVORA.Android/AndroidService/NLAndroidServiceControllerNotifications.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingController.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingPreferences.cs`
- Modify: `src/NOVORA.Android/AndroidService/NLAndroidServiceControl.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs`
- Modify: `src/NOVORA.Android/Properties/NLPropertiesAndroidManifest.xml`
- Test: `tests/NOVORA.Tests/Test/NLTestAndroidControl.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestAndroidPackage.cs`

**Interfaces:**
- Consumes Task 8 snapshot/actions.
- Produces event-driven mode commands, edge auto-hide, and battery notifications.

- [ ] **Step 1: Extract and test pure docking state**

Test four-second inactivity, nearest edge, expanded/busy/dragging suppression, handle bounds, and restore.

- [ ] **Step 2: Add bubble segmented control and Reactivate**

Render server-confirmed mode/family/health/battery. Lock controls while busy.

- [ ] **Step 3: Implement auto-hide**

Use one cancellable four-second UI inactivity deadline reset by interaction. Dock to nearest edge; leave a narrow translucent handle; hidden content must not intercept app touches.

- [ ] **Step 4: Add notification channel and Android 13+ permission**

Post only server alert transitions Low15/Critical5/Full100 and name the controller.

- [ ] **Step 5: Compile/test and commit**

```powershell
dotnet build src/NOVORA.Android/NLProjectAndroid.csproj -c Release -t:Compile --no-restore
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
git add src/NOVORA.Android tests/NOVORA.Tests/Test/NLTestAndroidControl.cs tests/NOVORA.Tests/Test/NLTestAndroidPackage.cs
git commit -m "feat(android): add ExIn modes and hidden bubble"
```

### Task 10: Android 1.4.25 and Physical Release Gate

**Files:**
- Modify: `src/NOVORA.Android/NLProjectAndroid.csproj`
- Modify: `src/NOVORA/Android/NLAndroidRelease.json`
- Modify: `Documentation/NLDocumentationReleaseVersion.md`
- Create: `Documentation/NLDocumentationExIn1425Evidence.json`
- Test: `tests/NOVORA.Tests/Test/NLTestOfficialRelease.cs`

**Interfaces:**
- Produces signed Android 1.4.25 and evidence separating automated/runtime/physical results.

- [ ] **Step 1: Add failing metadata assertions**

Assert VersionCode 25, VersionName 1.4.25, release descriptor, APK manifest, canonical APK, and SHA-256 agreement.

- [ ] **Step 2: Increment source version**

```xml
<ApplicationVersion>25</ApplicationVersion>
<ApplicationDisplayVersion>1.4.25</ApplicationDisplayVersion>
```

Do not update canonical APK/hash before producing the signed binary.

- [ ] **Step 3: Run sequential automated verification**

```powershell
dotnet build src/NOVORA/NLProjectDesktop.csproj -c Release --no-restore
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore
dotnet build src/NOVORA.Android/NLProjectAndroid.csproj -c Release --no-restore
```

Expected: 0 warnings/errors and all tests pass. Never run competing builds against shared `obj`.

- [ ] **Step 4: Package/sign and verify binary manifest/hash**

Require binary `versionCode=25` and `versionName=1.4.25`, then update canonical integrated APK, descriptor, docs, and hash together.

- [ ] **Step 5: Execute the physical matrix**

Test both controller families across Launcher, Instagram, Facebook, TikTok, Piano Tiles 3, Clash Royale, RB6, and CoD Mobile when installed. Record unavailable titles, do not mark them passed.

- [ ] **Step 6: Enforce continuity**

Run five RB6 match/menu cycles, equivalent CoD cycles, VE start/stop between cycles, app detours, mode transitions, reconnects, pointer/click/scroll, calibration, diagnostics, and battery alerts. Any NOVORA-caused controller loss fails Release; Reactivate does not turn failure into pass.

- [ ] **Step 7: Record evidence and commit only after the gate passes**

```powershell
git add src/NOVORA.Android/NLProjectAndroid.csproj src/NOVORA/Android/NLAndroidRelease.json Documentation tests/NOVORA.Tests/Test/NLTestOfficialRelease.cs
git commit -m "release(android): validate ExIn 1.4.25"
```
