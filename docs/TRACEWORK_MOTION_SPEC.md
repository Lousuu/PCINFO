# TRACEWORK Motion Specification

> Current authority for the HardwareVision 2.0.2 motion candidate on Draft PR #10.
>
> Static composition, color, typography, density, and page-role intent remain owned by
> [`../TRACEWORK_Design_Rules.md`](../TRACEWORK_Design_Rules.md). This document derives
> only runtime motion, staging, timing, lifecycle, and cleanup rules from that static
> language. Where older motion values in `TRACEWORK_VISUAL_LANGUAGE.md`,
> `TRACEWORK_UI_HANDOFF.md`, `STARTUP_SEQUENCE.md`, or `HANDOFF.md` differ, this document
> supersedes those dynamic values. Historical release evidence remains historical.

## 1. Scope and invariants

- Preserve one `MainWindow`, one `MainShellHost`, one `MotionTransitionHost` named
  `PageHost`, one `CurrentPage` binding, and the existing page/ViewModel cache.
- Preserve FLOW RELAY's state order:
  `Idle -> Route -> Shift -> Relay -> Settle -> Idle`.
- `CurrentPage`, selection, navigation metadata, persisted page key, old-page
  deactivation, and target activation change together only at Relay.
- Preserve SYSTEM REWIRE priority, polling, providers, history, PresentMon, recording,
  reports, settings persistence, tray behavior, and version metadata.
- Animate presentation only. Do not animate layout properties, scale, blur, shader,
  `VisualBrush`, screenshot copies, per-item rows, or a full-page Clip.
- Every animation is finite, versioned, cancellation-safe, and reduced by the effective
  Motion profile.

## 2. Semantic motion vocabulary

TRACEWORK motion follows the same hierarchy as the static page:

| Role | Meaning | Runtime responsibility |
|---|---|---|
| PageRoot | The page transaction surface | Establish continuity and the broad enter/exit |
| Primary | The page's single visual subject | Exit after Secondary; enter before Secondary |
| Secondary | Supporting context | Exit first; enter last |

Each of the twelve Tracework page layouts has exactly one Primary role and at most one
Secondary role. Repeated cards, sensor rows, table rows, timeline points, and generated
items never receive either role. Role references are resolved once per committed content
root and cached for that content; snapshot handling does not repeatedly traverse the
visual tree.

## 3. Native first-frame gate

The sole startup Window remains opacity-hidden and outside the virtual desktop until the
dark surface has been committed at both the staging and final positions.

The current state machine is:

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

Terminal alternatives are `FailOpenReleased` and `Cancelled`.

1. Render #1 is the ordinary first WPF render boundary after `Show`.
2. Render #2 commits the dark surface while the HWND is hidden off-screen, then performs
   the off-screen `DwmFlush`.
3. The captured physical final placement is applied while opacity remains zero.
4. Render #3 commits the final-position frame, then performs the final-position
   `DwmFlush`.
5. Only the final flush may publish `FirstFrameGateReleased` and release opacity.

The final placement is captured once in physical coordinates, restored through the HWND,
and verified within one physical pixel before the final release. Cursor movement cannot
retarget the release. A generation-guarded 500 ms fail-open restores a usable final
placement and publishes an explicit fail-open reason. Closing cancels late callbacks.
Tray restore never restages the Window. No synchronous `Dispatcher.Invoke(Render)`,
timer, retry loop, or `CompositionTarget.Rendering` subscription is allowed.

## 4. Surface readiness and Index authorization

`SurfaceMeasured` and `FirstFrameGateReleased` are independent facts:

- `SurfaceMeasured` means the loaded Shell, PageHost, overlay, and positive layout have
  been observed.
- `FirstFrameGateReleased` means the final-position compositor boundary has completed or
  the bounded fail-open path has released the gate.
- `VisualReady` is derived only when both are true.

