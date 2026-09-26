# ADR-006: scrcpy Encapsulation and Retirement

- Status: Accepted
- Date: 2026-09-23
- Owner: VisionEngine

## Context

VE currently requires a documented scrcpy-server 4.1 binary and implements its compatible PC transport, decode and control path. Removing it before parity would regress working behavior.

## Options

1. Remove scrcpy references immediately.
2. Keep direct dependency permanently.
3. Encapsulate it as a backend and retire by evidence gates.

## Evidence

`NOV-AUD-0004`, server hash/provenance and current VE transport/protocol code.

## Decision

Choose option 3. Gates are: G0 inventory; G1 backend abstraction; G2 Android model experiment; G3 functional and operational parity; G4 tested fallback; G5 physical validation and Runtime Dependency = 0. Textual references, licenses and historical benchmarks may remain.

## Consequences

scrcpy remains a legitimate temporary dependency. A new backend cannot become default from build/tests alone.

## Rejected alternatives

Immediate removal sacrifices Stability Core. Permanent direct dependency defeats strategic independence.

## Rollback

The scrcpy backend remains selectable until the final validated Release removes it.
