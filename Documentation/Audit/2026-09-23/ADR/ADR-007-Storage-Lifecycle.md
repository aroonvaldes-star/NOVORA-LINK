# ADR-007: Storage Ownership and Lifecycle

- Status: Accepted
- Date: 2026-09-23
- Owner: Shared storage infrastructure

## Context

NOVORA stores user media, settings, controller profiles, history, trust material, installed tools and Android temporaries in several roots.

## Options

1. Move everything into one directory.
2. Keep paths without ownership metadata.
3. Keep platform-appropriate roots and document Owner, Creator, Readers, Lifetime and Cleanup for each artifact.

## Evidence

Storage map in the Current Architecture Pack and 115 storage detections.

## Decision

Choose option 3. User-visible content stays under Desktop/NOVORA-Files. Settings, profiles and histories stay under scoped LocalAppData subtrees. Secrets/trust use protected no-backup/private storage. Installed binaries stay under the application Tools directory. Android runtime temporaries have explicit cleanup.

## Consequences

Shared storage helpers may be introduced, but ownership categories remain separate. No private payload is logged by default.

## Rejected alternatives

One directory mixes lifecycle and privacy. Unowned paths cannot be cleaned or migrated safely.

## Rollback

Any physical path migration requires read-old/write-new compatibility and a documented restore path.
