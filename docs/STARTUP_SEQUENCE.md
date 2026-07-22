# INITIAL TRACE startup sequence

## Purpose

INITIAL TRACE is HardwareVision 2.0.1's bounded startup presentation. Service milestones may accumulate before the window appears, but its visual clock begins only after `ContentRendered`, loaded/template-ready shell surfaces, positive Measure/Arrange results, and a Dispatcher Render turn. It is not a loading simulator and never invents a percentage.

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

Commit requires ready theme resources, service graph, page router, history buffer and shell surface; a terminal Sensor Bus; `VisualReady`; and the internal initial-page projection gate. The gate consumes the first or a newer shared Polling version after Dispatcher application and one post-data `LayoutUpdated`. Dashboard CPU, GPU, Memory, Disk, Network and System regions must each resolve to `Value`, `Unavailable`, `Unsupported`, `Failed`, or `TimedOut`; `Pending`, blank placeholders and unconfirmed zero values are not ready. Timeout converts unresolved slots to `TimedOut` rather than committing pending state. It does not wait for Advanced Sensors, multiple history samples, or PresentMon, and performs no extra poll or hardware scan.

## Motion profiles

- Full: bounded by about 4000 ms after visual readiness. Index reveal, one Bind pulse, six-row Route stagger, ordered Shell reveal, and one commit-lock flash.
- Standard: bounded by about 3200 ms after visual readiness. Opacity Route stagger and compressed ordered reveal.
- Reduced: bounded by about 1500 ms after visual readiness. Simultaneous short opacity-only reveal; no spatial movement.
- Off: no startup visual clock; state commits and the shell is restored immediately.
- Classic theme: plain opacity reveal bounded to at most 120 ms; TRACEWORK overlay remains hidden.

All durations are one-shot. There is no DispatcherTimer, render-loop subscription, scale, blur, shader, screenshot, VisualBrush, or layout-property animation.

## Visual composition and accessibility

The overlay uses a 12-column technical grid and independent `StartupBackgroundLayer`, `StartupContentLayer`, and `StartupBottomRailLayer`. Tracework motion-enabled launches install a static cover before the first visible frame. During Reveal those three layers fade concurrently with the existing Shell targets, so the opaque startup background can no longer hide Shell reveal motion. Classic and Off have no cover.

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

Automated runtime coverage verifies state order, real template parts, Measure/Arrange, Dispatcher application, actual Transform animation clocks, overlay concurrency, timeout resolution, lifecycle and final-state restoration. The 2.0.1 suite contains `1432` tests, plus 20/20 cold-template and 20/20 visual/projection repeats. Screenshots, manual pixel inspection, real-DPI/high-contrast/remote-desktop validation, real administrator sensor load, and launching the requireAdministrator release EXE remain outside this automated boundary.
