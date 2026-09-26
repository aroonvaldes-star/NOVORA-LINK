# NOVORA Protocol Contract and scrcpy Transition Gates

Baseline: `7329c1bdaebbfe51fe6bbe18119328c4d26d4912`

## Protocol families

| Family | Owner | Framing/version | Identity/authentication | Limits and failure behavior |
|---|---|---|---|---|
| Control USB/LAN | `NOVORA.Control` | 4-byte big-endian length + JSON, version 1 | TLS fingerprint, invitation secret/device token, monotonically increasing request ID | 64 KiB maximum; invalid/truncated frames close the request; caller cancellation is the timeout boundary |
| Link control/data | `NOVORA.LinkEngine.Protocol` | Versioned HELLO/ACK and framed packets | Explicit relay/session context after trusted control | IPv4 validation, bounded packet buffers, EOF/cancellation terminate the session |
| Vision media/control | `NOVORA.VisionEngine.Protocol` | scrcpy 4.1 compatible codec/session/media/control frames | VE device serial + SCID/socket identity; trust is established outside this media framing | 64 MiB media packet maximum; unknown codec, invalid dimensions, zero/oversize payload and EOF fail closed |
| Discovery | `NOVORA.Control` / `NOVORA.Discovery` | Versioned discovery response and nonce | Discovery is never authorization | Nonce/version/address validation; result only proposes a destination |

New NOVORA-owned protocols must follow ADR-005: magic, protocol version, session/connection ID, message type, payload length, ordered sequence where required, capabilities and explicit maximums. Capability negotiation never grants trust.

## Shutdown and timeout policy

- Reads accept a cancellation token; deadlines are imposed by the owning session.
- Truncated streams produce EOF and do not replay mutations.
- Version mismatch is rejected before command dispatch.
- Session replacement invalidates the prior generation; stale replies cannot restore it.
- Graceful stop cancels readers, closes transport, awaits owned tasks and publishes stopped state.

## scrcpy backend state

`VEServerBackend.Scrcpy41Compatibility` is the only implemented Android video backend. `VEServerBackendCatalog` reports:

- Available: true when the packaged dependency is present at runtime.
- Selected: true for the current implementation.
- Active: true only while its owned session is running.
- RuntimeDependency: `scrcpy-server`.
- ProtocolVersion: `4.1`.

This is encapsulation, not independence. `NOV-AUD-0004` remains CONFIRMED.

## Retirement gates

| Gate | Requirement | Current result |
|---|---|---|
| G0 | Inventory, provenance, hash and runtime call graph | PASS for current scrcpy backend |
| G1 | Backend identity and dependency state are explicit and tested | PASS |
| G2 | NOVORA Android Agent experiment implements negotiated protocol without becoming default | NOT STARTED |
| G3 | Functional parity: video, audio, input, clipboard, files, privacy, reconnect and fullscreen/focus | BLOCKED pending G2 and physical device |
| G4 | Tested fallback/rollback between candidate and scrcpy backend | BLOCKED pending G2 |
| G5 | Physical latency/jitter/loss/quality parity and RuntimeDependency = 0 | BLOCKED pending G2 and physical device |

scrcpy cannot be removed or declared replaced until G0-G5 pass. Textual provenance and historical benchmark references may remain after runtime retirement.

## Automated evidence

- Backend dependency descriptor test.
- Control version mismatch before dispatch.
- Oversize and truncated frame rejection.
- Cancellation as read timeout boundary.
- Existing VE codec/session/media and scrcpy 4.1 control serialization tests.
