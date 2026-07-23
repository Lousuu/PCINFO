# TRACEWORK Visual Language

## v2.0.1 final startup surface and atomic-rail rules

- The first physical surface is machine black `#0B0E11` before `Show()`: Window, outer root, Shell host and startup overlay agree without waiting for a theme resource. The Window remains opaque.
- Ledger text owns one left edge. `INITIAL PROJECTION` and its value align with NODE / LAUNCH / THEME / MOTION / VERSION; the input port is a separate mark 12 DIP to the left. SENSOR BUS Detail owns only its visible/capped width, followed by a 16 DIP gap and the fixed output port.
- The established source-right / vertical-up / target-right route and 5×5 moving head remain the active one-shot signal. A static low-energy copy shares the exact route geometry underneath it: 0.12 Full/Standard, 0.08 Reduced, visible from Ledger Ready through Lock, and gone at Reveal. It never breathes, scans, flashes, loops or polls.
- COMMIT is a readable lock state, not a port flash. It waits for the Projection pulse, builds to 0.70, holds for at least 350/250/180 ms after its Full/Standard/Reduced establishment, and exits in 90 ms. Lock budgets are 1250/950/360 ms. Early Reveal compensation is a bounded one-shot animation completion (200/150/80 ms maximum); failure is never delayed.
- Bottom-rail edge typography is symmetric: both edge labels are 11 DIP Bold with 18 DIP line height on the same 20 DIP row; the center remains 15 DIP SemiBold. Previous/Current Text and Previous/Current Code are one visual transaction with the phase track. No incoming code may precede its incoming text, and no outgoing text may remain paired with a newer code.
- Dedicated first-frame, alignment, dormant-channel, source-port, COMMIT, rail-style and atomic-transition groups each pass 20/20 and increase coverage from 1717 to 1857. Final isolated builds, two full runs and CI are owned by Draft PR #9. Advanced Sensors, SYSTEM REWIRE, Route, values, readiness settle, reveal order, single Shell/PageHost/service and business polling remain unchanged.
- This pass performs no redesign, merge, Ready transition, tag, Release, formal administrator EXE launch, screenshot analysis, manual visual acceptance or real-DPI validation.

## v2.0.1 final INITIAL TRACE runtime language

- Reveal is a one-way state, not another replayable animation. Late snapshots may refine text but never relight Background, Content, Bottom Rail, COMMIT, or Projection after the exit has begun.
- A delayed trace is physically absent until its delay. Every Clip begins with an empty Rect at 0 ms, holds that Rect through the delay, reveals in travel direction, commits final geometry, and clears its clock. Route connections propagate only downward; adjacent rows never grow toward each other.
- The Route rhythm is Full 205 ms per row / 1220 ms phase and Standard 120 ms / 720 ms. Source and target ports are visible only when their owning signal has arrived: SENSOR BUS reaches 0.35 at its node and 1 in Bind; Projection Input reaches 1 only after Ledger Ready.
- INDEX is a guaranteed visible state. The rail is ready at 180/140 ms Full/Standard, holds INDEX for at least 120/160 ms, and drains a monotonic `Index -> Route -> Bind -> Lock -> Reveal` queue without duplicates or regressions.
- Projection geometry uses logical DIP and a 0.5 DIP alignment grid. Same-Y routes tolerate at most 1 DIP; bent routes begin at 36 DIP, use a 12 DIP compact corridor minimum below 72 DIP and 24 DIP at/above 72 DIP. Live 1107×685, 1120×720, and 1600×900 layouts must all retain a route.
- Projection data is a two-layer coalesced transition, never a stack of ghost values. A PollingVersion change establishes a new zero baseline and invalidates stale value/pulse generations. Lock completes one active pulse before showing COMMIT, starts no replay, and Reveal remains the cleanup fallback.
- Hard cutoff is followed by one bounded readiness settle, so real Sensor Bus/Projection data arriving at the boundary wins over a synthetic Partial. This changes no polling cadence, scan, provider, PageHost, Shell, Advanced Sensors, or SYSTEM REWIRE behavior.
- Automated coverage is `1857`, with eight focused 20/20 runtime-stabilization groups, seven focused 20/20 presentation-polish groups, and retained fail-open, cold-template, nested-scroll, polling, and architecture checks. No formal administrator EXE or manual visual/screenshot validation was performed.
- Final Release/Debug/test builds, two isolated Release test runs and empty-stderr evidence are recorded in Draft PR #9 and the final task report. The existing PR owns CI and remains Open, Draft, and Unmerged.

