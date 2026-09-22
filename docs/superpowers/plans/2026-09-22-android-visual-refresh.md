# NOVORA Android Visual Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a cohesive dark/light NOVORA Android interface across every activity and the floating control while preserving the complete 1.4.26 functional baseline and independent engine behavior.

**Architecture:** Keep the native .NET Android activities and service contracts. Extract reusable visual components and page builders from the current monolithic activity, then migrate each surface without moving transport, engine, or recovery ownership into UI code.

**Tech Stack:** .NET 10 Android, C#, native Android Views, xUnit, ADB, aapt2.

**Spec:** `docs/superpowers/specs/2026-09-22-android-visual-refresh-design.md`

## Global Constraints

- Keep application ID `com.novora.appcontrol`, minimum API 26, and current trusted USB/LAN control contracts.
- Preserve independent LinkEngine, VisionEngine, ExInEngine, and STEngine lifecycle and recovery.
- Preserve every real function available in the 1.4.26 baseline; never replace unavailable data with sample metrics.
- Add no polling, advertising, payment, analytics, private-data persistence, or new third-party dependency.
- The floating control remains optional and cannot own or mutate ExIn lifecycle merely because it is hidden or disabled.
- Follow `Documentation/NLDocumentationNamingMap.json` before introducing names.
- Package changes as the next Android build, inspect the binary manifest, synchronize `src/NOVORA/Android/NLAndroidApp.apk`, descriptor, and SHA-256.
- Report source, compile, tests, packaging, installation, and physical validation as separate evidence.

## Review Focus

- Large Android font scale: labels wrap without obscuring controls, and bottom navigation remains stable.
- Disconnected or stale snapshot: every page remains navigable and presents unavailable states without invented values.
- Theme switch during an active session: all visible surfaces rebuild with the new palette while the service session remains connected.
- Overlay disabled or permission denied: ExIn mode and output continue unchanged; only floating UI disappears.
- Rotation, cutouts, keyboard, and small screens: insets remain valid and no fixed-format control overflows.

---

### Task 1: Stabilize Pending ExIn And Android Release Work

**Files:**
- Modify: `src/NOVORA/UI/NLUIWindowMainVisionEngine.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingController.cs`
- Modify: `src/NOVORA/Control/NLControlFloatingDockState.cs`
- Modify: `src/NOVORA.Android/NLProjectAndroid.csproj`
- Modify: `src/NOVORA/Android/NLAndroidApp.apk`
- Modify: `src/NOVORA/Android/NLAndroidRelease.json`
- Modify: `tests/NOVORA.Tests/Test/NLTestControlFloatingDockState.cs`
- Modify: `tests/NOVORA.Tests/Test/NLTestOfficialRelease.cs`

**Interfaces:**
- Consumes: existing `ExInControlSession`, unique scrcpy SCID/socket allocation, and overlay preferences.
- Produces: independent ExIn control-only session and Android release metadata synchronized at 1.4.27.

- [ ] **Step 1: Confirm no presentation code binds ExIn to `VEControlManager`**

Run:
```powershell
rg -n "AttachExInToVisionControlVE|DetachExInFromVisionControlVE|PrepareExInForVisionSessionVE|RestoreGameModeBeforeRemoval|ShouldRestoreGameMode" src tests
```
Expected: no matches.

- [ ] **Step 2: Run the existing release and independence tests**

Run:
```powershell
dotnet test NOVORA.sln -c Release --no-restore
```
Expected: all tests pass, including `Current_android_source_and_canonical_package_agree`.

- [ ] **Step 3: Verify the canonical APK contract**

Run:
```powershell
& 'C:\Program Files (x86)\Android\android-sdk\build-tools\36.0.0\aapt2.exe' dump badging src\NOVORA\Android\NLAndroidApp.apk | Select-String '^package:|^sdkVersion:|^targetSdkVersion:'
Get-FileHash src\NOVORA\Android\NLAndroidApp.apk -Algorithm SHA256
```
Expected: package `com.novora.appcontrol`, version 1.4.27/code 27, minSdk 26, targetSdk 36, descriptor hash equal to the file hash.

