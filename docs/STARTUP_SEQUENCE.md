# INITIAL TRACE startup sequence

This document is the HardwareVision 2.0.3 startup authority. Runtime page motion is specified in [`TRACEWORK_MOTION_SPEC.md`](TRACEWORK_MOTION_SPEC.md).

## Purpose and ownership

INITIAL TRACE is a bounded presentation of real application readiness. It never invents progress and never starts a second hardware scan.

- `App` owns one `StartupSequenceService` and starts it once after the single `MainWindow.Show()` returns.
- The existing `MainWindow`, `MainShellHost`, `PageHost`, `MainViewModel`, Polling loop, history buffer and cached Dashboard remain authoritative.
- The overlay is a child of `MainShellHost`; it does not create another Window, Shell, page, ViewModel or service graph.
- Startup snapshots are immutable, versioned and monotonic. Stale versions and callbacks after completion are ignored.

## State and readiness

The logical order is:

```text
Dormant -> Index -> Route -> Bind -> Lock -> Reveal -> Complete
```

Real milestones cover theme resources, service graph, page router, SENSOR BUS, history buffer and Shell surface. `VisualReady` requires two independent facts:

- `SurfaceMeasured`: the loaded Shell, PageHost and overlay have positive arranged dimensions.
- `FirstFrameGateReleased`: the final-position compositor boundary completed or the bounded fail-open released it.

An Index snapshot that arrives early is retained as the latest pending generation and replayed on a separate Render-priority turn. Failure cannot fabricate surface measurement.

## Native first-frame gate

Tracework with motion enabled uses the sole Window and the following finite state machine:

```text
Dormant
-> NativePrepared
-> ShownHiddenOffscreen
-> FirstOffscreenRenderCommitted
-> OffscreenCompositionFlushed
-> FinalPlacementAppliedHidden
-> FinalPositionRenderCommitted
-> FinalPositionCompositionFlushed
-> Released
```

`FailOpenReleased` and `Cancelled` are terminal alternatives. Final placement is captured once in physical coordinates and applied through the HWND; cursor movement cannot retarget release. The 500 ms generation-guarded fail-open restores a usable placement. Closing invalidates late callbacks, and tray restore never restages the Window.

The gate uses Dispatcher Render boundaries and `DwmFlush`, not a permanent rendering subscription, retry loop or synchronous Dispatcher render. Classic and Motion Off bypass the visual gate safely.

## Dashboard source lifecycles

The initial Projection contains CPU, GPU, Memory, Disk, Network and System slots.

- Sensors resolve CPU/GPU/Memory only after their first UI apply or explicit polling failure.
- Disk waits for its initial disk refresh, Network for `NetworkAdapterService`, and System for `HardwareSnapshot`.
- A source remains Pending until it completes or fails. Missing values after completion become Unavailable; Unsupported/Failed require explicit evidence.
- One coalesced Dashboard UI batch publishes at most one changed Projection, so 0/6 may legally become 6/6 without fabricated intermediate steps.
- States do not regress within a PollingVersion. A newer PollingVersion can establish a new data baseline but cannot reopen a completed startup sequence.
- Only the existing startup hard cutoff may convert unresolved slots to TimedOut.

No startup milestone waits for Advanced Sensors, PresentMon, multiple history samples or a second poll.

## SENSOR BUS to Projection

The v2.0.3 visible order is strict:

```text
terminal SENSOR BUS snapshot
-> final Detail text transition completes
-> one Render-priority final-layout confirmation
-> Projection value/anchors resolve
-> SENSOR BUS output port enters
-> Projection input port enters 25 ms later (Full/Standard)
-> both port animations complete
-> a real Rendering frame observes both ports fully visible
-> dormant route may appear
-> active Projection route and Pulse may start
```

The output port belongs immediately after the rendered Detail text, not at the route row's far edge. Detail ellipsizes within its responsive cap. Geometry is calculated from the actual center of the two 6×6 ports in `OverlayRoot` DIP coordinates.

The final Detail text, port position and route therefore share one stable layout. Layout pending receives two bounded Render confirmations and two short-lived `LayoutUpdated` passes; ContextIdle detaches the handler. There is no polling loop or unconditional snapshot `UpdateLayout`.

