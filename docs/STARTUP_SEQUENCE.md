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

- Full: 240 ms Index; six Route rows starting every 170 ms; a 1050 ms Route phase; 180 ms COMMIT; and a 360 ms Reveal phase. Each row uses Upper 0–55 ms, Node 55–95 ms, Name 60–135 ms, Status 70–135 ms, Detail 85–150 ms, terminal lock 95–185 ms, and Lower 145–205 ms. Projection values exchange vertically over 140 ms.
- Standard: 190 ms Index; six Route rows starting every 110 ms; a 680 ms Route phase; 180 ms COMMIT; and a 270 ms Reveal phase. Each row uses Upper 0–45 ms, row content 40–100 ms, and Lower 85–135 ms. Projection values use a 120 ms vertical Clip.
- Reduced: bounded by about 1500 ms after visual readiness. Index explicitly restores the title/subtitle child opacity, the whole Route matrix fades as one, Projection values cross-fade over 100 ms, and COMMIT/Reveal remain opacity-only; there is no spatial movement or Projection route.
- Off: no startup visual clock; state commits and the shell is restored immediately.
- Classic theme: plain opacity reveal bounded to at most 120 ms; TRACEWORK overlay remains hidden.

All durations are one-shot. There is no DispatcherTimer, render-loop subscription, scale, blur, shader, screenshot, VisualBrush, or layout-property animation.

## Projection route and presentation queue

- The SENSOR BUS output and INITIAL PROJECTION input are visible `6×6` framed ports with centered 1 DIP coordinate anchors. Their centers are translated into `OverlayRoot` immediately before every playback.
- A valid three-segment route requires finite loaded endpoints, positive sizes, `target.X > source.X`, at least 96 DIP horizontal distance, and at least 40 DIP for both horizontal segments. `corridorX` is the 50% point clamped to 48 DIP from each endpoint. A short route is allowed only as one horizontal segment when vertical error is at most 4 DIP.
- Source horizontal, vertical bridge and target horizontal are independent 1 DIP Borders with independent Clips. Upward vertical reveals begin at the bottom; no negative height and no whole-route horizontal Clip is used.
- Route build time is `totalRouteLength / speed`: Full uses 600 DIP/s clamped to 360–520 ms with 80/90/120 ms segment minima, 50 ms hold and 90 ms fade; Standard uses 800 DIP/s clamped to 260–380 ms with 60/70/90 ms minima, 30 ms hold and 70 ms fade. Adjacent segments overlap by 15 ms. Full alone moves one `5×5` square node along the established route.
- Business resolved count and last presented count are separate. Index/Route updates only the business count. Bind animates the value from the last presented count immediately, waits for the Projection Ledger's entry completion, then plays one route. An active route is never replaced; later snapshots update the value immediately and coalesce into one latest pending replay.
- Lock allows the active route to finish but starts no new route. Reveal increments the generation, clears pending/active state, removes all segment/head/Canvas clocks and hides the route before the Shell appears.

## Visual composition and accessibility

The overlay uses independent `StartupBackgroundLayer`, `StartupContentLayer`, and `StartupBottomRailLayer` surfaces in `Auto / * / Auto` rows. Tracework motion-enabled launches install a static black cover before the first visible frame; Dormant shows no route rows, rail, Phase, COMMIT, or dashboard. `PrepareIndexInitialState` installs a one-shot hidden baseline before Index. The middle remains quiet whitespace, and the separate 56 DIP rail stays at the bottom. During Reveal all three layers fade concurrently with the existing Shell targets. Classic and Off have no cover.

The six milestones use one ItemsControl and six lightweight `StartupMilestoneRow` controls. Every 34 DIP row uses fixed `24 / 180 / 72 / *` columns. Before Route, `PrepareForRoute` hides both route segments and every row field. Route arrival then reveals each row and gives already resolved terminal nodes one lock flash. `routeArrivalPlayed` and `terminalLockPlayedState` de-duplicate route/state callbacks while preserving later real transitions.

The right ledger has Identity, Environment and Projection groups entering in Index, Route and Bind. Projection exposes the real `ResolvedVisibleSlotCount / TotalVisibleSlotCount RESOLVED` through separate previous/current layers. Count increases animate once even when one snapshot resolves multiple slots; equal or lower counts do not play. Full computes a path from the SENSOR BUS row anchor to the Projection ledger anchor in `OverlayRoot` coordinates, using a three-segment orthogonal path when Y differs. Invalid/short distances suppress only the pulse, not the value update.

The bottom rail presents exactly five stages: `01 / 05 INDEX`, `02 / 05 ROUTE`, `03 / 05 BIND`, `04 / 05 LOCK`, and `05 / 05 REVEAL`, each paired with its Chinese label. Completed, current and future segments use Success, Identity/Telemetry and TraceGrey. Failure replaces the center with `启动降级：{FailureMessage}` and the code with `FAILED`; fail-open and overlay exit remain unchanged. COMMIT is a separate 28x28 mint lock group visible only for `Lock && CanCommit`.

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

Automated runtime coverage verifies start-after-Show ordering, atomic readiness, zero-size recovery, all four surface entry points, a real WPF Show/Loaded/Dispatcher lifecycle, 20/20 visual-readiness fail-open, 1120x720 and 1600x900 geometry, final interaction restoration, and unchanged 20/20 cold-template behavior. The completed choreography adds 20/20 Index/Route/Bottom Rail and 20/20 Projection/live-coordinate repetitions, including pre-ready locks, duplicate suppression, old/new projection values, responsive endpoints and terminal cleanup. The suite contains `1597` tests, above the `1557` baseline; final isolated builds, two full Release runs and CI are recorded in Draft PR #9. Screenshots, manual pixel inspection, real-DPI/high-contrast/remote-desktop validation, real administrator sensor load, and launching the requireAdministrator release EXE remain outside this automated boundary.