- [ ] **Step 4: Commit the stabilized baseline**

```powershell
git add src/NOVORA/UI/NLUIWindowMainVisionEngine.cs src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingController.cs src/NOVORA/Control/NLControlFloatingDockState.cs src/NOVORA.Android/NLProjectAndroid.csproj src/NOVORA/Android/NLAndroidApp.apk src/NOVORA/Android/NLAndroidRelease.json tests/NOVORA.Tests/Test/NLTestControlFloatingDockState.cs tests/NOVORA.Tests/Test/NLTestOfficialRelease.cs
git commit -m "fix: separate ExIn and floating control lifecycles"
```

### Task 2: Build The Shared Android Design System

**Files:**
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUITheme.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIVisual.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUILayout.cs`
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIComponents.cs`

**Interfaces:**
- Consumes: `NLAndroidUITheme.Current(Context)` and Android native `View` types.
- Produces: `AppBar`, `StatusChip`, `SectionTitle`, `EngineRow`, `ActionTile`, `CommandButton`, `SegmentedControl`, and `BottomTab` factories.

- [ ] **Step 1: Add exact palette and geometry tokens**

Implement immutable values on `NLAndroidUIPalette` for background, surface, raised surface, border, primary/secondary text, accent, success, warning, error, disabled, 8 dp card radius, 16 dp page padding, and 48 dp minimum touch target. Use the values from the approved spec.

- [ ] **Step 2: Implement reusable component factories**

Create `NLAndroidUIComponents` with signatures:
```csharp
internal static View AppBar(Context context, string version, string connection, string engines, Action toggleTheme, Action openSettings);
internal static TextView StatusChip(Context context, string text, NLAndroidUIStatusTone tone);
internal static TextView SectionTitle(Context context, string text);
internal static View EngineRow(Context context, string name, string summary, string state, NLAndroidUIStatusTone tone, Action? action);
internal static Button ActionTile(Context context, string title, string caption, int iconResource, Action action);
internal static Button CommandButton(Context context, string text, bool primary, Func<Task> action);
internal static LinearLayout SegmentedControl(Context context, IReadOnlyList<(string Label, bool Selected, bool Enabled, Func<Task> Action)> items);
internal static Button BottomTab(Context context, string label, int iconResource, bool selected, Action action);
```

- [ ] **Step 3: Make dynamic text layout-safe**

Set `MaxLines`, ellipsis, minimum heights, wrapping, and stable layout weights explicitly. Use icons for theme/settings/navigation commands and content descriptions for every actionable icon.

- [ ] **Step 4: Compile Android**

Run:
```powershell
dotnet build src/NOVORA.Android/NLProjectAndroid.csproj -c Release --no-restore -t:Compile
```
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit the design system**

```powershell
git add src/NOVORA.Android/AndroidUI/NLAndroidUITheme.cs src/NOVORA.Android/AndroidUI/NLAndroidUIVisual.cs src/NOVORA.Android/AndroidUI/NLAndroidUILayout.cs src/NOVORA.Android/AndroidUI/NLAndroidUIComponents.cs
git commit -m "feat: add Android visual design system"
```

### Task 3: Recompose The Main Shell And Primary Pages

