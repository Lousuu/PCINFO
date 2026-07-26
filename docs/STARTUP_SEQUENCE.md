# INITIAL TRACE startup sequence

## v2.0.2 current startup motion contract

- [`TRACEWORK_MOTION_SPEC.md`](TRACEWORK_MOTION_SPEC.md) is the current runtime authority. It supersedes older state names, Render counts, Index authorization, Reveal/Shell timings, full-page Clip descriptions, test totals, and PR/release boundaries below while preserving them as history.
- The native gate is now `Dormant -> NativePrepared -> ShownHiddenOffscreen -> FirstOffscreenRenderCommitted -> OffscreenCompositionFlushed -> FinalPlacementAppliedHidden -> FinalPositionRenderCommitted -> FinalPositionCompositionFlushed -> Released`, with `FailOpenReleased` and `Cancelled` terminals. Three independent Render boundaries and two `DwmFlush` points precede publication of the final gate release.
- `SurfaceMeasured` and `FirstFrameGateReleased` are independent snapshot facts; `VisualReady` requires both. Index waits for both. An early Index snapshot is retained as one pending generation and replayed through a separate Render callback after release.
- `SYS/BOOT.00` uses a stable-width local horizontal Clip for 180/120 ms Full/Standard with real intermediate Rects. Startup Dashboard handoff uses a readable Reveal hold, concurrent overlay/Shell/PageRoot/role choreography, no full-page Clip, visible completion before ContextIdle cleanup, and stable milestone presentation objects.
- Automated state evidence cannot certify the first visible pixel or subjective frame pacing. Manual cold-start and navigation recording acceptance remains pending. Draft PR #10 remains Open/Draft/Unmerged; no version metadata, tag, merge, or Release is part of this candidate.

## v2.0.2 DPI-aware first-frame placement correction

- The initial PR #10 placement mixed coordinate domains: `GetMonitorInfo` returned a physical-pixel work area, but the calculated physical X/Y values were written to WPF `Left` / `Top` as DIP. The error scales with monitor DPI and produces the lower-right cold-start displacement.
- The gate now captures a `FirstFramePhysicalPlacement` before staging: target monitor, physical work area, effective X/Y DPI, and final physical bounds. The window's DIP dimensions are converted with `DIP × DPI / 96`, centering and off-screen staging stay in physical coordinates, and final release applies the saved HWND bounds through `SetWindowPos`. The cursor is consulted only during capture, so movement during the two Render boundaries cannot retarget the release.
- Manual placement captures the HWND's existing physical rectangle; CenterOwner captures the owner's physical rectangle and monitor. The normal CenterScreen launch uses the selected monitor's physical work area. A native-API failure uses the existing fail-open path; it never leaves opacity gated permanently.
- The state machine, two Render-priority callbacks, `DwmFlush`, 500 ms fail-open, generation invalidation, activation restoration, dark first frame, tray behavior, Classic/Off bypass, minimize/maximize behavior, and all COMMIT/Reveal timing remain unchanged.
- Ten cases repeated 20 times verify 100/125/150/175/200%, negative-left, right-side, lower, mixed-DPI, and cursor-after-capture placement with `<= 2` physical-pixel center error. The dedicated result is `200/0/200`; the existing candidate visual set remains `240/0/240`, both with empty stderr. No manual real-monitor DPI acceptance is claimed.
- Two test-only Reveal waiters were made scheduling-safe: they accept either an active WPF clock or its already completed/cleaned zero-opacity state within a bounded one-second Dispatcher tolerance. This changes no production duration or event order. Focused Reveal results are `20/0/20` and `4/0/4`; Runtime XAML is `101/0/101`. Repository-external Release/Debug/test builds are clean, and two independent complete Release apphost runs both pass `2497/0/2497` with empty stderr.

## v2.0.2 candidate native first-frame and COMMIT contract

