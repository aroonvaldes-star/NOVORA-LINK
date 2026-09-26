# ADR-008: Diagnostics versus User Messages

- Status: Accepted
- Date: 2026-09-23
- Owner: Application UX and diagnostics

## Context

Engine errors need technical detail for diagnosis, while Android and PC users need short actionable state without paths, stack traces, secrets or protocol payloads.

## Options

1. Display raw exceptions.
2. Log and display one shared string.
3. Publish structured diagnostic codes/details and separately map them to user messages.

## Evidence

Current engine status objects mix Message and LastError fields; process and transport failures can contain external output.

## Decision

Choose option 3. Engine status exposes stable category/code plus technical detail for local diagnostics. UI maps category/state to concise Spanish text. Secrets, tokens, clipboard, OTP and payloads are excluded. Unknown errors retain a correlation identifier without exposing private data.

## Consequences

Tests assert user-facing behavior and diagnostic classification independently.

## Rejected alternatives

Raw exceptions expose internals. One shared string is unstable for tests and inadequate for support.

## Rollback

Existing Message/LastError remain during migration as compatibility fields populated from the structured diagnostic.