**Files:**
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs`
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIMainShell.cs`
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIHomePage.cs`
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIConnectPage.cs`
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIEnginesPage.cs`

**Interfaces:**
- Consumes: Task 2 component factories and existing `NLControlSessionState`/`NLControlSnapshot` callbacks.
- Produces: five-destination shell plus Home, Connect, and Engines page view references required by existing update methods.

- [ ] **Step 1: Extract shell construction without moving state ownership**

Implement `NLAndroidUIMainShell` to create app bar, page host, status region, and fixed bottom navigation. `NLAndroidUIActivity` remains owner of service binding, commands, lifecycle, and snapshot state.

- [ ] **Step 2: Build Home from real state**

Compose the existing welcome bitmap, four 2x2 action tiles, USB/LAN strip, and four engine rows. Bind values only through existing `_snapshot`, `_service.Session.Current`, and `UpdateSnapshot` paths.

- [ ] **Step 3: Build Connect with USB priority**

Place physical USB detection first; group QR, LAN discovery, stored PCs, and invitation paste under auxiliary connection. Preserve all current click handlers and authorization checks.

- [ ] **Step 4: Build Engines as independent rows**

Render Link, Vision, ExIn, and ST independently. Keep Vision start/stop/settings, Link start/stop, and ExIn navigation handlers unchanged.

- [ ] **Step 5: Verify stale and disconnected states**

Temporarily exercise `UpdateSnapshot(null)` and disconnected session state in the existing rendering path. Expected: no exception, navigation remains enabled, commands requiring connection are disabled, and no sample values appear.

- [ ] **Step 6: Compile and commit**

Run the Android compile command from Task 2, then:
```powershell
git add src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs src/NOVORA.Android/AndroidUI/NLAndroidUIMainShell.cs src/NOVORA.Android/AndroidUI/NLAndroidUIHomePage.cs src/NOVORA.Android/AndroidUI/NLAndroidUIConnectPage.cs src/NOVORA.Android/AndroidUI/NLAndroidUIEnginesPage.cs
git commit -m "feat: redesign Android shell and engine pages"
```

### Task 4: Redesign ExIn And Vision Settings Without Coupling Engines

**Files:**
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIExInPage.cs`
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIVisionSettingsPage.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestExInModes.cs`

**Interfaces:**
- Consumes: `NLControlExIn`, existing `exin.mode`, `exin.reactivate`, calibration, and `applyVideoSettings` commands.
- Produces: stable ExIn status/live/calibration views and VE settings view preserving existing fields and actions.

- [ ] **Step 1: Pin ExIn behavior before visual migration**

Add a test proving a mode transition preserves the engine mode on failed output creation and that reactivation does not require floating UI. Use existing `RecordingOutputVE`; do not reference Android Views from desktop tests.

- [ ] **Step 2: Build ExIn page**

Use hardware identity, segmented Game/UI mode, reactivation, battery/diagnostic blocks, raw/corrected live values, and calibration actions. Never display fixed polling rate or latency.

- [ ] **Step 3: Build Vision settings page**

Preserve monitor, profile, bitrate, resolution, FPS, audio selectors, recording lockout, and apply-together semantics. Use compact field rows rather than nested cards.

- [ ] **Step 4: Run focused tests and compile**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore --filter "FullyQualifiedName~NLTestExIn"
dotnet build src/NOVORA.Android/NLProjectAndroid.csproj -c Release --no-restore -t:Compile
```
Expected: all focused tests pass; Android has 0 warnings/errors.

- [ ] **Step 5: Commit**

```powershell
git add src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs src/NOVORA.Android/AndroidUI/NLAndroidUIExInPage.cs src/NOVORA.Android/AndroidUI/NLAndroidUIVisionSettingsPage.cs tests/NOVORA.Tests/Test/NLTestExInModes.cs
git commit -m "feat: redesign Android ExIn and Vision controls"
```

### Task 5: Redesign Files, Multimedia, QR, And Auxiliary Activities

**Files:**
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIFilesActivity.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIShareActivity.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIQrActivity.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingSettingsActivity.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIUsbBootstrapActivity.cs`
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIFilesPage.cs`
- Create: `src/NOVORA.Android/AndroidUI/NLAndroidUIMultimediaPage.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs`

**Interfaces:**
- Consumes: existing storage, share target, media snapshot, QR invitation, bootstrap, and overlay preference APIs.
- Produces: visually unified auxiliary workflows with unchanged transport behavior.