INITIAL TRACE may collect real milestones before either fact, but Index cannot start
until both facts are true. A failure releases the native gate for usability without
inventing surface measurement.

If an Index snapshot arrives before the gate, the overlay stores exactly the latest
pending Index snapshot. Once both conditions are true, it schedules one independent
Dispatcher Render callback and replays that snapshot. Duplicate callbacks, stale
generations, Reveal, Complete, cancellation, unload, and close invalidate the pending
replay.

## 5. SYS/BOOT.00 reveal

- Full: one horizontal local Clip, 180 ms.
- Standard: one horizontal local Clip, 120 ms.
- Reduced: opacity only.
- Off: immediate final state.

Full and Standard freeze one valid natural text width. The Clip begins empty, exposes
real non-empty/non-final intermediate Rect widths, reaches the full width, commits the
final Rect, and clears its clock. A first unstable layout may defer once to Render; it
must not rebuild the Clip during travel or call `UpdateLayout` from the snapshot hot path.

## 6. Startup Dashboard handoff

Startup Reveal coordinates the existing overlay, Shell regions, PageRoot, and semantic
Dashboard roles. The overlay never applies a full-page Clip.

### Full

| Time | Event |
|---:|---|
| 0 ms | Publish readable Reveal and start its hold |
| 120 ms | Hold ends; overlay fade starts; PageRoot and SignalRail start |
| 140 ms | Dashboard Primary and TelemetrySpine start |
| 185 ms | Dashboard Secondary starts |
| 190 ms | TimeRibbon starts |
| 300 ms | Overlay is collapsed on the visible completion boundary |
| 375 ms | Stable presentation boundary |
| 390 ms | ContextIdle cleanup may release references and clear residual clocks |

The overlay fade lasts 180 ms. PageRoot uses a bounded opacity/translate entrance;
Primary visibly precedes Secondary. Cleanup must not share the user-visible completion
frame.

### Standard

Reveal holds for 100 ms, the overlay fades for 150 ms, and the complete visible handoff
settles in approximately 310–330 ms. The same PageRoot -> Primary -> Secondary order is
retained with compressed timings.

### Reduced and Off

Reduced holds Reveal for 60 ms and uses opacity-only fades: overlay 100 ms and PageRoot
120 ms, without translation, clipping, or role staggering. Off commits and cleans up
immediately without animation clocks.

## 7. FLOW RELAY transaction

The route direction remains derived from page order/group. The old page exits in the
opposite spatial direction from the incoming page. Relay itself preserves brightness:
the new content is committed into a prepared non-flashing base state before its entrance.

### Full profile

```text
Route   0–20 ms
Shift   20–70 ms
Relay   commit at 70 ms
Settle  70–190 ms
Finalize 190–200 ms
Total   200 ms
```

Old content:

| Surface | Delay | Duration | Opacity | Offset |
|---|---:|---:|---:|---:|
| PageRoot | 0 ms | 100 ms | 1 -> 0.74 | 4 DIP |
| Secondary | 0 ms | 70 ms | 1 -> 0.68 | 5 DIP |
| Primary | 12 ms | 78 ms | 1 -> 0.80 | 3 DIP |

New content:

| Surface | Delay from commit | Duration | Opacity | Offset |
|---|---:|---:|---:|---:|
| PageRoot | 0 ms | 120 ms | 0.78 -> 1 | 5 -> 0 DIP |
| Primary | 10 ms | 100 ms | 0.84 -> 1 | 4 -> 0 DIP |
| Secondary | 34 ms | 106 ms | 0.72 -> 1 | 6 -> 0 DIP |

Exit uses an accelerating curve; entrance uses a decelerating curve. Interaction may be
restored immediately because the committed PageRoot starts above 0.70 opacity, while
final visual normalization continues to its bounded completion.

### Standard profile

```text
Route   0–10 ms
Shift   10–45 ms
Relay   commit at 45 ms
Settle  45–140 ms
Finalize 140–150 ms
Total   150 ms
```

