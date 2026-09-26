# ADR-005: NOVORA Protocol Versioning

- Status: Accepted
- Date: 2026-09-23
- Owner: Neutral protocol infrastructure

## Context

PC and Android are separate projects with source-level compatibility expectations. scrcpy protocol is internal and version-coupled. NOVORA needs deliberate compatibility and resource limits.

## Options

1. Continue ad hoc message evolution.
2. Copy scrcpy framing.
3. Define a NOVORA-owned versioned envelope and capability negotiation.

## Evidence

Current Control/Remote/LE protocols already carry versions or handshakes, but no single policy covers message limits, unknown frames and compatibility.

## Decision

Choose option 3. Every new NOVORA protocol envelope defines Magic, ProtocolVersion, SessionId/ConnectionId, MessageType, PayloadLength, Sequence where ordered, capabilities and explicit maximums. Unknown messages, malformed frames, mismatch, timeout, reconnect and graceful shutdown receive tested behavior. Existing protocols migrate incrementally.

## Consequences

Capability negotiation does not authorize a session. Authentication/trust remains separate.

## Rejected alternatives

Ad hoc evolution permits silent incompatibility. Copying scrcpy inherits an internal protocol NOVORA does not control.

## Rollback

Existing protocol versions remain supported during a documented compatibility window.