## v2.0.1 corrected INITIAL TRACE motion rules

- Signal routes must terminate at explicit ports. INITIAL TRACE uses one visible `6×6` output port on SENSOR BUS and one matching input port beside the Projection value; a floating line ending at a transparent layout point is not acceptable.
- A bent route is three independent 1 DIP segments, never one Path under a whole-route horizontal Clip. The bridge sits at the clamped midpoint corridor, and each segment reveals in travel order with upward/downward direction preserved.
- Full route speed is 600 DIP/s and Standard is 800 DIP/s within their fixed bounds. Only Full carries a `5×5` square signal node; the node may trail an established segment but may never advance ahead of it or deform into a horizontal bar on the bridge.
- Projection data and Projection route are separate channels. Real values update immediately; the route waits for the ledger, cannot be interrupted, and coalesces bursts into one current and at most one latest replay. Pre-Bind resolved data is deliberately replayed from the last visually presented count.
- Route rows obey `Upper -> Node -> text -> lock -> Lower`, with 170 ms Full and 110 ms Standard row starts. Pending feedback uses its own frame. The phase clock must contain all six rows before Bind.
- Reduced restores title/subtitle visibility and remains opacity-only. The first Index rail phase waits until the rail is visible. Reveal has priority over all queued Projection visuals and clears them before the existing Shell reveal sequence.
- Current automated coverage is `1637` with 20-case WPF/STA repetition groups for geometry, queueing, Route choreography and Bottom Rail/Reduced. Draft PR #9 holds CI evidence. Manual visual acceptance, screenshot analysis and formal administrator EXE launch remain unperformed.

## v2.0.1 visible-time and cold-template rules

- Startup visual duration is measured from the user-visible, template-ready surface, never from background service construction.
- The startup background, content and bottom rail must exit concurrently with Shell reveal; an opaque overlay may not conceal the motion it coordinates.
- Long-running background initialization cannot consume the visible one-shot phase durations.
- The first SYSTEM REWIRE and all later transitions are structurally identical: cold template state is warmed or replayed, not visually degraded.
- A Tracework motion-enabled launch owns the first frame with a static cover; Classic and Off do not. The main interface must not flash before INITIAL TRACE.

> Status: the TRACEWORK visual language, Stage 6, and INITIAL TRACE startup sequence are complete for HardwareVision 2.0.0. Automated performance/lifecycle validation is complete; manual visual and real-DPI validation remain outside the automated boundary.

## INITIAL TRACE and 2.0.0 final contracts

- INITIAL TRACE uses the same 12-column logic-led grid as the application: left milestone axis, central system index and six-row status matrix, right launch/theme/motion/version ledger, and a bottom state rail. There is no centered logo, circular spinner, fabricated percentage, screenshot copy, or decorative third-party asset.
- Status is communicated by fixed text, node shape, and semantic color together. Pending uses telemetry cyan, Ready uses success mint, Partial uses attention amber, Failed uses alert coral, and dormant structure stays neutral.
- Overlay priority is fixed at Z=120, above SYSTEM REWIRE at 100 and FLOW RELAY at 40. The overlay is non-focusable, exposes one assertive live-region announcement, and blocks pointer interaction only while active.
- Full motion uses a horizontal index reveal, one bounded moving signal pulse, ordered shell-region clip/opacity reveals, and one commit-lock flash. Standard compresses the sequence and omits the internal moving pulse. Reduced is simultaneous short opacity only. Off is immediate. No scale, blur, shader, layout-property animation, screenshot, or VisualBrush is used.
- Diagnosis Summary, GPU selector, Dashboard primary instrument, and Advanced Sensors status rules are final 2.0.0 layout/state contracts. Sparse content is never stretched by an inherited minimum or star-sized final template row; selectors use one aligned full-width control.
- Automated coverage totals `1366` tests. Manual visual acceptance, screenshots, real-DPI environments, and administrator EXE launch were not used.

