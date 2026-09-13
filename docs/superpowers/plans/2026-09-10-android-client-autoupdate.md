# Android Client Auto-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hacer que NOVORA para Windows instale o actualice automáticamente el cliente NOVORA-LINK Android, resuelva su Activity launcher real, lo abra con el token de sesión y sólo entonces espere HELLO/ACK.

**Architecture:** La responsabilidad se encapsula en `NOVORA.LinkEngine.Android.ManagerAndroidClientLE`, que reutiliza `AdbService` y lee un manifiesto de release junto al APK. `ManagerRuntimeLE` invoca ese componente después de verificar el transporte y antes del handshake. El release genera `Tools\Android\NOVORA.LinkEngine.Android.manifest.json` junto al APK; Runtime nunca depende de Android SDK, Visual Studio, `aapt` ni `apkanalyzer`.

**Tech Stack:** C#/.NET 8 WPF, .NET for Android, ADB, xUnit, PowerShell de release, SHA-256, JSON con `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-09-10-android-client-autoupdate-design.md`

## Global Constraints

- Package/ApplicationId Android: `com.novora.linkengine`.
- APK distribuido: `src/NOVORA/Tools/Android/NOVORA.LinkEngine.Android.apk`.
- Manifiesto distribuido: `src/NOVORA/Tools/Android/NOVORA.LinkEngine.Android.manifest.json`.
- Extra de Intent: `novora_remote_token`.
- `versionCode` decide instalación/actualización; `versionName` es informativo.
- No downgrade automático.
- El APK debe validarse por SHA-256 antes de instalarse.
- El token de sesión nunca se persiste ni se incluye en logs, mensajes de error o diagnósticos.
- `AdbService` sigue siendo el único ejecutor de `adb.exe`.
- No se reinstala el APK cuando la versión instalada es igual o superior.
- El flujo normal no puede requerir PowerShell, Visual Studio ni ADB manual.
- La rama GitHub visible todavía no contiene los archivos LinkEngine/Android más nuevos que existen en el árbol local del usuario; al ejecutar el plan se debe trabajar sobre el árbol local actual y no reemplazarlo por una versión anterior del repositorio remoto.

---

### Task 1: Modelo de manifiesto, rutas y política de actualización

**Files:**
- Create: `src/NOVORA/LinkEngine/Android/AndroidClientManifestLE.cs`
- Create: `src/NOVORA/LinkEngine/Android/AndroidClientUpdatePolicyLE.cs`
- Modify: `src/NOVORA/Services/NovoraPaths.cs`
- Test: `tests/NOVORA.Tests/AndroidClientUpdatePolicyTests.cs`

**Interfaces:**
- Consumes: `NovoraPaths.BaseDirectory`, `NovoraPaths.ToolsDirectory`.
- Produces: `AndroidClientManifestLE`, `AndroidClientUpdateDecisionLE`, `AndroidClientUpdatePolicyLE.Decide(long? installedVersionCode, long bundledVersionCode)`, `NovoraPaths.AndroidClientApk`, `NovoraPaths.AndroidClientManifest`.

- [ ] **Step 1: Write the failing policy tests**

```csharp
using NOVORA.LinkEngine.Android;
using Xunit;

namespace NOVORA.Tests;

public sealed class AndroidClientUpdatePolicyTests
{
    [Fact]
    public void Missing_package_requires_install()
    {
        Assert.Equal(
            AndroidClientUpdateDecisionLE.Install,
            AndroidClientUpdatePolicyLE.Decide(null, 10));
    }

    [Fact]
    public void Older_package_requires_update()
    {
        Assert.Equal(
            AndroidClientUpdateDecisionLE.Update,
            AndroidClientUpdatePolicyLE.Decide(9, 10));
    }

    [Fact]
    public void Equal_package_does_not_reinstall()
    {
        Assert.Equal(
            AndroidClientUpdateDecisionLE.Keep,
            AndroidClientUpdatePolicyLE.Decide(10, 10));
    }

    [Fact]
    public void Newer_installed_package_does_not_downgrade()
    {
        Assert.Equal(
            AndroidClientUpdateDecisionLE.Keep,
            AndroidClientUpdatePolicyLE.Decide(11, 10));
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release --filter AndroidClientUpdatePolicyTests
```

