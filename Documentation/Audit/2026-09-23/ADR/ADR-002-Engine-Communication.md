# ADR-002: Engine Communication and Ownership

- Status: Accepted
- Date: 2026-09-23
- Owner: NOVORA application architecture

## Context

ExIn and VE have direct bidirectional dependencies. ST reads all engines. Cross-engine lifecycle commands would make one failure propagate to unrelated engines.

## Options

1. Direct calls between engines.
2. A global service locator/event bus.
3. Explicit command interfaces, immutable snapshots and typed events composed by the application.

## Evidence

`NOV-AUD-0002`, `NOV-AUD-0003`, `ExInControlSession`, `VEControlManager` and current status-event wiring in MainWindow.

## Decision

Choose option 3. Commands cross ownership through narrow interfaces. Observations cross as immutable snapshots or typed events. MainWindow/application adapters coordinate combinations. No engine starts, stops or recovers another engine.

## Consequences

Some adapters are required. A global untyped bus is prohibited. Event handlers must unsubscribe during disposal.

## Rejected alternatives

Direct calls preserve the cycle. A service locator hides dependencies and weakens tests.

## Rollback

Each adapter migration retains the previous concrete implementation behind the new interface until its focused and full tests pass.