## Stabilization rules

- GPU metric matrices use an adaptive maximum of two columns. Each metric has adjacent `Auto / 12 / Auto / *` columns in a shared-size scope: label and value align locally and values must not be pushed to the far edge by a star spacer. Selector/identity and telemetry/charts occupy independent vertical column stacks.
- Network type and state badges use one rectangular geometry (`74` minimum width, `28` height, `10,0` padding, corner `1`). State color is driven by the real `Device.IsUp` boolean; localized status text is presentation only. Hidden metric containers collapse before `UniformGrid` placement so hidden values never reserve holes.
- Session Report uses two independent vertical stacks in Wide/Standard. A low-content panel must never share a row-height calculation with a tall chart/timeline panel. DIAGNOSIS SUMMARY is top-aligned and content-sized; Compact/Narrow uses the fixed diagnostic/timeline/performance/chart/limits/hardware order via shared templates.
- Settings does not use a static category index without selection, navigation, command, or accessibility behavior. The real control workspace is full width and is not wrapped in an additional decorative frame.
- Tracework scrollbars appear only when scrolling is required (`Auto`), but their track and rectangular Thumb remain visible without hover. Thumb minimum length is 32 DIP; hover uses identity and dragging uses telemetry. Classic obtains its rounded geometry from the same resource surface.
- Sparse panels must not be stretched by unrelated content through a shared maximum row height. Prefer independent vertical stacks where two editorial columns have different content density.
- Automated stabilization and release-readiness coverage totals `1366` tests. Clean Release and Debug application builds pass with zero warnings and errors. Manual visual and real-DPI validation remain separate acceptance work.

## 1. Purpose and ownership boundary

This document defines the independent static visual language of TRACEWORK / HardwareVision. It borrows only high-level ideas—logic-led composition, editorial hierarchy, controlled asymmetry, in-world instrumentation, and contrast in information density. It does not reproduce or depend on any Arknights, Hypergryph, Rhodes Island, third-party logo, icon, font, illustration, texture, copy, or promotional composition. No external proprietary asset was downloaded or added.

The pilot changes presentation only. FLOW RELAY, SYSTEM REWIRE, the single PageHost and CurrentPage binding, page lifecycle, page ViewModel caching, hardware acquisition, Polling, sensor/GPU history, PresentMon, the recorder, session formats, settings persistence, and tray behavior remain authoritative and unchanged.

## 2. Problem statement and design principles

The former Dashboard and CPU Tracework layouts overused equal-width, fully bordered panels. Repetition flattened hierarchy, made every signal appear equally important, and left the dark/grey/violet palette without a distinct telemetry or state vocabulary.

The reconstructed language follows these rules:

- logic decides decoration and content decides composition;
- every page has one visual subject;
- a strict grid permits controlled asymmetry, never arbitrary offsets;
- whitespace is structural, not leftover space;
- color communicates identity, telemetry, or real state;
- open and soft fields are the default; raised panels require a boundary reason;
- real data and existing projections drive every visible instrument.

## 3. Editorial layout and logical grid

The page content grid uses a 12px column gap and a 4px base vertical rhythm. Normal module separation is 16px; major sections use 24px.