- The remaining 200–230 ms flash was below the WPF overlay: one Render callback can validate managed layout but cannot prove that the native compositor has presented the dark frame. Cold start therefore stages the only Window outside the virtual desktop while hidden, preserving its original startup location, final multi-monitor coordinates, and activation behavior.
- `PrepareFirstFrame` owns the explicit one-shot state sequence `Dormant -> NativePrepared -> ShownHidden -> FirstRenderCommitted -> NativeCompositionFlushed -> FinalPlacementCommitted -> Released`. The first Render callback validates the generation, HWND/HwndSource, positive loaded layout, loaded overlay, and direct `#0B0E11` surfaces, reports readiness, then calls `DwmFlush` without releasing. One second Render callback restores the final native placement while still hidden, flushes, commits the Dispatcher boundary, and sets opacity directly to 1. There is no fade, third callback, retry loop, timer, or `CompositionTarget.Rendering` subscription.
- A separate 500 ms generation-guarded path restores placement and activation semantics, reasserts the dark CompositionTarget and DWM state, flushes best-effort, then enters `FailOpenReleased`. Closing/Closed enters `Cancelled`; late callbacks cannot move, activate, or change opacity. Tray restore never re-stages. Classic and Motion Off fail open without hiding the startup surface.
- Native theme setup tries immersive dark attribute 20 and only falls back to 19 on failure. It then writes attributes 34/35/36 with Tracework Border `0x002D2620`, Caption `0x00110E0B`, and Text `0x00F7F3EE`. Same HWND/generation/theme writes are idempotent. Classic restores 34/35/36 to `0xFFFFFFFF`; missing APIs and nonzero HRESULTs are diagnostic-only and never block startup.
- COMMIT has one exit root. `CommitGraphicLayer` contains the 28×28 lock and settles at 0.82; the lock base is 1. The text is a sibling at opacity 1, so it is no longer multiplied by graphic attenuation. Full/Standard build for 180 ms, Reduced for 90 ms; stable holds are 480/360/180 ms, producing 660/540/270 ms minimum totals measured from `PlayCommit`. Early Reveal waits only the remaining total. Failure, cancellation, unload, hidden completion, and Off bypass the hold. Only `CommitExitRoot` exits, 1 to 0 over 90 ms; cleanup clears all clocks, restores Root/Graphic/Lock/Text to 1/0.82/1/1, and collapses the group.
- Existing Index 180/120 ms, Projection routing/coalescing, Bottom Rail, Reveal holds 100/80/40 ms, Shell Reveal, readiness and business state remain unchanged. Twelve focused groups run 20 times each, bringing the candidate suite from 2057 to 2297. Isolated Release/Debug/Test builds pass with zero warnings and errors, Runtime XAML is `101/0/101`, the focused set is `240/0/240`, and two independent full Release processes are both `2297/0/2297` with empty stderr; package vulnerability and deprecation audits are zero. This is an Open Draft candidate only: v2.0.1 is unchanged, and no tag, Release, merge, administrator EXE, screenshot review, or manual recording acceptance is claimed.

## v2.0.1 final native/visual state contract

