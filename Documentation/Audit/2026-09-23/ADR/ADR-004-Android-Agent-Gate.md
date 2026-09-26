# ADR-004: Android Agent Model Gate

- Status: Accepted
- Date: 2026-09-23
- Owner: Android platform architecture

## Context

Public MediaProjection, shell/app_process and hybrid models have different consent, privilege, compatibility and maintenance costs. The current runtime uses scrcpy-server through app_process.

## Options

1. Public app APIs only.
2. Shell/app_process agent only.
3. Hybrid capability model.
4. Keep the current backend indefinitely.

## Evidence

Android 14 MediaProjection requires consent per capture session for target API 34+, while shell models may depend on hidden APIs. Current physical compatibility coverage is incomplete.

## Decision

No replacement Agent is implemented yet. Build two bounded experiments after protocol contracts exist: public API capture lifecycle and shell/app_process bootstrap. Evaluate a hybrid only if neither model alone meets the operational envelope. Current scrcpy-server remains the fallback during experiments.

## Consequences

This ADR approves experiments, not a production Agent. No architecture may claim the hybrid outcome in advance.

## Rejected alternatives

Selecting a model without baseline and device matrix would predetermine the audit.

## Rollback

Experiments remain outside the production selection path and can be removed without changing current VE runtime.