Old content uses PageRoot `1 -> 0.80` over 70 ms with 3 DIP, Secondary
`1 -> 0.76` immediately over 48 ms with 4 DIP, and Primary `1 -> 0.84` after
8 ms over 54 ms with 2 DIP. New content uses PageRoot `0.84 -> 1` over 95 ms
with 4 DIP, Primary `0.88 -> 1` after 8 ms over 78 ms with 3 DIP, and
Secondary `0.80 -> 1` after 24 ms over 82 ms with 4 DIP.

### Reduced and Off

Reduced commits at the first available Relay turn and completes a 90 ms opacity-only
settle, with no spatial rail, Relay translation, PageRoot translation, Clip, or role
stagger. Off commits immediately with no visual clock.

### Relay overlap and first visible frame

The selected navigation item reflects the request immediately. `CurrentPage`, route
metadata, persisted page key, old-page deactivation, and target activation remain one
atomic Relay commit.

For an animated Relay, the cached outgoing and incoming `ContentPresenter` instances
share one temporary Grid for at least one real `CompositionTarget.Rendering` frame.
The incoming presenter must be Loaded and have positive arranged dimensions before that
frame is accepted. Cleanup runs after the rendered overlap frame and leaves only the
incoming cached presenter. The overlap never creates another page or ViewModel, never
parents one UIElement twice, and remains generation-guarded during rapid navigation.

The first visible incoming frame is therefore bounded by Relay plus the next available
render (approximately 120 ms Full and 90 ms Standard in the target environment), not by
the end of Exit, business refresh, or ContextIdle cleanup. The content surface never
drops below the profile's high committed base while the presenter is replaced.

## 8. Lifecycle and replacement

- Same-target requests reuse the current operation.
- A different pre-commit target cancels the stale request; latest valid target wins.
- A post-commit replacement starts from the newly real page.
- Hidden, minimized, unloaded, and SYSTEM REWIRE takeover paths restore baselines and
  complete the latest valid business transaction exactly once.
- Resize uses current dimensions and never mutates layout properties through animation.
- Close/dispose invalidates generations, cancels owned work, unsubscribes events, and
  leaves no unobserved task.
- A visible transition completion is published first; `DispatcherPriority.ContextIdle`
  performs non-visible cleanup and releases cached references afterward.

## 9. Performance boundary

- Snapshot application performs no unconditional `UpdateLayout`.
- The six startup milestone presentation objects are stable for the overlay lifetime;
  snapshots update their properties instead of recreating row containers.
- Milestone row references and per-page role references are cached.
- No snapshot performs a repeated visual-tree walk, collection recreation, timer tick,
  hardware read, or synchronous Dispatcher render.
- All clocks, Clips, transforms, opacity bases, hit testing, and temporary references
  have explicit completion, cancellation, and unload cleanup.

## 10. Validation and acceptance boundary

The automated candidate gate requires:

- 18 independently filterable static contract groups, reported as 18 tests
  rather than repeated string checks;
- Runtime XAML construction;
- clean isolated Release app, Debug app, and Release test builds;
- two independent complete Release runs with identical final totals and empty
  stderr;
- Advanced Sensors, SYSTEM REWIRE, and FLOW RELAY focused regression evidence;
- zero vulnerable and zero deprecated package findings;
- clean `git diff --check`;
- final Draft PR CI success.

Automated state and dark-surface contracts do not prove pixel-level acceptance. Manual
cold-start and navigation recordings remain required to judge the first visible client
frame, continuous SYS/BOOT.00 Clip, COMMIT weight, Dashboard handoff, role ordering,
Relay continuity, and perceived frame pacing. The candidate remains Open, Draft, and
Unmerged; no version change, tag, or Release is authorized.

## 11. 2.0.2 runtime wiring correction