- Before `Show()`, the one physical Window, root, Shell and overlay retain direct `#0B0E11` backgrounds. `PrepareFirstFrame` additionally creates the HWND, commits the same native CompositionTarget background, attempts DWM dark title attributes 20/19, and arms an opacity gate. Render readiness or the 500 ms fail-open releases that gate exactly once; close invalidates it and tray restore never re-arms it.
- Commit readiness is `coreReady && sensorTerminal && visualReady && initialProjection.IsReady`. Bind may expose this instantaneous result but cannot latch it. At Lock or later, prior/current readiness sets a service-lifetime monotonic authorization; PollingVersion changes can refresh Projection without revoking COMMIT.
- `PlayCommit` establishes a visual latch after any active Projection pulse. Group and Lock settle at 0.70 and Text at 1. Later Lock snapshots cannot collapse them. Reveal uses one 90 ms no-relight exit for Group/Lock/Text and clears all commit clocks and Center Clip together.
- Reveal is irreversible at snapshot receipt, not at animation completion. The same call finalizes Projection, stops pulses, clears the ordinary phase queue and atomically commits Text/Code/brushes/Track as `05 / 05 REVEAL`. Stable holds are Full 100 ms, Standard 80 ms, Reduced 40 ms; failure/Off/hidden terminal paths hold 0.
- Shell reveal begins behind the still-opaque startup layer on that same snapshot. Full targets end by 160 ms and Standard by 130 ms; Reduced is simultaneous 120 ms opacity-only and Classic remains 120 ms. After the hold, all non-Off startup layers and any COMMIT exit together for 90 ms to opacity 0, then the overlay collapses and clears Clip/Translate/Opacity/hold clocks.
- Full/Standard Index freezes one valid natural-width `SYS/BOOT.00` layout and grows one RectangleGeometry continuously for 180/120 ms. Invalid layout receives at most one Render retry; failure or an already-advanced phase commits final state. Pending retries are generation-invalidated on reset, Reveal, Complete, cancel and unload.
- Ten independently filterable groups repeat 20/20 and cover native gate, fail-open, monotonic authorization, visual latch, no-relight exit, atomic Reveal rail, Shell overlap, late snapshots, stable Index Clip and terminal clock cleanup across 920×620, 1107×685, 1120×720 and 1600×900. Candidate total is 2057; final release requires two identical 0-failure/empty-stderr Release runs plus PR/main/package CI.
- PR #9 is the release integration boundary. After all automated gates pass, the user authorizes Ready + merge commit, main/package validation, annotated `v2.0.1` and a formal Release with one `HardwareVision.exe`. The administrator EXE is not launched, no screenshot/recording is viewed by Codex, and no new manual recording acceptance is claimed.

## Final 2.0.1 presentation polish

- App prepares the existing first-frame path before `MainWindow.Show()`. The physical Window, outer root, `MainShellHost`, overlay root and startup background all use the direct static color `#0B0E11`; the final native opacity gate described above supersedes this earlier static-only candidate record.
- Projection content is ledger-aligned: heading and resolved value share the NODE / LAUNCH / THEME / MOTION / VERSION left edge. The input port is a separate `-18` DIP overlay, leaving 12 DIP before the label without changing its center-anchor semantics. The SENSOR BUS output uses Detail / 16 DIP / 6 DIP port / remainder columns; Detail caps at 420, 300 or 220 DIP while the 34 DIP row and ellipsis remain.
- `ProjectionRoute` and `ConfigureProjectionGeometry` drive both the existing upward pulse and a new static dormant channel. Dormant is hidden before Ledger Ready, uses 0.12 Full/Standard or 0.08 Reduced, stays below the active one-shot pulse and survives its completion plus Lock, then clears at Reveal. Off and invalid geometry suppress it. There is no loop, Timer, rendering callback, poll or alternate geometry algorithm.
- COMMIT is still started only after the active Projection pulse completes. Lock durations are 1250/950/360/0 ms for Full/Standard/Reduced/Off. `PlayCommit` records the actual start, establishes Full/Standard in 180 ms (Reduced in 90 ms), settles Group/Lock at 0.70 and text at 1, and guarantees stable holds of 350/250/180 ms. Reveal exits in 90 ms. A single animation completion may add at most 200/150/80 ms when abnormal timing arrives early; failure bypasses compensation and remains fail-open.
- The rail's 20 DIP first row aligns STARTUP STATE and PhaseCode with one 11 DIP Bold / 18 DIP line-height style; center copy remains 15 DIP SemiBold. PhaseCode now has Previous and Current layers. Each queue pop creates one `StartupPhasePresentation`, prepares old/new Text and Code together, and applies the track when the incoming pair starts. Full uses paired `TranslateY`, Standard paired clip/fade, Reduced paired fade and Off a direct atomic commit. Failure and all cleanup paths treat Text/Code together.
- Seven new groups repeat 20/20 and lift the suite from 1717 to 1857: first frame, alignment, dormant channel, source port, COMMIT minimum presentation, rail style and atomic transition. Existing fail-open, cold-template and nested-scroll regressions are retained. Final build/run/CI evidence belongs to the existing Open Draft PR #9; no merge, tag, Release, administrator EXE or manual visual/DPI acceptance occurred.