| Internal content width | Mode | Logical columns | Rule |
|---|---:|---:|---|
| `>= 1360` | Wide | 12 | Full controlled asymmetry |
| `960–1359` | Standard | 8 | Preserve primary/secondary proportion |
| `680–959` | Compact | 4 | Stack primary and auxiliary regions |
| `< 680` | Narrow | 1 | Single column; no horizontal overflow |

`TraceworkResponsiveGrid` performs deterministic Measure/Arrange from the explicit internal content width. It uses attached placement properties for each mode, contains no timer or animation, clamps spans to the active column count, and ignores collapsed children.

## 4. Semantic color system

### Neutral foundation

| Role | Color |
|---|---|
| Canvas | `#0B0D10` |
| Surface | `#12171C` |
| Raised | `#182027` |
| Soft | `#202932` |
| Divider | `#34414B` |
| Trace Grey | `#64717B` |
| Dust | `#939CA3` |
| Paper | `#E7E4DD` |
| Secondary text | `#B2BAC0` |

### Information and state

| Role | Color | Meaning |
|---|---|---|
| Identity / IonViolet | `#8B7CFF` | Brand identity, page index, primary structure, selection |
| Telemetry / SignalCyan | `#58C4D6` | Charts, live readings, sample paths and windows |
| Active / PhosphorMint | `#79D9B1` | Real active, connected, locked, committed or successful state only |
| Attention / WarmAmber | `#D6A75B` | Real warning, limit, degradation or pause only |
| Fault / AlertCoral | `#D46A6A` | Real failure, disconnection, hazard or abnormal threshold only |

Neutral surfaces should occupy roughly 75–85% of the first view. Identity is normally 8–10%, telemetry 5–8%, active 1–3%, and attention/fault approach zero unless real state requires them. Mint, amber, and coral are never ambient decoration. Each semantic color has a corresponding brush and a low-opacity soft brush.

`TraceworkTechnicalGridBrush` is a static 24px tiled DrawingBrush with opacity `0.03`. It is limited to large open instrument fields and has no timer or animation.

## 5. Typography hierarchy

| Style | Size | Use |
|---|---:|---|
| `TraceworkDisplayValueTextStyle` | 48 | The page's unique primary live value |
| `TraceworkPageIndexTextStyle` | 36 | Page/device index |
| `TraceworkSectionTitleTextStyle` | 16 | Major region title |
| `TraceworkBodyValueTextStyle` | 12 | Main values and parameters |
| `TraceworkAnnotationTextStyle` | 9 | Source, mode, unit, window and supporting annotation |

Annotation text does not carry critical interaction. Long device names retain trimming, ToolTip, and accessible naming.

## 6. Surface and container rules

Four surface levels are available: Canvas, Open field, Soft field, and Raised technical panel. Open fields carry the page subject or primary value; soft fields group auxiliary channels; raised panels are reserved for operations, independently bounded devices, scrolling tables, report summaries, or other complex regions with a genuine boundary requirement.

The existing `TraceworkPanel` and old style keys remain compatible. `TraceworkTechnicalPanelStyle` narrows their intended use instead of deleting them.

## 7. Shared visual primitives

- **InstrumentField** — one primary live value in a large open field, using a local axis/edge rather than a generic panel header.
- **DataRail** — NODE, PLATFORM, UPDATED, STATE, DEVICE, TOPOLOGY, CHANNELS, and WINDOW share a horizontal or vertical baseline; items do not become independent cards.
- **AnnotationRail** — a narrow non-interactive source/index/mode rail. Decorative parts are non-focusable and do not hit-test.
- **ChartField** — one large primary chart with current value and a shared AVG/MIN/MAX baseline; auxiliary channels use compact micro charts.
- **SignalMatrix** — shared headers, weak row separation, aligned current values, preserved scrolling and long-text tooltips; rows are not cards.
- **TechnicalPanel** — the backwards-compatible fully bounded container, used only where interaction, scrolling, or complex containment justifies it.

## 8. Dashboard pilot: SYSTEM STATE

