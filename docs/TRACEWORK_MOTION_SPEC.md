# TRACEWORK Motion Specification

> Current runtime motion authority for HardwareVision 2.0.3.

Static composition, typography, color, density and responsive roles are owned by [`../TRACEWORK_Design_Rules.md`](../TRACEWORK_Design_Rules.md). Startup detail is owned by [`STARTUP_SEQUENCE.md`](STARTUP_SEQUENCE.md).

## 1. Invariants

- One `MainWindow`, one `MainShellHost`, one `MotionTransitionHost` named `PageHost`, one `CurrentPage` binding and one App-owned service graph.
- Lazy page ViewModel caching is preserved. A transition may create presenters but never a second page ViewModel or parent one UIElement twice.
- FLOW RELAY phase order remains `Idle -> Route -> Shift -> Relay -> Settle -> Idle`.
- Business state commits in Route so the incoming page can load, arrange and render before decorative Shift/Relay. Visual Relay is not the business commit boundary.
- SYSTEM REWIRE has priority. Hidden/minimized/unloaded/closing paths restore a deterministic final state.
- Motion animates presentation only: no layout-property animation, scale, blur, shader, screenshot copy, VisualBrush, per-item row cascade or full-page Clip.
- Every clock, temporary presenter and callback is finite, versioned and cancellable.

## 2. Motion levels

New, missing or recovered settings request Full. Existing Full, Standard, Reduced and Off are preserved. OS reduce-motion may lower only EffectiveLevel.

| Level | Contract |
|---|---|
| Full | Complete FLOW RELAY band, PageRoot/Primary/Secondary hierarchy and spatial motion |
| Standard | Compressed but structurally identical hierarchy |
| Reduced | Bounded opacity-only transition, no spatial rail/translation/stagger |
| Off | Immediate commit with no visual clock |

Classic and Tracework use the same route/business/cache lifecycle. Classic uses its simpler visual resources without creating an alternate navigation service.

## 3. Semantic roles

| Role | Meaning | Runtime responsibility |
|---|---|---|
| PageRoot | Whole page transaction surface | Broad outgoing/incoming continuity |
| Primary | Page's main visual subject | Exits after Secondary and enters before Secondary |
| Secondary | Supporting context | Exits first and enters last |

Each Tracework page layout has one PageRoot, exactly one Primary and at most one Secondary. Repeated cards, item rows, sensors, table rows and chart points never receive these roles. Role references are resolved per committed content root and reused; snapshot hot paths do not repeatedly walk the visual tree.

## 4. Native first frame and startup

The single Window remains opacity-hidden and physically staged until the dark Tracework surface is committed at its final location. `SurfaceMeasured` and `FirstFrameGateReleased` are independent facts; Index waits for both. The state machine, compositor flush, bounded fail-open, SENSOR BUS Detail/port/route ordering and Pulse -> COMMIT -> Reveal contract are defined in [`STARTUP_SEQUENCE.md`](STARTUP_SEQUENCE.md).

Startup Projection may use one short-lived Rendering subscription owned by a generation. It must detach on every normal and abnormal terminal path. The first-frame gate itself does not use a permanent render loop.

## 5. FLOW RELAY business transaction

On a valid navigation request:

1. The service publishes Route and starts its bounded Route clock.
2. In the same asynchronous transaction, `CurrentPage`, selected navigation item, title/subtitle, route descriptor, persisted page key, old-page activation and target activation commit together.
3. `CachedPagePresenter` retains the outgoing presenter and attaches the incoming presenter immediately.
4. The incoming content loads, measures, arranges and renders while the outgoing content remains visible.
5. Shift and Relay provide the decorative continuation; visual Relay occurs at the plan CommitTime.
6. Settle completes incoming PageRoot/Primary/Secondary clocks.
7. Visible completion is published before ContextIdle cleanup releases temporary references.

The selected item may reflect the latest request immediately. Business commit is not delayed by the decorative 70/50 ms Route clock, and rapid replacement cancels obsolete decoration without undoing a completed valid page transaction.

## 6. Full plan

```text
Route    0–70 ms       business commit and incoming preparation
Shift   70–190 ms      outgoing hierarchy continues
Relay   at 190 ms      decorative relay boundary
Settle  190–410 ms     incoming hierarchy reaches final state
Finalize 410–420 ms    non-visible completion boundary
Total   420 ms
```

Outgoing:

| Surface | Delay | Duration | Opacity | Offset |
|---|---:|---:|---:|---:|
| PageRoot | 0 ms | 120 ms | 1 -> 0.32 | 6 DIP |
| Secondary | 0 ms | 88 ms | 1 -> 0.24 | 8 DIP |
| Primary | 24 ms | 96 ms | 1 -> 0.42 | 5 DIP |

Incoming from visual Relay:

| Surface | Delay | Duration | Opacity | Offset |
|---|---:|---:|---:|---:|
| PageRoot | 0 ms | 220 ms | 0.32 -> 1 | 8 -> 0 DIP |
| Primary | 20 ms | 180 ms | 0.42 -> 1 | 6 -> 0 DIP |
| Secondary | 66 ms | 190 ms | 0.24 -> 1 | 10 -> 0 DIP |

