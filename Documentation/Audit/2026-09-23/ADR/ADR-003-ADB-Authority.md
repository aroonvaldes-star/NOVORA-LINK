# ADR-003: Logical Authority over ADB and External Processes

- Status: Accepted
- Date: 2026-09-23
- Owner: Shared Android infrastructure

## Context

ADB and process execution are distributed across services, engines, discovery, update and UI paths. Most use structured arguments, but policy and lifecycle are not expressed through one auditable boundary.

## Options

1. One giant singleton executes every process.
2. Leave all launchers independent.
3. Define validated transport/process contracts and retain specialized owners behind them.

## Evidence

`NOV-AUD-0006`, 34 process detections and 102 ADB detections.

## Decision

Choose option 3. `NLServiceADB` remains the current logical ADB authority while an interface is extracted. A validated process runner owns path validation, structured arguments, cancellation, exit code and output. OS shell openings and updater handoff remain separately classified capabilities. Every Android operation receives an explicit serial/session context.

## Consequences

Migration is incremental. Process.Start is not banned; uncontrolled process execution is.

## Rejected alternatives

A giant singleton mixes unrelated lifecycle and security domains. Leaving launchers unclassified prevents complete audit.

## Rollback

Specialized launchers can remain behind adapters until their behavior and cleanup tests pass.