Wide mode uses a 7/5 split: CPU is the single primary InstrumentField on the left; GPU telemetry, Memory/Disk technical modules, and open Network/System modules form the secondary right column. Standard compresses to 5/3. Compact and Narrow stack the primary and secondary regions. NODE, PLATFORM, UPDATED, and STATE use one wrapping DataRail below the main composition.

All existing semantic cards, metrics, visibility rules, hardware selectors, two-way selection, ToolTips, automation names, and data references remain. Classic Dashboard is byte-identical to the pilot baseline.

## 9. CPU pilot: PACKAGE TELEMETRY

Wide mode uses a 4/8 split: package identity, primary InstrumentField, DEVICE/TOPOLOGY/CHANNELS/WINDOW rail are on the left; `Charts[0]` is the primary ChartField on the right. Standard uses 3/5. Compact and Narrow stack these regions. `Charts[1]`, `[2]`, and `[3]` remain the same ViewModels and Values collections but render as compact auxiliary channels. CoreRows remains a bounded SignalMatrix.

`CpuName`, `CoreThreadSummary`, `SelectedChartWindowSeconds`, `MetricProjection`, `Metrics`, `Charts`, Values/ranges/statistics, and `CoreRows` bindings are preserved. No chart, history source, subscription, timer, Polling path, or ViewModel was added. Classic CPU is byte-identical to the pilot baseline.

## 10. Accessibility and WPF performance boundary

- Key body text targets 4.5:1 contrast where practical; deterministic relative-luminance tests cover core semantic pairs.
- Identity, telemetry, and real-state meaning do not depend on color alone.
- Device selectors and long identity text retain ToolTip and AutomationProperties.Name.
- Decorative rails and grid texture are non-interactive; annotation does not enter the primary interaction sequence.
- Reduced and Off motion profiles do not alter static layout.
- The pilot adds no DispatcherTimer, CompositionTarget rendering loop, blur, shader, VisualBrush page copy, screenshot, external bitmap, or third-party UI dependency.
- Responsive layout is a lightweight Panel and performs only deterministic Measure/Arrange.

## 11. Full page prototype map

| Page | Single subject | Wide composition | Density and primary responsibility |
| --- | --- | --- | --- |
| Dashboard | SYSTEM STATE | 7/5 | Editorial system overview; unchanged pilot |
| CPU | PACKAGE TELEMETRY | 4/8 | Instrument/identity versus primary chart; unchanged pilot |
| GPU | RENDER PIPELINE | 8/4 | Render telemetry/chart versus selected adapter identity; sensor matrix below |
| Memory | MEMORY TOPOLOGY | 7/5 | Capacity relationship versus real module/channel topology; module matrix below |
| Disk | STORAGE HEALTH | 5/7 | Array identity versus capacity/health; real device/partition topology below |
| Network | LINK TELEMETRY | 8/4 | Current throughput/link field versus adapter identity; address matrix below |
| Motherboard | PLATFORM IDENTITY | 7/5 | Low-density board identity versus firmware/UEFI; platform sensors below |
| Advanced Sensors | SENSOR MATRIX | Top rail + matrix | Four responsive source/policy cells above a bounded high-density recycling SignalMatrix |
| Game Performance | CAPTURE CONTROL | 5/7 | TARGET PROCESS ControlWorkspace versus live KPI/chart; session/limits below |
| Session Report | SESSION DIAGNOSIS | 4/8 | Summary/findings versus real timeline, chart and limit events |
| Settings | SYSTEM CONTROL | 3/9 | Static category rail versus continuous settings ControlWorkspace |
| Metric Visibility | METRIC ROUTING | 3/9 | Category summary versus virtualized metric control matrix |

Each Wide composition has its own semantic ratio. Standard keeps the same primary/secondary relationship in eight columns. Compact stacks into four columns. Narrow is one column. The order follows reading priority, never preserves a forced multi-column layout, and never enables page-level horizontal scrolling.

## 12. Expanded shared primitives