Outgoing uses acceleration; incoming uses deceleration. Full retains RelayBand, SignalRail cursor, telemetry translation, page translation and role stagger.

## 7. Standard plan

```text
Route    0–50 ms
Shift   50–140 ms
Relay   at 140 ms
Settle  140–300 ms
Finalize 300–320 ms
Total   320 ms
```

Outgoing PageRoot uses `1 -> 0.38` over 90 ms with 4 DIP; Secondary uses `1 -> 0.30` immediately over 66 ms with 6 DIP; Primary uses `1 -> 0.46` after 16 ms over 74 ms with 4 DIP.

Incoming PageRoot uses `0.38 -> 1` over 160 ms with 6 DIP; Primary uses `0.46 -> 1` after 14 ms over 138 ms with 4 DIP; Secondary uses `0.30 -> 1` after 44 ms over 146 ms with 7 DIP.

Standard retains the same semantic ordering and spatial components with shorter durations.

## 8. Reduced and Off

Reduced commits during Route, reaches visual Relay at 50 ms and performs a 100 ms PageRoot opacity settle for a 150 ms total. PageRoot uses `1 -> 0.58` outgoing and `0.58 -> 1` incoming. It has no Relay translation, SignalRail cursor, telemetry/page translation or role stagger.

Off commits immediately, publishes Idle and creates no motion clock.

## 9. Presenter overlap

`CachedPagePresenter` keeps outgoing and incoming presenters in one temporary Grid. The incoming presenter must be loaded, positively arranged and rendered before overlap evidence is accepted. Cleanup is generation-guarded and occurs after at least one real rendered overlap frame.

- The old page remains visible while the new page is prepared; there is no blank PageHost frame.
- Cleanup cannot remove the current presenter after rapid replacement.
- Completed outgoing clocks may already have been normalized before the test/cleanup observer runs; both the rendered intermediate evidence and legal cleaned terminal state are authoritative.
- The rendering observer is bounded to the overlap operation and always unsubscribed on completion, cancellation, unload, replacement or disposal.

## 10. Game report subroute

GameSessionReport is an independent CurrentPage content object, not an inline visual state masquerading as the GamePerformance page.

- Opening commits the report ViewModel, `GameSessionReport` route and Shell metadata together during Route.
- The GamePerformance navigation item stays selected because the report belongs to the Session/Game navigation group.
- The cached GamePerformance ViewModel remains the return target and retains recent-session list, pagination and scroll state.
- Closing uses the same FLOW RELAY transaction back to GamePerformance.
- A normal page request during report load is latest-wins; retiring report loading is cancelled or loses write permission and is disposed after the active navigation settles.

## 11. Startup Dashboard handoff

Reveal is an irreversible state commit. Full/Standard retain PageRoot -> Primary -> Secondary ordering while the startup overlay and Shell targets overlap. Reduced uses opacity-only; Off finishes immediately. Overlay collapse and ContextIdle cleanup are separate from the user-visible completion frame.

Late startup snapshots cannot restore Dashboard bases, replay Projection, relight COMMIT or clear a newer page transition.

## 12. Lifecycle and replacement

- Same-target requests reuse the current operation.
- A different pre-dispatch request cancels before commit; after commit, latest valid target wins and stale decoration converges to Idle.
- Resize records new geometry without cancelling valid active clocks.
- SYSTEM REWIRE/theme takeover cancels FLOW RELAY presentation and commits one coherent latest business state.
- Close/dispose invalidates generations, cancels owned work, unsubscribes events and observes background tasks.
- Background ViewModel dispatch uses nonblocking `BeginInvoke` with shutdown guards; no synchronous Dispatcher wait is permitted.

## 13. Performance boundary

- Navigation request paths perform no filesystem I/O, hardware read, unconditional `UpdateLayout`, collection rebuild or repeated visual-tree scan.
- Target ViewModel resolution and CurrentPage commit are independent of the decorative Route duration; the 60-switch production-clock fixture requires request-to-commit P50 below 50 ms and currently measures in the low-millisecond range.
- Page cache, role references and milestone presentation objects are reused.
- Temporary diagnostics are serialized off the request path and deduplicated.
- No long-lived CompositionTarget.Rendering subscription, decorative timer, layout animation or unbounded task is allowed.

## 14. Validation boundary

The automated baseline is `2637 passed / 0 failed / 2637 total`. It includes shown-Window visual-tree checks, real outgoing/incoming frame sampling, CurrentPage/DataContext/route/selection assertions, rapid latest-wins navigation, 2400+ stress iterations, startup Projection and cleanup, all motion levels, both themes, resize and disposal.

Automated rendering verifies WPF state and rendered fixtures. It does not certify subjective motion quality, DWM screen capture, every refresh rate, RDP/software renderer, real multi-monitor topology or hardware/provider combination.