## Projection route and Pulse

A valid route requires loaded, arranged, presentation-connected ports, finite coordinates and positive horizontal space. Source horizontal, vertical bridge and target horizontal are independent one-DIP segments with local Clips. Full alone uses the 5×5 moving head.

Projection presentation follows:

```text
Request
-> Geometry Prepare
-> Composition Wait
-> animation start
-> first post-start RenderingTime
-> second distinct post-start RenderingTime
-> visible-frame commit
-> minimum-visible hold
-> animation completion
-> Projection completion
-> independent Render-turn COMMIT
```

- Full/Standard route speeds remain 600/800 DIP/s within their bounded duration ranges.
- Full/Standard minimum visibility is 180/140 ms from the first post-start render.
- Completion requires animation complete, visible frame committed and minimum visibility reached.
- The first legal six-of-six cold-start state authorizes at most one Pulse. Duplicate snapshot, later PollingVersion, theme cycle, restore, stale callback and post-Reveal refresh cannot replay it.
- An active Pulse may finish after Lock; no second pending Pulse starts after Lock.
- Motion Reduced and Off do not run the spatial Pulse; Off uses the direct completion path.

Every completion, fail-open, Reveal, restore, unload, takeover, generation replacement and dispose path detaches the short-lived Rendering/Layout handlers before clearing visuals.

## COMMIT and Reveal

Lock plus `CanCommit` never starts COMMIT synchronously in the snapshot callback. It schedules an independent Render-priority evaluation.

- If Projection work is pending or active, COMMIT waits.
- The 700 ms no-visible-frame fail-open is armed from the Lock deferral, so a valid late Bind request receives a full presentation budget.
- If a visible frame committed but normal animation completion is lost, a separate 1500 ms completion guard settles the state.
- Normal ordering is `ProjectionPulseCompletedAt < CommitVisualStartedAt`.
- Full/Standard establish COMMIT over 180 ms; Reduced uses 90 ms. Stable holds are 480/360/180 ms, then the single `CommitExitRoot` exits over 90 ms.
- Reveal is irreversible. It atomically presents `05 / 05 REVEAL`, stops Projection, clears pending phase work, begins the overlay/Shell/Dashboard handoff and prevents later snapshots from relighting startup visuals.

## Motion profiles

- Full: complete Index/Route/Bind/Lock choreography, final Detail sequencing, ports, dormant line, moving Pulse, COMMIT and semantic Dashboard handoff.
- Standard: compressed choreography and line animation without the Full moving-head emphasis.
- Reduced: opacity-only bounded presentation; no spatial route, translation, Clip travel or role stagger.
- Off: no visual clock; state and Shell become final immediately.
- Classic: bounded plain reveal and no Tracework startup overlay choreography.

Requested motion comes from settings; OS reduce-motion can lower only the EffectiveLevel.

## Lifecycle and failure policy

- Full completion, hidden/minimized completion, failure, cancellation, overlay unload, Window close and App shutdown are finite terminal paths.
- Cancellation sources and observed tasks have one owner and are disposed.
- Subscriber exceptions are isolated and deduplicated in local logs.
- Animation clocks, Clips, transforms, opacity, visibility and hit testing are restored deterministically.
- Optional provider or layout failure changes only the affected milestone/visual path; it cannot block the main window indefinitely.
- Startup never waits for PresentMon and never launches an administrator child process.

## Validation boundary

Automated coverage uses real shown WPF Window fixtures for first-frame state, surface readiness, final SENSOR BUS Detail, port order, stable geometry, two distinct render times, Pulse-once, Lock races, COMMIT ordering, no-relight, Reveal, theme switching and cleanup. DPI and multi-monitor placement logic covers 100/125/150/175/200% and negative/adjacent monitor coordinates.

The v2.0.3 baseline is `2637 passed / 0 failed / 2637 total`. These tests verify the WPF visual tree and lifecycle; they do not prove DWM capture, subjective motion quality, every real monitor/RDP/software-rendering environment or every hardware/provider combination.
