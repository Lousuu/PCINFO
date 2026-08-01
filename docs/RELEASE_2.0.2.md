# HardwareVision 2.0.2

HardwareVision 2.0.2 closes the startup, page-transition, session-report and lifecycle work accumulated on PR #10 while preserving the one-Window, one-Shell, one-PageHost and one-service-graph architecture.

## Startup and visual state

- Startup prioritizes the existing Polling first cycle before Dashboard refresh readiness. The six visible Dashboard slots follow their real source lifecycles; partial provider success/failure is terminal per source and only the existing hard cutoff creates `TimedOut`.
- INITIAL PROJECTION accepts real intermediate values such as 0/6 → 3/6 → 6/6, but only the first six-of-six terminal state authorizes one cold-start Pulse. Duplicate snapshots, newer Polling versions, theme changes, window restore, stale callbacks and post-Reveal updates cannot replay it. Motion Off remains a zero-Pulse direct path.
- COMMIT still waits for Pulse completion. Reveal, Bottom Rail, first-frame gating, DPI-aware placement, DWM fail-open and the accepted polling → Projection → Pulse → COMMIT → Reveal order retain their bounded contracts.
- Classic → Tracework no longer exposes a white Shell/PageHost gap.

## Navigation and performance

- The first page mounts immediately and participates in normal DataBind/Layout, preventing an initial zero-sized Dashboard.
- Later cached pages keep their independent Render turn. Outgoing cleanup is a one-shot `DispatcherPriority.Background` operation guarded by navigation generation and unload/dispose state.
- There is no production `CompositionTarget.Rendering` subscription, timer, delay, synchronous Dispatcher invoke or `UpdateLayout` in the transition path. Real Rendering sampling remains test-only.
- Diagnostic file I/O is serialized on one background channel rather than opening/pruning/appending the log on the UI navigation request path.

## Reports, cadence and scrolling

- Session chart time labels use DPI-aware text measurement and remain inside the plot across tested durations and scales.
- Primary FPS cadence is Display → Present → Application → compatibility. Optional cadence columns are append-only; legacy session files remain readable and are never rewritten.
- The FPS compatibility title, selector, legend, values, diagnostics and calculation rules remain. The redundant FPS subtitle is removed and empty subtitle rows collapse in both Tracework and Classic reports.
- Nested scroll handoff uses the local ScrollViewer chain, retains fractional wheel input and preserves popup, drag and horizontal-gesture behavior.

## Reliability and project audit

- Recoverable settings-write failure keeps the already normalized in-memory settings.
- App shutdown drains log requests queued before the shutdown barrier after service disposal; log-directory failures remain fail-open.
- Motion integration samples actual render frames with guaranteed test-side unsubscription while retaining the original frame-count and final-state assertions.
- Two obsolete CommunityToolkit 8.4.0 generated EventArgs cache sources were removed. They had no production, XAML, reflection, serialization, settings, test, CLI or release entry, and a Release build succeeded with both excluded.
- The audit retained helpers, resource dictionaries, provider fallbacks, service constructors and lifecycle code when deletion or consolidation could not be proven safe.

## Compatibility and distribution

- Hardware/session schemas, settings keys, polling cadence, provider selection, PresentMon 2.5.1 embedding, cached ViewModels and public binding names are unchanged.
- The release remains Windows x64, .NET 8 WPF, framework-dependent, single-file and untrimmed, with exactly one public `HardwareVision.exe` asset.
- The requireAdministrator executable is built and inspected but is not launched by Codex.

## Validation boundary

Release-prep Head `e3ad300698ef04fc5ef5ba70148c563b11b2b0c3` passes PR CI run `30532980844` with `2606 passed / 0 failed / 2606 total`. The final local gate passed all directed groups, zero-warning Release/Debug/test builds, zero vulnerable/deprecated packages, and two identical frozen-binary full Release runs at `2606/0/2606`, exit 0 and empty stderr. Formal publication still requires final PR CI, merged-main CI, the tag package workflow and public asset verification.

Existing human cold-start evidence confirmed the real polling/source-lifecycle/Pulse/COMMIT/Reveal order. The user explicitly authorized skipping a new manual candidate acceptance. Automation cannot prove subjective animation appearance; the final release is supported by the complete automated gates, existing human evidence and formal Release verification.
