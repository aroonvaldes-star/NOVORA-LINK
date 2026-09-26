# ADR-009: One Active Phone per PC

- Status: Accepted
- Date: 2026-09-23
- Owner: NOVORA product/session architecture

## Context

Current product specifications and manuals define one active phone per PC. `ARCHITECTURE-RULES.md` separately claims five simultaneous LinkEngine sessions, creating contradictory ownership and resource expectations.

## Options

1. One active phone per PC.
2. Up to five simultaneous active phones.
3. Multiple known phones but one active cross-engine session.

## Evidence

`NOV-AUD-0005`, Android visual specification, manual and current selected-device model.

## Decision

Choose option 3. NOVORA may remember multiple known/trusted phones, but exactly one phone owns the active VE/LE/ExIn/control session on a PC. Background discovery may observe candidates without allocating engine resources. Switching active phone is an explicit lifecycle transition.

## Consequences

The obsolete five-active-session statement must be corrected. Every command validates the active SessionId/DeviceId/transport before mutation.

## Rejected alternatives

Five active phones conflicts with current UX and maximum-performance goal. Forgetting all non-active phones would harm reconnection usability.

## Rollback

A future multi-device design requires a new ADR, capacity benchmarks and per-session resource ownership; it cannot silently expand this policy.