The implementation at `7705779b66d978ec888257da448757723a5bea74` satisfied
many static contracts but did not reliably carry them into the live visual tree.
In particular, a normal `SizeChanged` cancelled Page enter clocks, every active
startup snapshot cleared the prepared Dashboard state, several page roles still
described the old hierarchy, and layout-dependent route geometry had no bounded
asynchronous readiness path.

The corrected runtime contract is:

- a valid host resize records diagnostics and lets the current Exit/Enter clocks
  continue; only invalid dimensions or an explicit lifecycle takeover cancel;
- navigation uses `Idle / Prepared / Exiting / Committed / Entering /
  Finalizing / Cancelled` states with version-guarded finalization;
- startup takes ownership once, preserves Full `0.32 / 0.42 / 0.24` and Standard
  `0.38 / 0.46 / 0.30` Root/Primary/Secondary bases through Index, Route, Bind,
  and Lock, then plays Reveal without first restoring the final state;
- the twelve Tracework page layouts carry semantic runtime roles. CPU now maps
  `CpuPrimaryChartField` to Primary and `CpuSecondaryRegion` to Secondary;
- SignalRail and Projection pulse validate Loaded/Arrange/ActualSize/
  PresentationSource/finite coordinates and use at most two
  `DispatcherPriority.Render` retries. No snapshot path restores unconditional
  `UpdateLayout`;
- the former 18 × 20 source checks are reported only as 18 independently named
  `Static contract guard` tests. Separate shown-Window WPF integration tests
  sample real old-page Exit, Relay content replacement, new-page Enter, resize
  continuation, startup preparation/handoff, Projection geometry, and
  latest-wins navigation.

Static checks do not establish visual acceptance. The candidate must be run from
`%TEMP%\PCINFO-2.0.2-motion-runtime-fix\Release\HardwareVision.exe`; the exact
commit, size, timestamp, SHA-256, PDB, and test assembly are recorded beside it
in `candidate-info.txt`. PR #10 remains Open, Draft, and Unmerged pending manual
cold-start and navigation recordings.

## 12. Final visual-stability correction

The human recording at `b75f200` overruled the earlier synthetic result: the
Projection pulse was absent on a real cold start, the Dashboard handoff still
contained a near-black/single-frame jump, Classic exposed the safety surface,
Classic-to-Tracework could expose a light strip, and Relay was too dark.

- Projection requests latch on a newer polling version, an increased resolved
  count, or the post-data-layout edge. A latched request survives Bind to Lock,
  coalesces to the latest data, and COMMIT waits for pulse completion. A 700 ms
  diagnostic fail-open prevents a permanent Lock.
- Reveal completion is visual, not merely logical. The whole overlay fades while
  Dashboard Root/Primary/Secondary and Shell targets run their real clocks; a
  rendered final frame is reported before logical completion. Full uses
  `0.32 / 0.42 / 0.24`; Standard uses `0.38 / 0.46 / 0.30`.
- Theme transition completion waits for the target DynamicResource, Chrome,
  ContentTemplate, PageHost geometry, opacity, transform, and Clip across two
  bounded rendered passes, with one third pass only after a real size change.
  The fail-open limit is 900 ms.
- `SafetyBackground` is always `#0B0E11`; the full-client `ThemeSurface` above it
  is the active `AppBackgroundBrush`. Classic therefore retains its original
  surface and gutters from baseline `ea22346bee9c3dde5db5e16b178e0333630ddd6d`,
  while Tracework retains a dark strip beneath its Chrome.

Automated static and real-WPF runtime evidence cannot replace the requested
cold-start, navigation, and theme-switch recordings. Final frozen-binary evidence
is two consecutive complete Release processes at `2525/0/2525`, both with empty
stderr. PR #10 remains Draft.

## 13. Relay performance stabilization after `0c7cd95`

