# NOVORA Engine Naming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Normalize LinkEngine C# naming and add the non-rendering VisionEngine Core foundation.

**Architecture:** Preserve current LinkEngine behavior while flattening redundant C# directories and renaming each file's primary type to match `Function + Folder + Engine`. Add a small VisionEngine Core lifecycle that intentionally has no renderer or image presentation.

**Tech Stack:** C#/.NET 8 WPF host, .NET Android companion, Rust native LinkEngine relay.

**Spec:** `docs/superpowers/specs/2026-09-04-engine-naming-design.md`

## Global Constraints

- C# engine naming: `<Function><Folder><Engine>.cs`.
- `LE` = LinkEngine; `VE` = VisionEngine.
- Primary type name equals filename.
- Do not change LinkEngine wire protocol or runtime behavior.
- Do not rename Android package id `com.novora.linkengine`.
- Do not rename Rust relay modules in this pass.
- Do not implement VisionEngine rendering or image presentation.

---

### Task 1: Add structural naming validation

**Files:**
- Create: `scripts/validate_engine_naming.py`

**Interfaces:**
- Consumes: repository root structure.
- Produces: exit code 0 only when required normalized files/types exist and redundant C# function subfolders are gone.

- [ ] Write structural validation for normalized LinkEngine and VisionEngine Core.
- [ ] Run it before normalization and confirm it fails.
- [ ] Keep it as a repository validation utility.

### Task 2: Normalize Windows LinkEngine C# layout and primary types

**Files:**
- Modify/move: `src/NOVORA/LinkEngine/**/*.cs`
- Modify references: `src/NOVORA/**/*.cs`, `tests/NOVORA.Tests/**/*.cs`

**Interfaces:**
- Consumes: existing LinkEngine public methods and behavior.
- Produces: same behavior with normalized type/file names.

- [ ] Flatten redundant C# function directories.
- [ ] Rename primary LinkEngine types to file names.
- [ ] Update all C# references atomically.
- [ ] Run structural validation.

### Task 3: Normalize Android LinkEngine C# layout and primary types

**Files:**
- Modify/move: `NOVORA.linkEngine.Android/LinkEngine/**/*.cs`
- Modify references: `NOVORA.linkEngine.Android/**/*.cs`

**Interfaces:**
- Consumes: current Android VPN/control/data behavior.
- Produces: same behavior under normalized names; package id unchanged.

- [ ] Flatten redundant C# function directories.
- [ ] Rename primary Android LinkEngine types to file names.
- [ ] Update Android references.
- [ ] Run structural validation.

### Task 4: Add VisionEngine Core without renderer

**Files:**
- Create: `src/NOVORA/VisionEngine/Core/StatesCoreVE.cs`
- Create: `src/NOVORA/VisionEngine/Core/ResultCoreVE.cs`
- Create: `src/NOVORA/VisionEngine/Core/StatusCoreVE.cs`
- Create: `src/NOVORA/VisionEngine/Core/SessionCoreVE.cs`
- Create: `src/NOVORA/VisionEngine/Core/EngineCoreVE.cs`

**Interfaces:**
- Produces: `StatesCoreVE`, `ResultCoreVE`, `StatusCoreVE`, `SessionCoreVE`, `EngineCoreVE`.

- [ ] Add state/result/status/session types.
- [ ] Add lifecycle-only engine with initialize/start/stop/dispose.
- [ ] Ensure no renderer, capture, decoder, or image-window dependency exists.
- [ ] Run structural validation.

### Task 5: Package and report verification

**Files:**
- Create: `ENGINE_NAMING_REPORT.md`

**Interfaces:**
- Produces: migration map, verification result, and Windows build commands.

- [ ] Scan for stale old type names and redundant directories.
- [ ] Run structural validation to green.
- [ ] Record that .NET compilation could not be run in this Linux sandbox if `dotnet` is unavailable.
- [ ] Zip the clean normalized folder.
