# INITIAL TRACE startup sequence

## Purpose

INITIAL TRACE is HardwareVision 2.0.1's bounded startup presentation. Service milestones may accumulate before the window appears. App starts the service exactly once immediately after `MainWindow.Show()` returns; the service then waits for a loaded, positively measured Shell/PageHost/overlay surface before entering Index. It is not a loading simulator and never invents a percentage.

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
| SHELL SURFACE | atomic real-surface report from Loaded, ContentRendered/Render, LayoutUpdated, or SizeChanged with positive shell/PageHost size and a loaded overlay | Core |

Commit requires ready theme resources, service graph, page router, history buffer and shell surface; a terminal Sensor Bus; `VisualReady`; and the internal initial-page projection gate. The gate consumes the first or a newer shared Polling version after Dispatcher application and one post-data `LayoutUpdated`. Dashboard CPU, GPU, Memory, Disk, Network and System regions must each resolve to `Value`, `Unavailable`, `Unsupported`, `Failed`, or `TimedOut`; `Pending`, blank placeholders and unconfirmed zero values are not ready. Timeout converts unresolved slots to `TimedOut` rather than committing pending state. It does not wait for Advanced Sensors, multiple history samples, or PresentMon, and performs no extra poll or hardware scan.

## Motion profiles

- Full: 180 ms Index; six 150 ms Route rows starting every 45 ms; a 375 ms Route phase; 180 ms COMMIT; and a 360 ms Reveal phase. Route segments use a 70 ms clip; only milestone names move 6 DIP. A real projection-count increase reveals vertically for 90 ms and emits one 72x1 cyan pulse over 180 ms.
- Standard: 120 ms Index; six 80 ms opacity Route rows starting every 28 ms with the same segment clip and no translation; a 220 ms Route phase; 180 ms COMMIT; and a 270 ms Reveal phase.
- Reduced: bounded by about 1500 ms after visual readiness. Index, the whole Route matrix, projection changes, COMMIT and Reveal use short opacity-only clocks; there is no spatial movement.
- Off: no startup visual clock; state commits and the shell is restored immediately.
- Classic theme: plain opacity reveal bounded to at most 120 ms; TRACEWORK overlay remains hidden.

All durations are one-shot. There is no DispatcherTimer, render-loop subscription, scale, blur, shader, screenshot, VisualBrush, or layout-property animation.

## Visual composition and accessibility

The overlay uses independent `StartupBackgroundLayer`, `StartupContentLayer`, and `StartupBottomRailLayer` surfaces in `Auto / * / Auto` rows. Tracework motion-enabled launches install a static black cover before the first visible frame; Dormant shows no route rows, rail, Phase, COMMIT, or dashboard. Index reveals the Auto-sized top composition, the middle remains quiet whitespace, and the separate rail stays at the bottom. During Reveal all three layers fade concurrently with the existing Shell targets. Classic and Off have no cover.

The six milestones use one ItemsControl and six lightweight `StartupMilestoneRow` controls. Every 34 DIP row uses fixed `24 / 180 / 72 / *` columns. Its upper and lower route segments are 1x17 DIP, terminal segments are hidden, and the 4x4 node shares the exact row center with the name, state and detail. Each real milestone-state change owns one bounded transition; unchanged state does not replay. Phase appears once on the bottom rail.

The right ledger exposes the real `ResolvedVisibleSlotCount / TotalVisibleSlotCount RESOLVED` projection. Count increases animate once even when one snapshot resolves multiple slots; equal or lower counts do not play. COMMIT is a 28x28 mint lock group and is visible only for `Lock && CanCommit`.

Full Reveal exits content over 110 ms with opacity, -8 DIP horizontal translation and a 25% right-side clip contraction; the bottom rail exits over 120 ms and the background exits from 35 to 260 ms. Signal rail, telemetry spine, PageHost and time ribbon enter at 45/90/135/245 ms for 100/100/190/90 ms. Standard uses 70/70/150/70 ms Shell target durations inside a 270 ms phase. Reduced is one 150 ms cross-opacity path. Completion clears all opacity, translation and clip clocks.

The overlay has no buttons, tab stops, or focus target. One invisible assertive live region announces snapshot changes. It blocks pointer input only while active and becomes collapsed/non-hit-testable after completion.

## Lifecycle and cleanup

- Full sequence, hidden/minimized completion, window close, App shutdown, and overlay unload are finite terminal paths.
- Cancellation sources and observed tasks have one owner and are disposed at shutdown.
- Subscriber exceptions are logged and isolated from state progression.
- Repeated same-state terminal milestone reports do not republish snapshots.
- ShellSurface and VisualReady are committed atomically under one lock with one version increment, one readiness wake-up, and one snapshot publication. Invalid sizes and duplicate reports publish nothing.
- Visual readiness is bounded by one cancellable 2500 ms delay. Timeout logs and publishes Complete with an explicit `visual surface readiness timeout` failure, does not fake ShellSurface readiness, collapses the overlay, and restores Shell hit testing.
- Animation clocks are cleared; opacity, translation, clip, visibility, and hit testing are restored deterministically.
- Late sensor or layout signals cannot reopen or regress a completed sequence.

## Preserved behavior

INITIAL TRACE does not change Polling cadence, hardware providers, SensorHistory sampling, GPU history, PresentMon, recording, report formats, settings writes, theme commit semantics, FLOW RELAY commit timing, page activation, cached ViewModels, tray behavior, or shutdown ordering.

## Validation boundary

Automated runtime coverage verifies start-after-Show ordering, atomic readiness, zero-size recovery, all four surface entry points, a real WPF Show/Loaded/Dispatcher lifecycle, 20/20 visual-readiness fail-open, 1120x720 and 1600x900 geometry, final interaction restoration, and unchanged 20/20 cold-template behavior. The final choreography adds direct runtime checks for named row parts, Route clocks, projection clips/pulses, COMMIT gating/center clip, Reveal clocks and deterministic cleanup, including separate 20/20 Route, Projection and Reveal repetitions. Two isolated full Release runs report `1557 passed, 0 failed, 1557 total`. Screenshots, manual pixel inspection, real-DPI/high-contrast/remote-desktop validation, real administrator sensor load, and launching the requireAdministrator release EXE remain outside this automated boundary.
