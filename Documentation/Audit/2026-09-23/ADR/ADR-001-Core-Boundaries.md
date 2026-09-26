# ADR-001: Neutral Core Boundaries

- Status: Accepted
- Date: 2026-09-23
- Owner: NOVORA application architecture

## Context

VE, LE, ST and ExIn are folders inside one Desktop assembly. ST imports concrete runtime and engine types. Creating a parallel solution now would hide rather than resolve those dependencies.

## Options

1. Split every engine into a new project immediately.
2. Keep direct concrete dependencies.
3. Introduce minimal neutral immutable contracts at proven cross-owner boundaries, then reconsider project extraction after tests pass.

## Evidence

`NOV-AUD-0001`, `NOV-AUD-0002`, the project graph and current composition root.

## Decision

Choose option 3. Contracts live in a neutral `Contracts` namespace inside the existing Desktop project first. A contract is added only to remove a confirmed cross-owner dependency or permit an independent test. MainWindow remains composition owner.

## Consequences

Engine folders can evolve without a disruptive project migration. Binary independence remains a later decision and must not be claimed yet.

## Rejected alternatives

Immediate project splitting creates broad build and packaging risk before ownership is proven. Keeping concrete dependencies violates the independence goal.

## Rollback

Adapters can be removed and callers restored while the original concrete APIs remain during each bounded migration.
