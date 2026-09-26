# NOVORA General Independence and Audit Execution Plan

**Spec:** `output/pdf/NOVORA-LINK_Guia_Maestra_Independencia_Auditoria_v1.2_Complemento.pdf`

**Goal:** Audit and evolve all NOVORA surfaces toward evidence-backed engine independence without sacrificing the current functional baseline.

## Global constraints

- Preserve the current dirty working tree as part of the baseline. Never revert unknown changes.
- One active Android phone per PC is the current operating policy until an ADR changes it.
- Prefer events, callbacks, readiness and on-demand snapshots. Do not add informational polling.
- Each engine owns its lifecycle and Recovery. Congestion, WouldBlock and normal backpressure are not Recovery failures.
- STEngine observation is passive and must not mutate other engines.
- Keep NVIDIA optional and preserve AMD, Intel and software fallback.
- Do not create a replacement Android Agent or remove scrcpy before the Android and parity gates pass.
- Do not publish, push, install, terminate external processes or modify a live device without separate authorization and evidence controls.
- Use TDD for every production behavior change.

## Task 1: Preserve baseline and build the discovery package

Produce reproducible repository, environment, project, dependency, process, ADB, scrcpy, storage and runtime inventories under `Documentation/Audit/2026-09-23/`. Record detections without treating them as confirmed findings.

**Verification:** every declared artifact exists, parses, records its command/source and contains the baseline commit plus dirty-state identity.

## Task 2: Triage findings and document current architecture

Convert detections into `NOV-AUD-XXXX` records only after tracing callers, owners and runtime consequences. Produce dependency, ownership, runtime, Android and storage graphs.

**Verification:** each confirmed finding links to direct source or executable evidence; unsupported inferences remain DETECTED.

## Task 3: Approve ADR pack

Create ADR-001 through ADR-008 plus the active-phone capacity decision. Decisions cover Core boundaries, engine communication, ADB authority, Android Agent gate, protocol versioning, scrcpy transition, storage and diagnostic/user-message separation.

**Verification:** every ADR contains context, options, evidence, decision, consequences, rejected alternatives, rollback and date.

## Task 4: Make Stability observation pure

Add a failing regression test proving that an ST snapshot does not mutate VE performance state, then separate pure evaluation from state-changing policy application.

**Verification:** focused RED-GREEN test, full .NET suite and Release build.

## Task 5: Remove direct ST dependencies on concrete engine runtimes

Introduce the smallest neutral snapshot/provider contracts approved by ADR-001/002. Keep MainWindow as composition owner and preserve event-driven refresh.

**Verification:** tests demonstrate ST can classify VE, LE and ExIn snapshots without constructing engine runtimes; full suite/build green.

## Task 6: Break the ExIn and VE ownership cycle

Move shared control-session capabilities behind a neutral contract or application adapter approved by ADR-002. ExIn remains functional without a VE window and VE remains functional without ExIn.

**Verification:** standalone and VE+ExIn tests, fullscreen/focus lifecycle contract tests, full suite/build green. Physical proof remains a separate gate.

## Task 7: Centralize Android transport and controlled process execution

Inventory every process launch, then route applicable ADB/process operations through validated contracts with structured arguments, cancellation, exit code, output and cleanup. Preserve specialized process ownership where consolidation would create a giant service.

**Verification:** negative tests for disallowed paths/arguments and cancellation; no shell concatenation on migrated paths; full suite/build green.

## Task 8: Define NOVORA Protocol and scrcpy transition gates

Document and test protocol framing, version mismatch, capabilities, payload limits, malformed input, timeout, shutdown and session identity. Encapsulate scrcpy as a backend; do not implement or select a new Android Agent before ADR-004 passes.

**Verification:** protocol contract tests and current scrcpy fallback tests. Runtime Dependency remains explicitly nonzero until physical parity is proven.

## Task 9: Complete vendor-neutral acceleration and operational envelope

Separate Available, Selected, Active and BenefitMeasured for NVIDIA/AMD/Intel/software. Add instrumentation needed for comparable VE/LE/ExIn/ST measurements without inventing thresholds.

**Verification:** backend-state tests, software fallback, controlled benchmark format, no claims without P50/P95/P99 evidence.

## Task 10: Independence matrix, fault injection and final validation

Classify and test all 15 engine subsets as REQUIRED, SUPPORTED, UNSUPPORTED BY DESIGN or INVALID. Run safe automated fault cases, physical-device cases when available, security review, Android package/version/hash checks and final whole-branch review.

**Verification:** final report separates source, build, tests, local runtime, physical device and publication evidence. Anything unavailable is explicit and blocks the corresponding Release claim.

## Review focus

- Hidden cross-engine mutation or lifecycle control.
- Polling introduced under the name of health or telemetry.
- Duplicate state authorities and stale session/device identifiers.
- Recovery triggered by congestion or ordinary backpressure.
- Process execution without path/argument/lifecycle controls.
- Claims of GPU acceleration, independence or physical functionality unsupported by direct evidence.