Expected: FAIL because `NOVORA.LinkEngine.Android` policy types do not exist yet.

- [ ] **Step 3: Implement the manifest and policy types**

```csharp
namespace NOVORA.LinkEngine.Android;

public sealed record AndroidClientManifestLE(
    string PackageName,
    long VersionCode,
    string VersionName,
    string Sha256);

public enum AndroidClientUpdateDecisionLE
{
    Install,
    Update,
    Keep
}

public static class AndroidClientUpdatePolicyLE
{
    public static AndroidClientUpdateDecisionLE Decide(
        long? installedVersionCode,
        long bundledVersionCode)
    {
        if (bundledVersionCode < 1)
            throw new ArgumentOutOfRangeException(nameof(bundledVersionCode));

        if (installedVersionCode is null)
            return AndroidClientUpdateDecisionLE.Install;

        return installedVersionCode.Value < bundledVersionCode
            ? AndroidClientUpdateDecisionLE.Update
            : AndroidClientUpdateDecisionLE.Keep;
    }
}
```

Add to `NovoraPaths`:

```csharp
public string AndroidClientDirectory =>
    Path.Combine(ToolsDirectory, "Android");

public string AndroidClientApk =>
    Path.Combine(AndroidClientDirectory, "NOVORA.LinkEngine.Android.apk");

public string AndroidClientManifest =>
    Path.Combine(AndroidClientDirectory, "NOVORA.LinkEngine.Android.manifest.json");
```

- [ ] **Step 4: Run the policy tests again**

