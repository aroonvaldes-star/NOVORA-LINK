# NOVORA Engine Naming Design

## Goal

Normalize LinkEngine and establish VisionEngine using the global NOVORA engine naming rule:

`<Function><Folder><Engine>.cs`

where `LE` means LinkEngine and `VE` means VisionEngine.

## Rules

1. C# engine files use `Function + logical folder + engine suffix`.
2. The primary class, record, enum, or interface in a file has the same name as the file.
3. Engine root folders do not include the suffix: `LinkEngine/Core`, not `CoreLE`.
4. Redundant one-file function subfolders are removed. Example: `Recovery/Monitor/MonitorRecoveryLE.cs` becomes `Recovery/MonitorRecoveryLE.cs`.
5. Namespaces stay at the logical folder boundary: `NOVORA.LinkEngine.Recovery`, `NOVORA.VisionEngine.Core`.
6. Rust relay sources keep idiomatic Rust module names and are excluded from the C# naming rule.
7. This pass must not change LinkEngine behavior or protocol wire formats.
8. VisionEngine is code-first. Renderer/image presentation is not implemented in this pass.

## LinkEngine normalization

The existing LinkEngine C# implementation is flattened by logical subsystem and its primary types are renamed to match their files. Existing behavior, method names, ports, protocol payloads, ADB flow, recovery logic, and native relay source are preserved.

## VisionEngine foundation

Create the VisionEngine root and its initial Core types only:

- `Core/StatesCoreVE.cs`
- `Core/ResultCoreVE.cs`
- `Core/StatusCoreVE.cs`
- `Core/SessionCoreVE.cs`
- `Core/EngineCoreVE.cs`

The initial engine is lifecycle/state infrastructure only. It does not capture, decode, render, or display images.

## Android LinkEngine

The Android LinkEngine C# files follow the same flattening and primary-type naming rule. Android application id and package identity remain unchanged.
