# INITIAL TRACE startup sequence

## Purpose

INITIAL TRACE is HardwareVision 2.0.0's bounded startup presentation. It reports real initialization milestones and reveals the existing shell after its first usable layout. It is not a loading simulator and never invents a percentage.

## Ownership and topology

- `App` creates exactly one `StartupSequenceService` after theme and motion services are available.
- The existing `MainWindow`, `MainViewModel`, `MainShellHost`, chrome controls, and single `PageHost` remain authoritative.
- No second Window, shell, page host, page ViewModel, service graph, polling loop, history buffer, or hardware scan is created.
- The overlay is hosted in `MainShellHost` at Z=120. SYSTEM REWIRE remains Z=100 and FLOW RELAY remains Z=40.
- Startup snapshot exposure is read-only through `MainViewModel`; ordinary navigation is blocked while the sequence is active, except for the one existing initial navigation.

## State model

The monotonic phase order is `Dormant -> Index -> Route -> Bind -> Lock -> Reveal -> Complete`. Every published snapshot has a strictly increasing version and immutable milestone list. Older snapshots and updates after completion are ignored.

Milestone states are `Wait`, `Pending`, `Ready`, `Partial`, and `Failed`. Text and color both communicate state; no color-only meaning is required.

## Real milestones

| Milestone | Signal source | Commit role |
|---|---|---|
| THEME RESOURCES | applied `ThemeService` state | Core |
| SERVICE GRAPH | existing App-owned services constructed | Core |
| PAGE ROUTER | initial `CurrentPage` resolved | Core |
| SENSOR BUS | first existing polling update or polling failure | Informative |
| HISTORY BUFFER | existing `SensorHistoryService` attached | Informative |
| SHELL SURFACE | real LayoutUpdated and positive shell/PageHost size | Core |

The core commit requires theme, service graph, page router, and shell surface. Sensor Bus may finish as Pending, Partial, or Failed without deadlocking launch. The service observes the existing polling events and performs no extra scan.

## Motion profiles

- Full: 1400 ms nominal, 2600 ms hard bound. Horizontal index reveal, one moving signal pulse, ordered SignalRail/Telemetry/TimeRibbon/PageHost reveals, and one commit-lock flash.
- Standard: 900 ms nominal, 1900 ms hard bound. Compressed ordered reveal with no internal moving pulse.
- Reduced: 220 ms nominal, 800 ms hard bound. Simultaneous short opacity-only reveal; no spatial movement.
- Off: no startup visual clock; state commits and the shell is restored immediately.
- Classic theme: plain opacity reveal bounded to at most 120 ms; TRACEWORK overlay remains hidden.

All durations are one-shot. There is no DispatcherTimer, render-loop subscription, scale, blur, shader, screenshot, VisualBrush, or layout-property animation.

## Visual composition and accessibility

The overlay uses a 12-column technical grid: a left milestone axis, central `SYS/BOOT.00` / `TRACEWORK` identity and six-row matrix, a right node/launch/theme/motion/version ledger, and a bottom startup-state rail. Copy includes `INITIAL TRACE`, `COLD START / LOCAL`, and the current phase.

The overlay has no buttons, tab stops, or focus target. One invisible assertive live region announces snapshot changes. It blocks pointer input only while active and becomes collapsed/non-hit-testable after completion.

## Lifecycle and cleanup

- Full sequence, hidden/minimized completion, window close, App shutdown, and overlay unload are finite terminal paths.
- Cancellation sources and observed tasks have one owner and are disposed at shutdown.
- Subscriber exceptions are logged and isolated from state progression.
- Repeated same-state terminal milestone reports do not republish snapshots.
- Animation clocks are cleared; opacity, translation, clip, visibility, and hit testing are restored deterministically.
- Late sensor or layout signals cannot reopen or regress a completed sequence.

## Preserved behavior

INITIAL TRACE does not change Polling cadence, hardware providers, SensorHistory sampling, GPU history, PresentMon, recording, report formats, settings writes, theme commit semantics, FLOW RELAY commit timing, page activation, cached ViewModels, tray behavior, or shutdown ordering.

## Validation boundary

Automated coverage verifies state order, milestone truth, timing/profile contracts, lifecycle, event isolation, source architecture, z-order, accessibility, forbidden APIs/effects, actual shell layout readiness, and final-state restoration. The 2.0.0 suite contains `1366` tests. Screenshots, manual pixel inspection, real-DPI/high-contrast/remote-desktop validation, real administrator sensor load, and launching the requireAdministrator release EXE are intentionally outside this automated boundary.