Run:

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release --filter AndroidClientUpdatePolicyTests
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/NOVORA/LinkEngine/Android/AndroidClientManifestLE.cs src/NOVORA/LinkEngine/Android/AndroidClientUpdatePolicyLE.cs src/NOVORA/Services/NovoraPaths.cs tests/NOVORA.Tests/AndroidClientUpdatePolicyTests.cs
git commit -m "feat: add Android client update policy"
```

---

### Task 2: Helpers ADB para paquete, launcher y lanzamiento seguro

**Files:**
- Modify: `src/NOVORA/Services/AdbService.cs`
- Test: `tests/NOVORA.Tests/RegressionTests.cs`

**Interfaces:**
- Consumes: `AdbService.ShellAsync`, `AdbService.InstallAsync`, `AdbService.IsDeviceOnlineAsync`.
- Produces:
  - `Task<long?> GetInstalledVersionCodeAsync(string serial, string packageName, CancellationToken cancellationToken = default)`
  - `Task<string?> ResolveLauncherActivityAsync(string serial, string packageName, CancellationToken cancellationToken = default)`
  - `Task<string> LaunchActivityAsync(string serial, string componentName, string extraName, string extraValue, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Add failing reflection regression tests for the helper surface**

Append to `RegressionTests.cs`:

```csharp
[Fact]
public void Adb_exposes_android_client_package_helpers()
{
    var type = typeof(AdbService);

    Assert.NotNull(type.GetMethod("GetInstalledVersionCodeAsync"));
    Assert.NotNull(type.GetMethod("ResolveLauncherActivityAsync"));
    Assert.NotNull(type.GetMethod("LaunchActivityAsync"));
}
```

- [ ] **Step 2: Run the targeted regression test and verify it fails**

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release --filter Adb_exposes_android_client_package_helpers
```

Expected: FAIL because the helper methods do not exist.

- [ ] **Step 3: Implement version query parsing**

Add a helper that executes:

```text
dumpsys package 'com.novora.linkengine'
```

Parse `versionCode=` with a compiled/static regex equivalent to:

```regex
\bversionCode=(\d+)\b
```

Contract:

```csharp
public async Task<long?> GetInstalledVersionCodeAsync(
    string serial,
    string packageName,
    CancellationToken cancellationToken = default)
```

Rules:
- validate non-empty serial/package;
- if Package Manager clearly reports the package does not exist, return `null`;
- if output names the package but contains no parseable `versionCode`, throw `InvalidOperationException("ANDROID CLIENT: PACKAGE QUERY FAILED")` with sanitized diagnostic context;
- preserve `OperationCanceledException`.

- [ ] **Step 4: Implement launcher resolution**

Execute via existing `ShellAsync`:

```text
cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER 'com.novora.linkengine'
```

Contract:

```csharp
public async Task<string?> ResolveLauncherActivityAsync(
    string serial,
    string packageName,
    CancellationToken cancellationToken = default)
```

Accept only a trimmed line containing `/` whose package prefix matches the requested package. Return `null` for `No activity found`, empty output, or an invalid component.

- [ ] **Step 5: Implement activity launch without exposing the token**

Contract:

```csharp
public Task<string> LaunchActivityAsync(
    string serial,
    string componentName,
    string extraName,
    string extraValue,
    CancellationToken cancellationToken = default)
```

Build the shell command internally:

```text
am start -W -n '<component>' --es '<extraName>' '<extraValue>'
```

Use a private shell-quote helper that escapes single quotes. Do not include the resulting command in exceptions or logs because it contains the session token. If `ShellAsync` throws, rethrow a sanitized `InvalidOperationException("ANDROID CLIENT: LAUNCH FAILED")` with the original exception as `InnerException`.

- [ ] **Step 6: Re-run the helper regression test**

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release --filter Adb_exposes_android_client_package_helpers
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/NOVORA/Services/AdbService.cs tests/NOVORA.Tests/RegressionTests.cs
git commit -m "feat: add Android package adb helpers"
```

---

### Task 3: ManagerAndroidClientLE

**Files:**
- Create: `src/NOVORA/LinkEngine/Android/ManagerAndroidClientLE.cs`
- Test: `tests/NOVORA.Tests/AndroidClientManifestTests.cs`
- Test: `tests/NOVORA.Tests/RegressionTests.cs`

**Interfaces:**
- Consumes: `AdbService`, `NovoraPaths`, `AndroidClientUpdatePolicyLE`, `AndroidClientManifestLE`.
- Produces:

```csharp
public sealed class ManagerAndroidClientLE
{
    public ManagerAndroidClientLE(AdbService adb, NovoraPaths paths);

    public Task<ResultCoreLE> EnsureReadyAndLaunchAsync(
        string serial,
        string sessionToken,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write failing manifest validation tests**

Create `AndroidClientManifestTests.cs` around a public/static parser/validator owned by `ManagerAndroidClientLE` or a focused helper `AndroidClientManifestReaderLE`. Cover:

```csharp
[Fact]
public void Manifest_rejects_wrong_package_name() { /* packageName != com.novora.linkengine */ }

[Fact]
public void Manifest_rejects_zero_version_code() { /* versionCode = 0 */ }

[Fact]
public void Manifest_rejects_malformed_sha256() { /* not 64 hex chars */ }
```

Use temporary files; do not invoke ADB.

- [ ] **Step 2: Run the manifest tests and verify they fail**

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release --filter AndroidClientManifestTests
```

Expected: FAIL because the manifest reader/manager does not exist.

- [ ] **Step 3: Implement manifest loading and SHA-256 validation**

`ManagerAndroidClientLE` must:

1. Check `_paths.AndroidClientApk` exists; otherwise return `ResultCoreLE.Fail("ANDROID CLIENT: APK MISSING")`.
2. Check `_paths.AndroidClientManifest` exists and deserialize with `System.Text.Json`.
3. Require `PackageName == "com.novora.linkengine"`, `VersionCode >= 1`, non-empty `VersionName`, and 64 hexadecimal SHA-256 characters.
4. Compute `SHA256.HashData` over the APK and compare lowercase/uppercase invariantly.
5. On invalid manifest/hash, return `ResultCoreLE.Fail("ANDROID CLIENT: MANIFEST INVALID")`.

- [ ] **Step 4: Implement install/update decision and ADB guards**

Inside `EnsureReadyAndLaunchAsync`:

```csharp
if (!await _adb.IsDeviceOnlineAsync(serial, cancellationToken))
    return ResultCoreLE.Fail("ADB OFFLINE. No se puede preparar NOVORA-LINK Android.");

long? installed = await _adb.GetInstalledVersionCodeAsync(
    serial,
    manifest.PackageName,
    cancellationToken);

AndroidClientUpdateDecisionLE decision =
    AndroidClientUpdatePolicyLE.Decide(installed, manifest.VersionCode);
```

For `Install` or `Update`, call existing:

```csharp
await _adb.InstallAsync(serial, _paths.AndroidClientApk, cancellationToken);
```

Require output containing `Success` case-insensitively. Otherwise return `ANDROID CLIENT: INSTALL FAILED` without leaking token data.

After install/update, query `versionCode` again and require `installedVersionCode >= manifest.VersionCode`.

- [ ] **Step 5: Implement launcher resolution and launch**

```csharp
string? component = await _adb.ResolveLauncherActivityAsync(
    serial,
    manifest.PackageName,
    cancellationToken);

if (string.IsNullOrWhiteSpace(component))
    return ResultCoreLE.Fail("ANDROID CLIENT: LAUNCHER NOT FOUND");

await _adb.LaunchActivityAsync(
    serial,
    component,
    "novora_remote_token",
    sessionToken,
    cancellationToken);
```

Treat output containing `Error type 3`, `Error:`, `Exception`, or non-successful launch status as `ANDROID CLIENT: LAUNCH FAILED`.

Return:

```csharp
return ResultCoreLE.Ok("ANDROID CLIENT: READY");
```

Do not return or log `sessionToken`.

- [ ] **Step 6: Add a regression test that the manager type/method exists**

Append:

```csharp
[Fact]
public void Android_client_manager_exposes_ready_and_launch_flow()
{
    var type = typeof(MainWindow).Assembly.GetType(
        "NOVORA.LinkEngine.Android.ManagerAndroidClientLE");

    Assert.NotNull(type);
    Assert.NotNull(type!.GetMethod("EnsureReadyAndLaunchAsync"));
}
```

- [ ] **Step 7: Run targeted tests**

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release --filter "AndroidClientManifestTests|Android_client_manager_exposes_ready_and_launch_flow"
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/NOVORA/LinkEngine/Android/ManagerAndroidClientLE.cs tests/NOVORA.Tests/AndroidClientManifestTests.cs tests/NOVORA.Tests/RegressionTests.cs
git commit -m "feat: manage Android client lifecycle"
```

---

### Task 4: Integrar el cliente Android en ManagerRuntimeLE antes de HELLO/ACK

**Files:**
- Modify: `src/NOVORA/LinkEngine/Runtime/ManagerRuntimeLE.cs`
- Test: `tests/NOVORA.Tests/RegressionTests.cs`

**Interfaces:**
- Consumes: `ManagerAndroidClientLE.EnsureReadyAndLaunchAsync`.
- Produces: Runtime inicia el cliente Android antes de `WaitForHandshakeAsync` y falla inmediatamente si preparación/lanzamiento falla.

- [ ] **Step 1: Add a failing structural regression test**

Add a test that reflects the private field or constructor dependency introduced for `ManagerAndroidClientLE`:

```csharp
[Fact]
public void Runtime_owns_android_client_manager()
{
    var runtimeType = typeof(MainWindow).Assembly.GetType(
        "NOVORA.LinkEngine.Runtime.ManagerRuntimeLE");
    Assert.NotNull(runtimeType);

    var field = runtimeType!.GetField(
        "_androidClient",
        BindingFlags.Instance | BindingFlags.NonPublic);

    Assert.NotNull(field);
    Assert.Equal(
        "ManagerAndroidClientLE",
        field!.FieldType.Name);
}
```

- [ ] **Step 2: Run the targeted test and verify it fails**

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release --filter Runtime_owns_android_client_manager
```

Expected: FAIL because `_androidClient` does not exist.

- [ ] **Step 3: Wire the manager into Runtime**

Add:

```csharp
using NOVORA.LinkEngine.Android;
```

Add field:

```csharp
private readonly ManagerAndroidClientLE _androidClient;
```

Construct it from the existing `AdbService` plus the same `NovoraPaths` used by ADB. If `AdbService` does not expose its paths, inject `NovoraPaths` into `ManagerRuntimeLE` from its composition root rather than constructing a second path root from an unrelated directory.

- [ ] **Step 4: Insert the client stage after transport verification**

Immediately after successful `engine.Transport.VerifyAsync(...)` and before `WaitForHandshakeAsync(...)`:

```csharp
UpdateRuntimeStateLE(
    StateRuntimeLE.WaitingForAndroid,
    "Preparando NOVORA-LINK Android.");

ResultCoreLE androidClient =
    await _androidClient
        .EnsureReadyAndLaunchAsync(
            serial,
            /* current per-session remote token */,
            cancellationToken)
        .ConfigureAwait(false);

if (!androidClient.Success)
{
    return await FailStartLEAsync(
            serial,
            androidClient.Message)
        .ConfigureAwait(false);
}

UpdateRuntimeStateLE(
    StateRuntimeLE.WaitingForAndroid,
    "Esperando cliente Android HELLO/ACK.");
```

Use the exact token already used by the existing Android remote launch path. Do not generate a second token for the same transport session.

- [ ] **Step 5: Remove the old hardcoded Android launch path**

Search the local current tree:

```powershell
Get-ChildItem . -Recurse -Filter *.cs |
    Select-String -Pattern 'am start|Error type 3|novora_remote_token|com\.novora\.linkengine\.MainActivity'
```

Any previous direct `am start -n ...MainActivity` path must either be deleted or routed through `ManagerAndroidClientLE`; there must be only one owner of the Android client launch.

- [ ] **Step 6: Re-run Runtime regression tests**

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release --filter Runtime_owns_android_client_manager
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/NOVORA/LinkEngine/Runtime/ManagerRuntimeLE.cs tests/NOVORA.Tests/RegressionTests.cs
git commit -m "feat: launch Android client before handshake"
```

---

### Task 5: Generar y empaquetar APK + manifiesto durante Release

**Files:**
- Create: `scripts/Prepare-AndroidClient.ps1`
- Create: `src/NOVORA/Tools/Android/.gitkeep` if the folder needs to exist without checking in release binaries
- Modify: release documentation/script that currently publishes NOVORA; if no release orchestrator exists, `Prepare-AndroidClient.ps1` becomes the required pre-publish step documented in `README.md` or release notes.
- Verify: `src/NOVORA/NOVORA.csproj`
- Verify: `Installer/NOVORA.Installer.iss`

**Interfaces:**
- Consumes: Android Release APK produced under `NOVORA.linkEngine.Android\bin\Release\net10.0-android\`.
- Produces:
  - `src/NOVORA/Tools/Android/NOVORA.LinkEngine.Android.apk`
  - `src/NOVORA/Tools/Android/NOVORA.LinkEngine.Android.manifest.json`

- [ ] **Step 1: Write the preparation script with explicit inputs**

`Prepare-AndroidClient.ps1` parameters:

```powershell
param(
    [Parameter(Mandatory = $true)]
    [string]$ApkPath,

    [Parameter(Mandatory = $true)]
    [long]$VersionCode,

    [Parameter(Mandatory = $true)]
    [string]$VersionName,

    [string]$DestinationDirectory =
        (Join-Path $PSScriptRoot "..\src\NOVORA\Tools\Android")
)
```

The script must:
- fail if APK is absent;
- require `VersionCode >= 1`;
- create destination directory;
- copy APK to the canonical filename;
- compute `Get-FileHash -Algorithm SHA256`;
- write JSON with `packageName`, `versionCode`, `versionName`, `sha256` using `ConvertTo-Json`;
- print the destination paths but not any session/runtime secrets.

- [ ] **Step 2: Run the script against the freshly built Android Release APK**

Example:

```powershell
.\scripts\Prepare-AndroidClient.ps1 `
  -ApkPath ".\NOVORA.linkEngine.Android\bin\Release\net10.0-android\com.novora.linkengine-Signed.apk" `
  -VersionCode 1 `
  -VersionName "1.0.0"
```

Expected: canonical APK and manifest appear under `src\NOVORA\Tools\Android`.

- [ ] **Step 3: Verify Windows build copies both artifacts**

`NOVORA.csproj` already includes:

```xml
<Content Include="Tools\**\*">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

Build:

```powershell
dotnet build .\src\NOVORA\NOVORA.csproj -c Release
```

Verify:

```powershell
Test-Path .\src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\Tools\Android\NOVORA.LinkEngine.Android.apk
Test-Path .\src\NOVORA\bin\Release\net8.0-windows10.0.26100.0\Tools\Android\NOVORA.LinkEngine.Android.manifest.json
```

Expected: both `True`.

- [ ] **Step 4: Verify installer recursion instead of adding duplicate file rules**

The existing Inno Setup `[Files]` rule recursively copies `{#PublishDir}\*`. Do not modify `Installer/NOVORA.Installer.iss` unless the publish verification proves `Tools\Android` is absent.

- [ ] **Step 5: Commit the release preparation changes**

```bash
git add scripts/Prepare-AndroidClient.ps1 src/NOVORA/Tools/Android README.md
git commit -m "build: package Android client with NOVORA"
```

Do not commit a transient APK binary if the repository policy intentionally excludes release binaries; if so, the release pipeline must still place it into the publish directory before packaging.

---

### Task 6: Full verification on Galaxy/authorized Android and regression suite

**Files:**
- Modify only if verification exposes a defect in Tasks 1-5.

**Interfaces:**
- Consumes: complete Android-client lifecycle.
- Produces: evidence that NOVORA no longer requires manual deployment and does not hit `Error type 3` for a valid packaged client.

- [ ] **Step 1: Run the full Windows test suite**

```powershell
dotnet test .\tests\NOVORA.Tests\NOVORA.Tests.csproj -c Release
```

Expected: all tests PASS.

- [ ] **Step 2: Build Windows Release**

```powershell
dotnet build .\src\NOVORA\NOVORA.csproj -c Release
```

Expected: 0 errors.

- [ ] **Step 3: Build Android Release**

```powershell
dotnet build .\NOVORA.linkEngine.Android\NOVORA.linkEngine.Android.csproj -c Release
```

Expected: 0 errors and a signed/installable APK is produced by the Android project configuration.

- [ ] **Step 4: Test missing-package flow**

On an authorized test device, remove the Android client once as test setup. Start the normal NOVORA connection flow. Expected sequence in NOVORA: `ANDROID CLIENT: INSTALLING` -> `ANDROID CLIENT: READY` -> HELLO/ACK -> LinkEngine healthy. No PowerShell or Visual Studio deployment is part of the product flow.

- [ ] **Step 5: Test upgrade flow**

Install an older `versionCode`, then run a NOVORA build containing a newer manifest/APK. Expected: `ANDROID CLIENT: UPDATING`, `adb install -r` succeeds, app data remains, launcher resolves, HELLO/ACK completes.

- [ ] **Step 6: Test no-reinstall and no-downgrade flows**

Same version: verify no install occurs. Higher installed version: verify no downgrade occurs. Both cases must still launch and handshake.

- [ ] **Step 7: Test immediate failure cases**

Temporarily remove the bundled APK, corrupt its manifest hash, and test a package with no resolvable launcher. Expected: precise `ANDROID CLIENT:*` error immediately; Runtime must not wait the 30-second HELLO timeout.

- [ ] **Step 8: Verify token privacy**

Search logs/output for the exact session token used in a test run. Expected: zero matches outside the transient ADB command invocation path; UI/error text contains no token.

- [ ] **Step 9: Verify publish payload**

After `dotnet publish`, confirm both Android artifacts exist under published `Tools\Android`. Build the installer and inspect/extract it if needed to confirm the same files are included.

- [ ] **Step 10: Final commit for verification-only fixes**

Only if verification required code changes:

```bash
git add <only-files-changed-to-fix-verification>
git commit -m "fix: harden Android client auto-update flow"
```

## Plan Self-Review

- Spec coverage: install, update, same-version, no-downgrade, SHA-256, package query, launcher resolution, token privacy, Runtime ordering, publish payload and installer recursion are all assigned to concrete tasks.
- Placeholder scan: no `TBD`, `TODO` or unspecified implementation step remains. The one local integration point intentionally says to use the existing per-session token because its exact field/member is present only in the user's newer local LinkEngine tree, not the currently visible GitHub branch; execution must resolve that symbol from the local source before editing.
- Type consistency: `ManagerAndroidClientLE.EnsureReadyAndLaunchAsync`, `AndroidClientUpdatePolicyLE.Decide`, `GetInstalledVersionCodeAsync`, `ResolveLauncherActivityAsync`, and `LaunchActivityAsync` use the same signatures throughout the plan.