- [ ] **Step 1: Recompose Files and Multimedia main pages**

Files exposes Received and Sent with real empty/progress states. Multimedia exposes real audio routes, recording, screenshot, and an optional floating-control section.

- [ ] **Step 2: Restyle auxiliary activities**

Apply the shared app bar, palette, typography, spacing, and command buttons. Preserve activity names, intent extras, permissions, and service binding.

- [ ] **Step 3: Enforce master overlay-toggle behavior**

When overlay is disabled, opacity, position, favorites, and appearance controls are hidden. The setting change only updates overlay preferences and never sends `exin.*`.

- [ ] **Step 4: Compile and commit**

Run Android compile, then:
```powershell
git add src/NOVORA.Android/AndroidUI/NLAndroidUIActivity.cs src/NOVORA.Android/AndroidUI/NLAndroidUIFilesActivity.cs src/NOVORA.Android/AndroidUI/NLAndroidUIShareActivity.cs src/NOVORA.Android/AndroidUI/NLAndroidUIQrActivity.cs src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingSettingsActivity.cs src/NOVORA.Android/AndroidUI/NLAndroidUIUsbBootstrapActivity.cs src/NOVORA.Android/AndroidUI/NLAndroidUIFilesPage.cs src/NOVORA.Android/AndroidUI/NLAndroidUIMultimediaPage.cs
git commit -m "feat: redesign Android files and auxiliary flows"
```

### Task 6: Redesign And Verify The Optional Floating Control

**Files:**
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingController.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingInline.cs`
- Modify: `src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingPreferences.cs`
- Test: `tests/NOVORA.Tests/Test/NLTestControlFloatingDockState.cs`

**Interfaces:**
- Consumes: shared palette/components, overlay preferences, and session snapshot.
- Produces: 56 dp folded bubble and compact expanded panel with no engine ownership.

- [ ] **Step 1: Add policy coverage for overlay-only removal**

Keep docking tests and add a pure policy assertion that removal has no engine command:
```csharp
[Fact]
public void Removing_overlay_has_no_engine_side_effect()
{
    Assert.Null(NLControlFloatingDockState.CommandOnRemoval);
}
```
Add `public static string? CommandOnRemoval => null;` to make the contract explicit and testable.

- [ ] **Step 2: Restyle folded and expanded states**

Keep stable 56 dp dimensions, configurable opacity, edge docking, capture visibility, and existing actions. Use compact rows and shared status tones; do not add nested cards.

- [ ] **Step 3: Verify no hidden ExIn mutation path exists**

Run:
```powershell
rg -n "exin\.mode|exin\.reactivate" src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingController.cs
```
Expected: matches only explicit buttons inside the visible expanded panel; no match in hide, remove, preference-change, or dispose methods.

- [ ] **Step 4: Run focused tests, compile, and commit**

```powershell
dotnet test tests/NOVORA.Tests/NLProjectTests.csproj -c Release --no-restore --filter "FullyQualifiedName~NLTestControlFloatingDockState"
dotnet build src/NOVORA.Android/NLProjectAndroid.csproj -c Release --no-restore -t:Compile
git add src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingController.cs src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingInline.cs src/NOVORA.Android/AndroidUI/NLAndroidUIFloatingPreferences.cs src/NOVORA/Control/NLControlFloatingDockState.cs tests/NOVORA.Tests/Test/NLTestControlFloatingDockState.cs
git commit -m "feat: redesign optional Android floating control"
```

### Task 7: Package, Inspect, And Physically Validate The Redesigned APK

**Files:**
- Modify: `src/NOVORA.Android/NLProjectAndroid.csproj`
- Modify: `src/NOVORA/Android/NLAndroidApp.apk`
- Modify: `src/NOVORA/Android/NLAndroidRelease.json`
- Modify: `tests/NOVORA.Tests/Test/NLTestOfficialRelease.cs`
- Create: `Documentation/NLDocumentationAndroidVisual1428Evidence.json`

**Interfaces:**
- Consumes: completed UI, release packaging target, canonical package loader, attached Samsung Android device.
- Produces: signed canonical APK with binary-manifest/hash evidence and explicit physical results.

- [ ] **Step 1: Advance Android release metadata once**

Set `ApplicationVersion` to `28` and `ApplicationDisplayVersion` to `1.4.28`. Update `NLTestOfficialRelease` expected values. Do not increment again for rebuild-only attempts.

- [ ] **Step 2: Run complete automated verification**

```powershell
dotnet build src/NOVORA/NLProjectDesktop.csproj -c Release --no-restore
dotnet build src/NOVORA.Android/NLProjectAndroid.csproj -c Release --no-restore -t:Compile
dotnet test NOVORA.sln -c Release --no-restore
```
Expected: both builds have 0 warnings/errors and all tests pass.

- [ ] **Step 3: Build and inspect the signed APK**

```powershell
dotnet build src/NOVORA.Android/NLProjectAndroid.csproj -c Release --no-restore -t:SignAndroidPackage -p:NovoraPackage=true
& 'C:\Program Files (x86)\Android\android-sdk\build-tools\36.0.0\aapt2.exe' dump badging src\NOVORA.Android\bin\Release\net10.0-android\com.novora.appcontrol-Signed.apk | Select-String '^package:|^sdkVersion:|^targetSdkVersion:'
```
Expected: package `com.novora.appcontrol`, code 28/name 1.4.28, minSdk 26, targetSdk 36.

- [ ] **Step 4: Synchronize canonical package atomically**

Copy the signed APK to `src/NOVORA/Android/NLAndroidApp.apk`, calculate SHA-256, and update `NLAndroidRelease.json`. Reload it through `NLServiceAndroidPackage.Load` via `NLTestOfficialRelease`.

- [ ] **Step 5: Install and capture physical evidence**

Resolve the physical USB serial from `adb devices -l` by selecting the connected serial without `:`, then run:
```powershell
$serial = (& .\src\NOVORA\Tools\adb.exe devices) |
    Select-String "`tdevice$" |
    ForEach-Object { ($_.Line -split "`t")[0] } |
    Where-Object { $_ -notmatch ':' } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($serial)) { throw 'No hay un Android físico USB autorizado.' }