## Final 2.0.1 runtime stabilization

- Reveal is visually irreversible. The first Reveal snapshot stops/cleans Projection, exits COMMIT when present, starts the concurrent Content/Bottom Rail/Background exit, and records `revealVisualStateEntered`. Later snapshots cannot restore opacity, restart choreography, replace exit clocks, or regress the visual phase; Complete/Unload performs final cleanup.
- Delayed RectangleGeometry animations always commit an empty Rect before `BeginAnimation`, contain explicit `0 ms -> delay -> delay + duration` keyframes, and commit/clear the final Rect in Completed. This applies to Route segments, all three Projection segments, Bottom Rail entry, phase-segment reveals, and the other startup Clips.
- Full uses 360 ms Index, 205 ms row starts, 1220 ms Route, 360 ms Bind, 1250 ms Lock, and 360 ms Reveal. Standard uses 300/120/720/220/950/270 ms. Reduced uses 120 ms Index and 360 ms Lock; Off has no visual clock. The next row starts only after the prior row's 145–205 ms Lower connection is complete.
- Bottom Rail Ready is 180 ms Full and 140 ms Standard. A monotonic queue presents Index before any already-arrived Route snapshot and holds Index for at least 120/160 ms. Repeated or regressive phases are ignored; Complete/fail-open clears pending phases without blocking exit.
- Source and target ports are phase-owned. SENSOR BUS is collapsed in Dormant/Index, Route starts it at 0, node arrival enters 0.35, and Bind enters 1. Projection Input stays at 0 through Bind entry and enters 1 only at Projection Ledger Ready.
- Projection geometry is evaluated in live WPF logical DIP coordinates. `target.X <= source.X`, non-finite coordinates, and horizontal distance below 24 DIP are invalid. Same-Y routes require `abs(dY) <= 1` and use one horizontal segment. Bent routes require at least 36 DIP; 36–72 DIP clamps endpoint segments to 12 DIP and 72 DIP or more uses 24 DIP. A temporarily unready layout receives one Render-priority retry; permanently invalid geometry suppresses only the route.
- Projection values use exactly Previous and Current layers. Active transitions are not replaced; arrivals coalesce to one latest replay. Full/Standard/Reduced durations are 160/130/100 ms. A newer PollingVersion invalidates old value/pulse callbacks, accepts a lower resolved count, resets the displayed baseline to zero, and animates `0 -> N`.
- Full route playback remains length-driven at 600 DIP/s, clamped to 360–520 ms plus 50 ms hold and 90 ms fade; Standard remains 800 DIP/s, 260–380 ms plus 30/70 ms. Lock permits the active playback to finish but drops pending playback and defers COMMIT until the active pulse Completed callback.
- Full/Standard hard cutoffs are 4500/3620 ms. After cutoff, unresolved readiness receives one bounded 180/150 ms settle (Reduced/Classic 80 ms, Off 0). A real Sensor Bus or Projection update during settle is accepted; only an unresolved settle expiry produces Partial/TimedOut. No extra poll or hardware read is performed.
- Runtime coverage includes 1107×685, 1120×720, 1600×900, 36/48/72 DIP, same-Y/up/down routes, 0.5 DIP alignment, reveal late snapshots, delayed Clips, route continuity, ports, real-service Index ordering, value coalescing, PollingVersion reset, Projection-to-COMMIT, readiness settle, the static dormant channel and atomic rail. The suite total is `1857`; administrator EXE and manual visual/DPI acceptance were not run.
- Final isolated Release, Debug, test-build, two-process Release-run and empty-stderr evidence is recorded in Draft PR #9 and the final task report. CI remains attached to that open Draft PR; no merge, tag, or Release is part of this sequence.

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
