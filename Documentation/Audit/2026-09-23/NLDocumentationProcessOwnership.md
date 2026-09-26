# External Process Ownership

Baseline: `7329c1bdaebbfe51fe6bbe18119328c4d26d4912`

## Validated finite runner

`NLServiceProcess` is the shared contract for finite child processes. It requires an existing absolute executable path and working directory, rejects control characters in arguments, uses `ArgumentList`, captures stdout/stderr and exit code, and terminates the process tree when cancellation wins.

The following finite ADB adapters use this contract:

- `VisionEngine/Exchange/VEExchangeADB.cs`
- `LinkEngine/Failover/LEFailoverADB.cs`

`NLServiceADB` remains the logical authority for general Android discovery and commands. Every migrated engine adapter requires an explicit serial or an already classified argument list.

## Specialized owners retained

| Owner | Capability | Reason it remains specialized | Required controls |
|---|---|---|---|
| `NLServiceADB` | General finite ADB and server lifecycle | Existing Android authority | Structured arguments, cancellation, output, one active phone |
| `NLDiscoveryADBTrackDevices` | Persistent `track-devices` stream | Framed event source, not a finite command | Explicit cancellation, bounded restart backoff, owned process cleanup |
| `VEServerManager` | Android media server process | Coupled to VE transport/session lifetime | Explicit serial, argument list, stop/dispose cleanup |
| `LENetworkRelay` | RelayCore child process | Long-lived LE-owned runtime | Absolute packaged path, redirected diagnostics, LE-only recovery |
| `NLServiceUpdate` | Signed updater handoff | Privileged release boundary | Signature/hash policy and explicit user action |
| UI shell openings | Browser, Explorer and `joy.cpl` | OS shell capability, not command execution | Fixed targets or validated URLs/paths; `UseShellExecute=true` only here |
| `VEStreamingProtected` | Windows display/privacy helpers | OS capability with distinct lifetime | Fixed executable, no user-built command line |

## Prohibited patterns

- Relative executable paths for non-shell child processes.
- Concatenating user-controlled values into `Arguments`.
- Launching Android work without explicit serial/session context.
- Leaving a cancelled finite child process alive.
- Treating a persistent stream or relay as a finite command.

## Evidence

- `NLTestServiceProcess.Controlled_process_requires_absolute_paths_and_safe_arguments`
- `NLTestServiceProcess.Controlled_process_captures_output_error_and_exit_code`
- `NLTestServiceProcess.Controlled_process_cancellation_terminates_the_process`
- Release solution build: 0 warnings, 0 errors.