& .\src\NOVORA\Tools\adb.exe -s $serial install -r .\src\NOVORA\Android\NLAndroidApp.apk
& .\src\NOVORA\Tools\adb.exe -s $serial shell am start -W -n com.novora.appcontrol/.MainActivity
```
Inspect dark/light Home, Connect, Engines, ExIn, Files, Multimedia, Settings, QR, auxiliary activities, and folded/expanded overlay. Repeat with increased font scale and overlay disabled. Record actual pass/fail values; never prefill success.

- [ ] **Step 6: Validate engine workflows physically**

Confirm USB and LAN discovery, VE start/stop, ExIn Game/UI with VE fullscreen, ExIn without bubble, files, capture/recording, theme persistence, reconnect, and independent engine failures. Mark anything untested as pending.

- [ ] **Step 7: Write evidence and clean generated residue**

Write exact hashes, manifest values, build/test counts, device serial, and physical outcomes to `Documentation/NLDocumentationAndroidVisual1428Evidence.json`. Remove generated `bin`, `obj`, and temporary `artifacts` directories only after verifying their resolved paths remain inside the repository.

- [ ] **Step 8: Final commit and publication**

```powershell
git add src/NOVORA.Android/NLProjectAndroid.csproj src/NOVORA/Android/NLAndroidApp.apk src/NOVORA/Android/NLAndroidRelease.json tests/NOVORA.Tests/Test/NLTestOfficialRelease.cs Documentation/NLDocumentationAndroidVisual1428Evidence.json
git commit -m "release: package Android visual refresh 1.4.28"
git push origin NOVORA-LINK
```