Runtime inspection found two Relay-bound costs: lazy target ViewModel creation
inside the commit callback and duplicate role-tree resolution in both
`ContentChanged` and Loaded preparation. The target ViewModel is now resolved
while the old page is still stable, before Shift. Loaded role resolution is
coalesced per navigation version and only rewalks the visual tree when neither
role was available at the first pass. Animation parameters, the single
PageHost, and the atomic Relay boundary are unchanged.

Low-cost stage diagnostics record `NavigationRequested`,
`TargetViewModelResolved`, `PageExitStarted`, `PageExitFirstRender`,
`PageExitCompleted`, `RelayCommitStarted`, `CurrentPageAssigned`,
`ContentTemplateApplied`, `TargetLayoutCompleted`, `PageEnterStarted`,
`PageEnterFirstRender`, `PageOverlapFrameRendered`, `PageEnterCompleted`,
`DeferredWorkStarted`, and `DeferredWorkCompleted`. Final lifecycle cleanup remains at
ContextIdle; the bounded outgoing presenter cleanup occurs immediately after its one
rendered overlap frame. No full-tree `UpdateLayout`, screenshot surface, duplicate page
instance, or paused data source was introduced.

## 14. Projection race and COMMIT Render boundary

Projection pulse ownership is monotonic across Bind and Lock. A request that
arrives after Lock, or after Lock wins the first position in the same Dispatcher
turn, remains pending until geometry and the real visible-frame gate succeed.

COMMIT uses a separate `DispatcherPriority.Render` turn. Publishing Lock with
`CanCommit` does not synchronously create its clocks. With no pending or active
Projection pulse, the following Render turn creates one COMMIT presentation;
additional Render turns do not replace its start timestamp or center Clip
clock. With Projection work present, COMMIT remains blocked until
`ProjectionPulseCompletedAt < CommitVisualStartedAt`.

The 700 ms no-visible-frame fail-open and the 1500 ms post-visible animation
completion guard are terminal bounds, not alternate animation timing. Automated
tests cover their ordering but do not replace the required human cold-start
recording.

## 15. Projection composition presentation boundary

The cold-start recording after `a990993` showed that the two layout retries
could finish and fail open before geometry settled. WPF property state also did
not establish that a customer-visible pulse survived long enough to be captured.
Projection motion therefore uses this presentation sequence:

1. prepare the existing route and one-DIP initial Clips without starting clocks;
2. attach one generation-owned rendering handler only while the loaded overlay,
   Window, presentation source, and geometry are valid;
3. use the first rendering callback to observe composition and start the
   existing pulse animation;
4. record the first post-start rendering time;
5. require a second distinct post-start rendering time plus a valid visible
   segment before committing the visible frame;
6. retain the route for at least 180 ms in Full or 140 ms in Standard, measured
   from the first post-start render;
7. complete only when animation completion, minimum visibility, and visible
   frame commitment are all true;
8. schedule COMMIT through its independent Render-turn evaluation.

If the animation completes before the minimum interval, its existing route is
held visibly without changing geometry, color, line width, head size, or normal
animation timing. Duplicate rendering times never advance the state. A newer
generation invalidates old callbacks; a newer lower-count polling version may
remain as the sole coalesced replay while the current pulse finishes.

The request-relative 700 ms fail-open covers missing layout, presentation
source, visible host, composition rendering, or visible segment. The 1500 ms
guard covers a pulse that committed a frame but did not complete normally.
Both paths detach the handler and release COMMIT without reopening startup.
Normal completion and every Reveal, restore, cleanup, unload, takeover, Motion
Off, completed snapshot, collapse, or generation replacement also detach it.

`CompositionTarget.Rendering` is a WPF rendering lifecycle signal, not a claim
that DWM displayed or a recorder captured the frame. `RenderTargetBitmap`
evidence likewise protects the visual tree only. The final automated gate used
one frozen binary for two `2556/0/2556` Release processes with exit 0 and empty
stderr; manual cold-start recording remains required.