- `CapacityField`: one primary total and a small number of neutral/SignalCyan capacity relationships. Capacity is not intrinsically warning or failure.
- `TopologyMatrix`: shared rows/nodes for real modules, devices, partitions, adapters, and addresses. It has no invented slots or random connector lines.
- `ControlWorkspace`: an index/category rail plus a bounded interaction surface. It reuses existing commands, focus order, two-way bindings, and persistence timing.
- `TimelineField`: a report-only field using existing timestamps, selected chart data, and explicit recorded limit events. It creates no inferred event.
- `IdentityPlate`: a low-density device/platform label using only existing name, stable identity, driver, firmware, interface, or source values.

The corresponding resource keys are `TraceworkCapacityFieldStyle`, `TraceworkCapacityTrackStyle`, `TraceworkCapacityUsedBrush`, `TraceworkCapacityFreeBrush`, `TraceworkTopologyMatrixStyle`, `TraceworkTopologyNodeStyle`, `TraceworkControlWorkspaceStyle`, `TraceworkControlRailStyle`, `TraceworkTimelineFieldStyle`, and `TraceworkIdentityPlateStyle`. They add no Timer, subscription, loop, third-party dependency, focus stop, or automation duplicate.

## 13. Page-specific prohibitions and state boundaries

- GPU does not copy CPU 4/8, create a new history service, or synthesize renderer state.
- Memory does not invent empty DIMM slots or remap reported memory type/form factor values.
- Disk does not synthesize a selector, health verdict, warning threshold, or nonexistent history chart.
- Network does not create traffic, topology lines, or status color without the real adapter state.
- Motherboard does not inflate sparse identity data into decorative charts.
- Advanced Sensors does not infer warning thresholds from formatted readings or disable recycling virtualization. Its page ScrollViewer owns header/rail/panel positioning, while the DataGrid retains its internal virtualized scroll range and forwards wheel input only at a real top/bottom boundary.
- Game Performance retains TARGET PROCESS 64px controls, 5*:9* inputs, 28px gap, ComboBox arrow allocation, four commands, capture lifecycle, and original samples.
- Session Report retains the real model/storage format, selected chart, limit-event data, and nested-wheel boundary behavior.
- Settings adds no category navigation state and does not treat every enabled Toggle as Mint success.
- Metric Visibility retains item order, two-way visibility, bulk actions, and persistence; a visible metric is an IonViolet selection, not success.

Amber and Coral are never used as general decoration. Mint is reserved for existing active/connected/success facts. Identity and ordinary selection use IonViolet; sampled/live numeric information uses SignalCyan. Page XAML contains no literal hexadecimal colors.

## 14. Automated validation and implementation state

The baseline remains `1006 passed, 0 failed, 1006 total`. The full expansion adds 130 cases for page subjects, prototype grids, binding manifests, role limits, forbidden effects, new primitive semantics, runtime XAML construction, explicit internal widths `1600`, `1366`, `1100`, `920`, and `679`, horizontal bounds, and normalized SHA-256 protection of all ten additional Classic layouts. The resulting suite contains `1136` cases. One intervening complete process reproduced the already documented transient `Report accuracy 01` timestamp case at `1135/1/1136` (`expected 10, got 5`); report/session source was not changed, and both subsequent complete Release processes passed `1136/0/1136`.

Clean Release and Debug application builds, two independent full Release processes, `git diff --check`, final pushed head, and CI are recorded in Draft PR #7 and the final task report. The one PageHost, one `CurrentPage` binding, page ViewModel cache, FLOW RELAY phases/commit, SYSTEM REWIRE, polling, history, PresentMon, recorder, report formats, and settings persistence remain unchanged.

Manual visual acceptance, screenshot analysis, real-DPI validation, and formal administrator EXE validation were not performed. The exact final full commit SHA cannot be self-recorded inside that same commit; it is recorded in Draft PR #7 and the final task report.

