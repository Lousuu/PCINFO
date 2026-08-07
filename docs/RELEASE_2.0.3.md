# HardwareVision 2.0.3

HardwareVision 2.0.3 improves navigation continuity, startup Projection presentation, recent-session routing and lifecycle robustness while preserving the one-Window, one-Shell, one-PageHost and one-service-graph architecture.

## Page transitions

- Full and Standard again use the complete FLOW RELAY choreography instead of delaying page availability behind decorative clocks.
- The target CurrentPage commits during Route, allowing the cached incoming page to load and render while the outgoing presenter remains visible.
- Full/Standard visual plans are 420/320 ms with PageRoot, Primary and Secondary exits/entrances. Reduced remains a 150 ms opacity-only path; Off remains immediate.
- A real rendered outgoing/incoming overlap frame precedes generation-guarded cleanup, preventing blank or stale cached-page frames.
- Background diagnostics and ViewModel dispatch do not synchronously block the UI thread.

## SENSOR BUS and INITIAL TRACE

- The final SENSOR BUS Detail text now settles before the output port is positioned or shown.
- Output and input ports enter in order, must both finish, and must be observed in a real visible render frame before the Projection line and Pulse can start.
- The port follows the final visible text width, eliminating the floating far-edge port and route jump.
- A valid Projection request survives Bind to Lock. The no-visible-frame 700 ms fail-open begins at Lock, so a late request receives its full presentation budget.
- Normal startup remains `Polling/source lifecycle -> final Detail -> ports -> route -> one Pulse -> COMMIT -> Reveal`. Duplicate updates, theme changes and stale callbacks cannot replay Pulse.

## Recent game sessions

- Opening a recent session now navigates to an independent `GameSessionReportViewModel` CurrentPage with matching DataContext, Shell route and title.
- The “游戏” navigation item remains selected; closing returns to the same cached GamePerformance instance with recent records, pagination, scroll state and timer lifecycle preserved.
- Repeated reports and rapid navigation are latest-wins. An older report load cannot overwrite or outlive the current route.
- Missing, corrupt, unauthorized or unwritable session/report paths produce recoverable local errors instead of showing another page or escaping the async command.

## Defaults and upgrade behavior

- New installations and missing/corrupt settings recovery now request Full motion by default.
- Existing explicit Full, Standard, Reduced and Off settings are preserved. HardwareVision does not migrate an existing Standard user to Full.
- OS reduce-motion continues to affect only EffectiveLevel, not the stored RequestedLevel.
- Settings schema, game session schema v4, legacy session readers, polling cadence, provider selection and data directories are unchanged.

## Reliability

- Startup-task queries and writes are asynchronous, serialized, cancellable and latest-wins. Child process stdout/stderr are drained concurrently; cancellation terminates the exact child.
- Startup initialization cannot overwrite a newer user startup-task choice.
- Theme and general ViewModel background notifications use nonblocking UI dispatch with shutdown guards.
- Recoverable game session directory/path failures are contained in the owning command and surfaced as bounded status.
- Visual timing tests observe valid completed/cleaned lifecycle states instead of depending on scheduler boundary races; production durations and visual parameters remain unchanged.

## Support boundary

The supported runtime is Windows 10/11 x64 with Microsoft .NET 8 Desktop Runtime x64. Automated tests cover DPI/layout logic, provider fault injection, unavailable PresentMon, missing/read-only/corrupt local data, all motion levels, both themes, tray/shutdown and shown WPF Window lifecycles.

Some DPI, multi-monitor, permission, provider, RDP and software-rendering scenarios are simulated. The release does not claim that every sensor exists on every motherboard/GPU/driver or that every real display topology has been manually tested. Optional capability failure must leave the main UI usable and report a deduplicated local status/log.

## Distribution contract

- win-x64
- framework-dependent
- single-file
- untrimmed
- embedded PresentMon 2.5.1, license and third-party notices
- PE AMD64
- `requireAdministrator`
- exactly one public asset: `HardwareVision.exe`
- signing state reported truthfully as signed or unsigned

No ZIP, checksum text, DLL, PDB, build-info or dependency-inventory file is a public Release asset.

## Validation

The release-preparation baseline is `2637 passed / 0 failed / 2637 total`, with clean source hygiene and zero vulnerable/deprecated packages for the application and test project. Formal publication additionally requires zero-warning Release/Debug/Test builds, all directed suites, 2400+ navigation stress, two identical frozen-binary full runs with exit 0 and empty stderr, PR/main CI, tag package workflow, attestation and downloaded-asset verification.

The release executable is inspected but not launched by Codex because its manifest requests administrator privileges.