## 15. INITIAL TRACE startup visibility correction

- The motion-enabled Tracework first frame is a static machine-black cover only. Dormant is a transient readiness state, not a long-lived visible composition: route rows, Phase, COMMIT, bottom rail, and the dashboard remain hidden until Index.
- The route matrix has exactly one ItemsControl. Every milestone row owns its 24 DIP track column, 1 DIP vertical segment, centered 4×4 rectangular node, 180 DIP name, 72 DIP state, and remaining detail. Node and text share the same row layout and center line; there is no Ellipse, second node rail, or full-height UniformGrid.
- Phase is rendered once, at the right edge of the bottom rail. COMMIT is one group containing lock mark and label and is visible only when the real snapshot is `Lock && CanCommit`.
- Top content measures to its content, the middle `*` row is intentionally quiet whitespace, and the separate Auto bottom rail stays bottom-aligned. Quiet space never distributes route nodes across the full viewport.
- Completion, cancellation, visual-readiness timeout, and unload clear opacity/translation/clip clocks, collapse the overlay, disable its hit testing, and restore the persistent Shell/PageHost baseline.

## 16. INITIAL TRACE final choreography and Advanced Sensors scrolling

- INITIAL TRACE retains one 12-column composition. The route occupies columns 0-7 with an 820 DIP maximum; the projection ledger occupies columns 8-11 with a 260 DIP minimum. Wide/Standard, Compact and Narrow margins resolve to `32,28,32,24`, `24,22,24,20` and `18,18,18,16`.
- The title subtitle is “启动中”. Index begins from an explicit hidden state and staggers SYS/BOOT.00, title/subtitle, Identity ledger, bottom rail and route label over 240 ms Full or 190 ms Standard. Reduced has only grouped opacity and Off has no visual clock.
- The route is one six-item control: each 34 DIP row has fixed `24 / 180 / 72 / *` columns, two 1x17 route segments, a centered 4x4 node and named text fields. All rows are prepared hidden before Route. Route arrival gives pre-resolved terminal nodes one lock flash; arrival and transition callbacks share state de-duplication.
- Identity, Environment and Projection ledger groups enter during Index, Route and Bind. Projection is data-driven by explicit previous/current resolved-count layers. Full computes a cyan orthogonal path from the live SENSOR BUS output anchor to the live ledger input anchor; Standard reveals the track without a moving head; Reduced/Off omit the path. There is no fixed 245 DIP placement or fixed 180 DIP translation.
- The 56 DIP bottom rail is a two-row five-stage control track: INDEX, ROUTE, BIND, LOCK and REVEAL with 01/05–05/05 labels and Success/current/future segment colors. Forward transitions play once. Failure uses Alert/Critical `启动降级：…` and `FAILED` while preserving fail-open.
- COMMIT uses a 28x28 lock mark, central 6x6 clip reveal and delayed label. Reveal separately coordinates content, background, bottom rail and the four persistent Shell targets. All clocks and temporary transforms/clips are cleared at the terminal snapshot.
- Advanced Sensors now has one physical page ScrollViewer (`Auto` vertical, disabled horizontal, `CanContentScroll=False`) around header, four-cell responsive rail and panel. The virtualized DataGrid keeps `RowHeight=34`, recycling and its own scrollbar at bounded Wide/Standard/Compact/Narrow heights.
- `NestedScrollViewerBehavior.ForwardAtBoundary` uses a 0.5 DIP boundary tolerance and directly moves the nearest outer ScrollViewer once. Interior wheel input remains inside the DataGrid; Shift-wheel, open ComboBox popups and pointer drags are not forwarded. The legacy report attached property remains supported.
- Automated coverage now contains `1597` tests, including 20/20 repetitions for Index/Route/Bottom Rail and Projection/live coordinates, with fail-open, SYSTEM REWIRE cold-template and Advanced Sensors nested-scroll regression coverage retained. Manual visual validation and formal administrator EXE launch remain unperformed.
