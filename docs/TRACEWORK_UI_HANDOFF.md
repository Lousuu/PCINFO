# TRACEWORK UI Handoff

> TRACEWORK static visual language pilot complete for Dashboard and CPU. Expansion to the remaining Tracework pages is still pending.

## A.0 Static visual language reconstruction pilot

- Baseline: `b87c712ca724cb7ade1237fc0d648bbcf77e2596` on `feature/tracework-ui`; existing PR #7 remains Open, Draft, and Unmerged.
- Shared system: exact neutral surface tiers plus IonViolet identity, SignalCyan telemetry, PhosphorMint active, WarmAmber attention, AlertCoral fault, corresponding soft brushes, a static `0.03` technical grid, five typography levels, and a deterministic 12/8/4/1 responsive grid.
- Primitives: InstrumentField, DataRail, AnnotationRail, ChartField, SignalMatrix, and a backwards-compatible restricted-use TechnicalPanel. Existing Tracework style keys remain available.
- Dashboard: `SYSTEM STATE`; Wide 7/5, Standard 5/3, Compact/Narrow stacked; CPU primary, GPU-led secondary field, Memory/Disk technical boundaries, open Network/System modules, and one shared DataRail.
- CPU: `PACKAGE TELEMETRY`; Wide 4/8, Standard 3/5, Compact/Narrow stacked; package identity and primary instrument, `Charts[0]` primary chart, existing `Charts[1..3]` compact auxiliary channels, existing `CoreRows` SignalMatrix.
- Invariants: Classic XAML is byte-identical; no new page ViewModel, chart VM, hardware subscription, timer, Polling/history update, navigation behavior, FLOW RELAY phase, SYSTEM REWIRE behavior, PresentMon path, recorder path, or GPU history path was added.
- Detailed specification: [`TRACEWORK_VISUAL_LANGUAGE.md`](TRACEWORK_VISUAL_LANGUAGE.md).
- Validation: clean Release and Debug application builds pass with `0 warning / 0 error`; two independent full Release processes both pass `1006 passed, 0 failed, 1006 total`, identical and above the 891 baseline. Coverage includes semantic colors/contrast, primitives, both pilot compositions, explicit internal widths, bindings, and Classic hashes. Manual visual validation, screenshot analysis, real-DPI validation, and the formal administrator EXE were not performed.
- CI stabilization: run `29910816910` passed production and test-runner builds and 1004/1006 tests; only the two new Classic hash guards failed because Windows CI checkout line endings differed from the local checkout. The guards now normalize LF/CRLF before hashing the same unchanged Classic content. Final-head CI is recorded in PR #7 and the final task report.
- Remaining work: expand reconstructed visual language to remaining Tracework pages; startup animation; full-project performance review; Stage 6; manual visual acceptance; real-DPI validation; formal administrator EXE validation.

## A. Current Repository State

- Repository: `https://github.com/Lousuu/PCINFO`
- Branch: `feature/tracework-ui`
- Draft PR: `https://github.com/Lousuu/PCINFO/pull/7`
- Base branch: `main`
- Current visual-amplification baseline: `10e997944e7cd516ed669d15b27a88fba7c7a272`
- Current scope: FLOW RELAY architecture and final visual amplification are complete on this branch; the exact documentation-head SHA is recorded in Draft PR #7 and the final task report.
- Working-tree expectation: clean after the final documentation commit is pushed.
- Build commands: `dotnet build .\HardwareVision\HardwareVision.csproj -c Release`; `dotnet build .\HardwareVision\HardwareVision.csproj -c Debug`
- Test command: `dotnet run --project .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release`

## B. Stage Status

- Stage 1: complete.
- Stage 1.1: complete.
- Stage 2: complete.
- Stage 3A: complete.
- Stage 3B: complete.
- Stage 3C: complete.
- Stage 3D-1: complete.
- Stage 3D-2: complete.
- Stage 4: complete; local automated validation and GitHub Actions are passing.
- Stage 5: complete locally; System Rewire theme transition is implemented and covered by automated tests.
- FLOW RELAY architecture: complete.
- FLOW RELAY final visual amplification: complete.
- Stage 6: not complete.

## C. Theme Architecture

The runtime theme system uses `AppTheme`, `ThemeService`, and inherited `ThemeContext`. `ThemeService` swaps the active theme resource dictionary while keeping shared typography, controls, Tracework shell/page resources, game page resources, and Motion resources loaded. Classic and Tracework are selected from Settings and persisted through `AppSettings.Theme`, with parser normalization preserving old or invalid values by falling back to Classic. Failed theme loads or replacements preserve the previous theme and do not persist the failed request.

## D. Shell Architecture

`App` creates services manually and passes them to `MainWindow`, which constructs one `MainViewModel`. `MainShellHost` owns `ClassicShellChrome`, `TraceworkShellChrome`, `TraceworkSignalRail`, `TraceworkTelemetrySpine`, and `TraceworkTimeRibbon`. There is still one named `PageHost`, now implemented by `MotionTransitionHost`, with one `Content="{Binding CurrentPage}"`. Theme switching changes chrome visibility and inherited context only; it does not replace `CurrentPage`.

## E. Page Architecture

Dual-template pages are active for Dashboard, CPU, GPU, Memory, Disk, Network, Motherboard, Advanced Sensors, Settings, Metric Visibility, Game Performance, and Game Session Report. Each root view chooses between a Classic layout and a Tracework layout through inherited theme state. The root view keeps its `DataContext`; page ViewModels are not rebuilt, and only the active layout is instantiated.

## F. Motion Architecture

Stage 4 adds `MotionLevel`, `MotionProfile`, `MotionLevelParser`, `IMotionEnvironment`, `SystemMotionEnvironment`, `IMotionService`, `MotionService`, `MotionChangedEventArgs`, `MotionContext`, and `MotionTransitionHost`.

Requested and Effective are separate. Requested is the user's persisted choice: Full, Standard, Reduced, or Off. Effective is computed at runtime from the requested level and environment. System downgrades do not overwrite `AppSettings.Motion`.

Downgrade matrix:

- Requested Off: Effective Off.
- Windows client animations disabled: Effective Off.
- RenderTier <= 0: Effective Off.
- High contrast: maximum Effective Reduced.
- Remote session: maximum Effective Reduced.
- RenderTier == 1: maximum Effective Reduced.
- RenderTier >= 2: Effective follows Requested.

`SystemMotionEnvironment` uses public WPF/Windows APIs: `SystemParameters.ClientAreaAnimation`, `SystemParameters.HighContrast`, `RenderCapability.Tier >> 16`, and `System.Windows.Forms.SystemInformation.TerminalServerSession`. It subscribes to WPF/System events and unsubscribes on dispose. It does not read private registry keys, poll, or create timers.

`MotionContext` exposes inherited attached properties so pages and controls receive motion state from the tree, not from `Application.Current` or a static mutable profile. The formal PageHost disables automatic content animation for FLOW RELAY. Ordinary Tracework navigation explicitly drives its existing internal `MotionSurface` with directional clip reveal, opacity, bounded translate, and Primary/Secondary settle. It uses no scale, blur, shader, layout property animation, double-buffered page copy, or continuous animation.

SYSTEM REWIRE and FLOW RELAY remain independent; SYSTEM REWIRE has takeover priority and cancels/restores FLOW RELAY visuals before starting. Classic never plays the Tracework FLOW RELAY transition. Reduced retains opacity-only degradation, and Off is immediate with no visual clock.

## F.1 System Rewire Theme Transition Architecture

Stage 5 adds a dedicated runtime theme-transition layer outside `ThemeService`: `SettingsViewModel -> IThemeTransitionService -> IMotionService/IThemeService/Dispatcher/IThemeTransitionClock -> MainViewModel -> MainShellHost -> SystemRewireOverlay`.

`ThemeTransitionService` owns transition timing, phases, cancellation, same-target coalescing, and failure results. It calls `ThemeService.ApplyTheme` only during the Latch phase. Startup still uses `ThemeService` directly, so the app never plays SYSTEM REWIRE during initial launch. Motion Off bypasses overlay timing and applies directly; Reduced keeps the overlay fade-only and short; Standard and Full use Trace/Latch/Splice with interaction blocking while active.

`MainViewModel` subscribes to `TransitionChanged` separately from `ThemeChanged` and exposes the latest `ThemeTransitionSnapshot` plus derived state for the shell. It does not navigate, save settings, or apply themes as part of that subscription. `SettingsViewModel` uses an async theme command and persists the selected theme only when the transition result is committed/applied. Failed, already-current, superseded, or cancelled results are not saved; save failure keeps the applied runtime theme and surfaces a warning.

`SystemRewireOverlay` is hosted once in `MainShellHost` above the persistent `PageHost` with `Panel.ZIndex=100`. It binds to `ThemeTransition`, intercepts mouse input while active, remains non-focusable, and does not replace page content or page ViewModels.

## G. Business Invariants

Stage 4 does not rebuild the service graph, does not rebuild page ViewModels, and does not change sensor collection, `PollingService`, PresentMon, FPS or session statistics, game session file formats, device selection persistence, metric IDs, units, ordering, or session report load lifecycle. Motion is a display-layer behavior after navigation has already completed.

## H. Tests And Validation

- Release build result: pass locally before commit.
- Debug build result: pass locally before commit.
- Final local test count: `660 passed, 0 failed, 660 total` after bugfix stabilization.
- GitHub Actions result: pass for run `29676111020` on `b87bd0ebd0754e9af16f662490703c51a736e6e9`.
- Bugfix stabilization CI: pass for run `29890063585` on `a5dc702fff4f6521b9c5e1c2d2190fc4d8c297ff`.
- Manual visual validation: not performed by user request.

Validation coverage includes the custom console runner, WPF runtime smoke tests, side-effect counting tests, Motion parser tests, effective downgrade matrix tests, MotionChanged tests, MotionContext tests, MotionTransitionHost tests, PageHost persistence tests, ThemeTransition phase/result tests, Rewire XAML 01..12 runtime tests, bugfix regression tests for pending page transitions, nested scroll boundary forwarding, shared GPU history sampling, and static architecture checks.

## H.1 Bugfix Stabilization (historical stage record)

This pass fixed four confirmed TRACEWORK UI regressions without adding FLOW RELAY, startup animation, memory page layout changes, Stage 6, or broad visual redesign.

- Page fade root cause: `MotionTransitionHost` skipped legal navigations when Loaded, template, or window visibility was not ready, and did not replay them. Fix: keep only the latest pending navigation, replay once when the host/template/window are ready, preserve first-page and permanent skip behavior, cancel fast-navigation animations, and restore opacity/translation to the final state.
- Fade parameters: Full is `220ms / 0.52 / 8px`; Standard is `175ms / 0.66 / 5px`; Reduced is `105ms / 0.84 / 0px`; Off creates no animation clock.
- Nested scroll root cause: the performance-limit `ListBox` consumed wheel input at its internal `ScrollViewer` boundary. Fix: `NestedScrollViewerBehavior.BubbleMouseWheelAtBoundary` forwards one equivalent wheel event to the nearest outer report `ScrollViewer`, with recursion protection and an open-ComboBox exception.
- GPU history root cause: GPU history was written from `DashboardViewModel.RefreshGpuDevices`, so background/dashboard-inactive/game-recording periods could stop chart history. Fix: `SensorHistoryService` records GPU samples directly from the shared `PollingService` readings and stores GPU buckets by stable device ID; Dashboard now only projects GPU devices.
- PageHost gap root cause: the animated `MotionSurface` was transparent and the host did not clip translated content. Fix: `MotionTransitionHost` clips to bounds; `MotionSurface` stretches and uses `AppBackgroundBrush`, while PageHost margins and shell spacing remain unchanged.
- Latest local validation before push: Release build `0 warning / 0 error`; Debug build `0 warning / 0 error`; custom runner passed twice with `660 passed, 0 failed, 660 total`; manual visual validation not performed.

## TRACEWORK layout stabilization (historical stage record)

### Baseline and scope

- Baseline head: `07b3c0ac10b597907a696f5effc03b1a046050a4`.
- Branch / PR: `feature/tracework-ui`, existing Draft PR #7.
- Scope: Tracework memory module layout, Tracework game TARGET PROCESS layout, shared Tracework page safe areas, SignalRail long labels, and automated layout coverage.
- Explicitly unchanged: all Classic layout structures; hardware collection and statistics; `SensorHistoryService`; `PollingService`; GPU history; PresentMon; game recorder/session formats; ViewModel lifecycle; `MotionTransitionHost`; single PageHost; the sole `CurrentPage` binding; FLOW RELAY; startup animation; SYSTEM REWIRE; Stage 6.

### Memory layout

- Root cause: module fields were generated in fixed three-column slots. A collapsed middle field hid its card but left the `ItemsControl` container in the panel, so later fields could not move forward.
- New panel: `AdaptiveUniformGrid` inherits WPF `Panel`, arranges only direct children whose visibility is not `Collapsed`, preserves source order, uses equal-width columns, fills the final row from the left, and invalidates measure/arrange when its dependency properties change.
- Panel properties on the memory page: `MinItemWidth=280`, `HorizontalGap=12`, `VerticalGap=12`, `MaximumColumns=3`.
- Responsive rules use the panel's actual available width: 3 columns at `>=1080px`, 2 columns at `680-1079px`, and 1 column below `680px`. The panel does not read MainWindow width.
- Field order remains: `插槽位置`, `容量`, `厂商`, `PartNumber`, `Speed`, `ConfiguredClockSpeed`, `FormFactor`, `MemoryType`, `位宽`. The default-hidden serial-number item remains in the existing ViewModel collection and occupies no layout slot while collapsed.
- Every module uses the same module `ItemTemplate`, item-container visibility binding, and `AdaptiveUniformGrid`. Module spacing is `12px`; the final module receives the shared page bottom safe area instead of a large per-module spacer.
- Module cards use `MinHeight=92`, stretch alignment/content, centered vertical content, and `Padding=16,12`. Labels and values are left-aligned, single-line, character-trimmed; values keep full-value tooltips. Long slot names and part numbers do not create page-level horizontal scrolling.
- Raw values such as `FormFactor=12` and `MemoryType=34` are preserved. No verified WMI/SMBIOS enum mapping exists in the current project, and this pass did not add an unverified table or alter collection logic.

### Game TARGET PROCESS layout

- The panel's five conceptual rows are: module code/title/formal right-top status; Chinese subtitle; SEARCH TARGET / PROCESS ROUTE labels; search TextBox / process ComboBox; refresh/detect/start/stop buttons.
- Labels and inputs share one grid with columns `5*`, `28px`, `9*`. Both labels are left aligned; PROCESS ROUTE is bottom aligned and shares the ComboBox left edge.
- TextBox and ComboBox use `Height=64`, `MinHeight=64`, centered vertical content, stretch horizontal content, and `Padding=18,0`. Their top and bottom edges match. The placeholder uses the same left inset without Canvas positioning.
- The ComboBox reserves an independent `48px` arrow column. Process text uses `CharacterEllipsis` / `NoWrap`, with the complete display name in Tooltip.
- The target-only button styles use `Height=64`, `MinWidth=172`, centered content, and `16px` horizontal spacing. The final button has no trailing right margin. The horizontal `WrapPanel` starts `20px` below the inputs and uses a `76px` row pitch, providing `12px` between wrapped 64px rows.
- The button-row copies of detection/capture/status text were removed. The existing `StatusText` binding remains in an explicit right-top badge container with `MinWidth=80`; command bindings, process ItemsSource/selection/search bindings, and command-driven Start/Stop availability are unchanged.

### Shared page safe area and SignalRail

- `TraceworkPageScrollViewerStyle` sets vertical Auto, horizontal Disabled, vertical-only panning, and `Padding=0,0,12,24`.
- Dashboard, CPU, GPU, Memory, Disk, Network, Motherboard, Game Performance, Settings, and Game Session Report use the shared scroll style and a shared stretch/top content stack.
- Advanced Sensors and Metric Visibility retain their fixed-grid page structures and use `TraceworkPageContentHostStyle` for the same right/bottom safe area.
- All 12 Tracework page roots use stretch alignment with `MinWidth=0` and `MinHeight=0`. No page-level horizontal scrollbar was introduced. The single PageHost and sole `CurrentPage` binding remain unchanged, and TimeRibbon stays in its separate shell row.
- SignalRail width is unchanged. Every navigation item uses `CharacterEllipsis` and `NoWrap`, with `ToolTip={Binding Title}` and `AutomationProperties.Name={Binding Title}`. `高级传感器` therefore retains its complete Tooltip and automation name while the fixed code column, explicit gap, and keyboard focus border remain separate from the trimmed title region.

### Files changed

- Production controls/themes: `HardwareVision/Controls/AdaptiveUniformGrid.cs`, `HardwareVision/Controls/TraceworkPanel.cs`, `HardwareVision/Themes/Tracework/Pages.xaml`, `HardwareVision/Themes/Tracework/GamePages.xaml`, `HardwareVision/Themes/Tracework/Shell.xaml`.
- Production views: all 12 `HardwareVision/Views/*/Tracework*Layout.xaml` page layouts for the shared root/safe-area contract, plus `HardwareVision/Views/Shell/TraceworkSignalRail.xaml`. Only the Memory and Game Performance page information layouts changed materially.
- Tests: `HardwareVision.Tests/AdaptiveUniformGridTests.cs`, `HardwareVision.Tests/TraceworkMemoryLayoutTests.cs`, `HardwareVision.Tests/GameTargetProcessLayoutTests.cs`, `HardwareVision.Tests/TraceworkPageSpacingTests.cs`, and registration in `HardwareVision.Tests/Program.cs`.
- Documentation: `HANDOFF.md` and `docs/TRACEWORK_UI_HANDOFF.md`.

### Automated validation and handoff state

- Clean Release build: pass, `0 warning / 0 error`.
- Debug build: pass, `0 warning / 0 error`.
- Previous test baseline: `660 passed, 0 failed, 660 total`.
- Final test count: `709 passed, 0 failed, 709 total`.
- Final two independent full test processes after the fixture fix: pass / pass, both `709 passed, 0 failed, 709 total`.
- Additional coverage: 13 adaptive-panel tests plus runtime/static Memory, TARGET PROCESS, page spacing, PageHost/CurrentPage, and SignalRail tests; total increase is 49.
- Transient observation: an intervening full run reported the pre-existing `Report accuracy 01` case as `708 passed, 1 failed, 709 total` (`expected 10, got 5`). No report or session logic was changed; two subsequent complete runs passed at 709/0.
- `git diff --check`: pass before both the test-fix and documentation commits.
- Initial layout commit: `462901647c3c116681a434faac876a327f84a220`.
- Failed CI run: `29896515742`. The production layout did not fail. The page-level responsive test inferred an internal panel width from the outer Window's nominal width, so GitHub Runner system measurements produced 2 columns where the fixture expected 3.
- Deterministic test fix: `ee8075ceb80fe435793c578e990cc36275daebe1`. Only `AdaptiveUniformGridTests.cs` and `TraceworkMemoryLayoutTests.cs` changed. Production files changed by the CI fix: none.
- Breakpoint verification now gives `AdaptiveUniformGrid` explicit available widths through `Measure` and `Arrange`: `1080px` for 3 columns, `680px` for 2 columns, and `679px` for 1 column. Assertions also verify the Panel's actual arranged width. The page-level runtime smoke checks successful measure/arrange, compact slots, non-overlap, and no horizontal overflow without deriving column count from Window width.
- Successful test-fix CI run: `29897148260` on `ee8075ceb80fe435793c578e990cc36275daebe1`; Release build, all 709 tests, source hygiene, and dependency inventory passed.
- Manual visual validation: not performed.
- Screenshot analysis: not performed.
- Formal administrator EXE: not launched.
- Real DPI validation: not performed.
- Historical remaining-work note: FLOW RELAY was still pending at this layout-stage checkpoint; that item is superseded by the completed sections below. Current remaining work is startup animation, full-project performance review, Stage 6, manual visual acceptance, real-DPI validation, and formal administrator EXE validation.
- Final head: the documentation commit containing this handoff is the final `feature/tracework-ui` branch head. Its exact full SHA and its successful final CI run are recorded in Draft PR #7 and the final task report because a Git commit cannot include its own hash in its tracked contents.

## FLOW RELAY navigation transition

### Baseline and scope

- Baseline head: `32ba6c0b776df0c3debcdc98b85463be83ad8be2` on `feature/tracework-ui`; existing PR #7 remains Open, Draft, and Unmerged.
- Scope is only ordinary-page FLOW RELAY navigation. Startup animation, Stage 6, Classic visuals, Memory and TARGET PROCESS layout redesign, full-project performance review, hardware collection, sensor refresh, PresentMon, game statistics/recording, GPU history, and session formats are outside this pass and unchanged.
- Navigation is split into `RequestNavigation` and `CommitNavigation`. Request creates/coordinates intent without changing `CurrentPage`, real selection, page activity, or `LastSelectedPage`. Commit retains lazy page creation and the existing order: deactivate old page, update the one real page/metadata/selection, persist the existing key once, then activate the target once.

### Routes, directions, state, and timing

- Groups: SYSTEM = Dashboard, CPU, GPU, Memory, Disk, Network, Motherboard, Advanced Sensors; SESSION = Game Performance and its nested Game Session Report route; CONTROL = Settings and Metric Visibility. Existing page keys remain the identity system, and the report adds no SignalRail item.
- Same-group later/earlier routes use `FromBottom` / `FromTop`. SYSTEM -> SESSION/CONTROL and SESSION -> CONTROL use `FromRight`; reverse group movement uses `FromLeft`. Game Performance -> Report is `FromRight`; Report -> Game Performance is `FromLeft`. Same-page and invalid routes are `None`; route distance does not change timing.
- The versioned state machine is `Idle -> Route -> Shift -> Relay -> Settle -> Idle`. Snapshots contain version, activity, phase, direction, origin/target page metadata, plan, and commit state. Stale snapshots cannot replace newer state.
- Full: Route `0-70ms`, Shift `70-120ms`, Relay commit `120ms`, Settle through `330ms`; page reveal `150ms`, opacity `0.74`, offset `10px`, Primary/Secondary delays `28/72ms`.
- Standard: Route `0-50ms`, Shift `50-90ms`, Relay commit `90ms`, Settle through `260ms`; page reveal `118ms`, opacity `0.80`, offset `7px`, Primary/Secondary delays `18/48ms`.
- Reduced: fade-only Shift/Relay commit at `40ms`, Settle through `120ms`; page opacity `0.86`, zero translation, no SignalRail spatial route, no telemetry translation, no clip reveal, and no module staggering.
- Off: zero duration, immediate commit, no overlay/cursor/telemetry/page animation, and no navigation animation clock.

### Service and shell integration

- App creates the single formal `NavigationTransitionService`, passes it through MainWindow/MainViewModel, and disposes it at shutdown. The service owns the clock, phases, version, cancellation, same-target task reuse, latest-target replacement, snapshot publication, and one Relay commit delegate. It has no page, WPF-control, ThemeService, MotionService mutation, polling, hardware, recorder, settings persistence, or DispatcherTimer responsibilities.
- Fast navigation has no queue. The same active target reuses its task. A different target before commit cancels without committing the stale target and uses the current real page as origin. A request after commit cancels old Settle, restores visual baselines, and starts from the newly real page. Twenty-request service coverage proves only the last target commits.
- SignalRail keeps its width and item structure. Its sole overlay uses a `1px` RouteSegment between actual button centers, a `6x2` RoutePulse, a Full-only `10x1` PulseTrail, and a committed-target `8x2` ArrivalLock (`52ms` Full / `38ms` Standard). Reduced and Off have no spatial rail visual. It is non-focusable, outside Tab order, and never hit-testable. Real selection changes only at commit.
- `TelemetryTitleTransitionHost` handles only Page Code, Title, and Subtitle through overlapping clipped Source/Target layers. Route fades the origin subtitle; Shift independently moves/crossfades source and target with direction-specific Full/Standard offsets and `60%` code travel; Reduced crossfades only. The target subtitle fades during the last roughly `70ms`, and only the committed target title becomes the polite live region. LIVE badge, polling state, footer, time, and hardware status remain static.
- The one `RelayBandOverlay` at Z=40 contains a machine-black body, `2px` leading edge, `1px` trailing edge, two traces, two nodes, Full-only internal pulse, short route code, and committed-snapshot CenterLock. Horizontal bands use 22% width clamped to `160-300px`; vertical bands use 22% height clamped to `120-220px`. Cubic EaseInOut crosses the visual center exactly at unchanged CommitTime without pause or reversal. Reduced uses a stationary short fade with no nodes/internal pulse.
- The sole formal `MotionTransitionHost x:Name="PageHost"` retains the sole `CurrentPage` binding, clipping, margin, and background. `IsAutoTransitionEnabled="False"` prevents the legacy `OnContentChanged` animation from stacking with FLOW RELAY; compatibility default remains true elsewhere. Shell calls explicit `PlaySettle` once for a committed Settle snapshot. Only its internal surface opacity/translate is animated; PageHost/layout/scale/blur are not.
- `NavigationMotion.Role` marks no more than one Primary and one Secondary region in each Tracework layout. Full/Standard use the plan delays; Reduced/Off do not stagger. DataGrid/List/ItemsControl items and sensor/data rows carry no role, so there is no per-card cascade. Cancellation restores opacity one, translations zero, and clears clocks.

### Isolation, lifecycle, focus, and invariants

- SYSTEM REWIRE has priority. While it is active, navigation commits directly with no FLOW RELAY visual. A theme request during FLOW RELAY cancels visuals, commits the latest pending valid target once if necessary, restores all baselines/Idle, and only then starts Rewire. The two overlays cannot be active together. Navigation never applies a theme; Rewire never calls `CommitNavigation`; page switching preserves theme and theme switching preserves page.
- Hidden, minimized, or shell-unloaded transitions stop visual work, commit the latest target once, clear to Idle, and do not replay after restore. Window close/dispose cancels without starting work or leaving unobserved tasks; MainShellHost unsubscribes. Resize uses current PageHost dimensions and never animates layout properties.
- Relay and cursor never take keyboard focus or enter Tab order. SignalRail remains clickable/keyboard-operable during navigation; no input control is force-focused and no focus trap is introduced.
- Preserved business invariants: one `CurrentPage`, one PageHost/binding, existing lazy page instances/cache, one old-page deactivation and one target activation per successful navigation, and one existing `LastSelectedPage` update. FLOW RELAY adds no ThemeService or MotionService-setting calls, polling/sensor refresh, hardware service work, PresentMon work, recorder work, GPU-history writes, session-format changes, or additional settings saves. The Game Session Report retains its nested open/load/close/dispose/realtime suspend-resume lifecycle while using route `08R` only for shell motion.

### Tests, builds, and handoff state

- Architecture-pass baseline: `709 passed, 0 failed, 709 total`; architecture-pass final count: `791 passed, 0 failed, 791 total` (82 added). Final visual-amplification count: `891 passed, 0 failed, 891 total` (100 additional tests).
- Added plan, service, control, integration, and lifecycle suites cover exact timing/direction/profile matrices, Relay-only single commit, cancellation/latest-wins/version/exception cleanup, cursor/telemetry/band/PageHost structure and final state, roles, Rewire isolation, lifecycle/focus, forbidden APIs, and business invariants. A real STA Dispatcher integration test proves the page, selection, and persisted key stay unchanged before Relay and change together only after the gate is released.
- Clean Release application build: pass, `0 warning / 0 error`. Debug application build: pass, `0 warning / 0 error`.
- Final visual-amplification Release test processes: two independent runs both report `891 passed, 0 failed, 891 total`; counts are identical and greater than the 791 visual baseline.
- `git diff --check`: pass. Initial CI run `29900694241` passed Release build but reported `747 passed, 44 failed, 791 total`: every failure was one of the new source-inspection tests looking only above the repository-external `AppContext.BaseDirectory` (`D:\a\_temp\hardwarevision-ci\bin`) instead of the workflow working directory. No production/runtime test failed. The focused test-only fix makes the four new fixtures follow the established current-directory-first repository search without changing or weakening assertions. An exact local reproduction built into an external temporary artifacts directory and ran its published EXE from the repository working directory with `791 passed, 0 failed, 791 total`. The exact successful final documentation-head CI run is recorded in PR #7 and the final task report.
- Manual visual validation: not performed. Screenshot analysis: not performed. Formal administrator EXE: not launched. Real-DPI validation: not performed.
- Remaining work: startup animation, full-project performance review, Stage 6, manual visual acceptance, real-DPI validation, and formal administrator EXE validation.
- Final head: the documentation commit containing this section is the final branch head. Its exact full SHA and successful final CI run are recorded externally in Draft PR #7 and the final task report because a Git commit cannot include its own hash in its own tracked contents.

## FLOW RELAY final visual amplification

### Baseline, scope, and visual cause

- Baseline head: `10e997944e7cd516ed669d15b27a88fba7c7a272` on `feature/tracework-ui`; existing PR #7 remains Open, Draft, and Unmerged.
- Scope is visual amplification only. `NavigationTransitionService`, Request/Commit separation, `Idle -> Route -> Shift -> Relay -> Settle -> Idle`, Full/Standard/Reduced commit times, cancellation/latest-wins behavior, SYSTEM REWIRE priority, page lifecycle, and all hardware/business services are unchanged.
- The first visual version looked simple because SignalRail used one cursor, Telemetry reused one text set, RelayBand was one solid rectangle with linear travel, and PageHost only faded/translated. The final layer keeps the same orchestration while giving each phase a distinct bounded visual role.

### SignalRail and Telemetry

- SignalRail uses one non-layout overlay: `RouteSegment` is a `1px` trace between actual source/target button centers; `RoutePulse` is `6x2`; Full adds a `10x1` fading PulseTrail; the committed selected target gets an `8x2` mint ArrivalLock for `52ms` Full or `38ms` Standard. Reduced and Off use no rail spatial effect. All four elements hide, reset transforms, and clear clocks on completion/cancel/unload/takeover.
- Telemetry now uses true overlapping Source/Target layers inside one clipped viewport. Route fades the source subtitle. Shift crossfades source `1 -> 0.28` and target `0.18 -> 1`, with Full horizontal `8/10px`, vertical `6/8px`, Standard horizontal `6/7px`, vertical `4/5px`, and code displacement at `60%` of title displacement. Reduced crossfades without translate; Off restores current text directly.
- Only a committed Relay snapshot promotes the target title to the polite automation live region; the source automation name is cleared. Settle normalizes the target and fades its subtitle in during the last `70ms`.

### Layered relay and directional PageHost reveal

- The sole RelayBand template contains `BandRoot`, `BandBody`, `2px` LeadingEdge, `1px` TrailingEdge, `TraceA/B`, `NodeA/B`, Full-only `InternalPulse`, short `RouteCode`, `CenterLock`, and one translate. Full and Standard use cubic EaseInOut main travel and cross center at the existing real CommitTime with no pause/reversal/loop. Standard retains node flashes but no moving internal pulse; Reduced has a stationary opacity fade with no nodes/spatial internals; Off has no band.
- CenterLock is driven only by `Phase == Relay`, `HasCommitted == true`, and the current snapshot version. Full is `18/18/28ms` (`64ms` total), Standard `14/10/22ms` (`46ms`), Reduced a `28ms` fade flash, and Off none.
- The one formal PageHost and one `CurrentPage` binding remain. Its existing MotionSurface uses a directional `RectangleGeometry` reveal plus opacity/translate: Full `150ms / 0.74 / 10px`; Standard `118ms / 0.80 / 7px`; Reduced opacity-only `0.86`; Off immediate. Clip animation changes only visibility, never layout size or page scale.
- Primary/Secondary settle is strengthened without adding item-level cascades: Full delays `28/72ms`, start opacity `0.68/0.58`, offsets `8/12px`; Standard `18/48ms`, `0.78/0.70`, `6/8px`. Reduced and Off do not stagger. Item/data/sensor rows remain excluded.

### Cleanup, performance, validation, and handoff

- Rapid replacement, cancellation, resize, unload, completion, and SYSTEM REWIRE takeover clear old versions and all opacity/translate/clip clocks. The visual layer adds no `DispatcherTimer`, `CompositionTarget.Rendering`, layout-property animation, blur, shader, screenshot/page copy, particle system, or continuous animation.
- Tests: six visual suites add 100 cases for exact profile values, rail geometry/state, Telemetry dual tracks, layered RelayBand/CenterLock, directional clip reveal, and lifecycle/isolation. Final count is `891 passed, 0 failed, 891 total`; two independent final Release runs are identical and above the `791` baseline. A preceding run reproduced the already documented transient `Report accuracy 01` timestamp case at `890/1/891` (`expected 10, got 5`); this pass changed no report/session/PresentMon source, and both subsequent complete runs passed.
- Builds: clean Release and Debug application builds pass with `0 warning / 0 error`; `git diff --check` passes. The exact successful final documentation-head CI run and full final SHA are recorded in Draft PR #7 and the final task report.
- Manual visual validation: not performed. Screenshot analysis: not performed. Formal administrator EXE: not launched. Real-DPI validation: not performed. Automated tests do not substitute for manual visual acceptance.
- Remaining work: startup animation, full-project performance review, Stage 6, manual visual acceptance, real-DPI validation, and formal administrator EXE validation.

## I. Key File Index

- `HardwareVision/App.xaml.cs`
- `HardwareVision/Models/AppSettings.cs`
- `HardwareVision/Models/MotionLevel.cs`
- `HardwareVision/Models/MotionProfile.cs`
- `HardwareVision/Models/MotionLevelParser.cs`
- `HardwareVision/Services/ThemeService.cs`
- `HardwareVision/Services/MotionService.cs`
- `HardwareVision/Services/SystemMotionEnvironment.cs`
- `HardwareVision/Themes/ThemeContext.cs`
- `HardwareVision/Themes/MotionContext.cs`
- `HardwareVision/Views/Shell/MainShellHost.xaml`
- `HardwareVision/Controls/MotionTransitionHost.cs`
- `HardwareVision/Behaviors/NestedScrollViewerBehavior.cs`
- `HardwareVision/Services/SensorHistoryService.cs`
- `HardwareVision/Controls/SystemRewireOverlay.cs`
- `HardwareVision/Themes/Tracework/Motion.xaml`
- `HardwareVision/Themes/Tracework/SystemRewire.xaml`
- `HardwareVision/Models/ThemeTransition*.cs`
- `HardwareVision/Services/*ThemeTransition*.cs`
- `HardwareVision/ViewModels/MainViewModel.cs`
- `HardwareVision/ViewModels/SettingsViewModel.cs`
- `HardwareVision/Views/Settings/ClassicSettingsLayout.xaml`
- `HardwareVision/Views/Settings/TraceworkSettingsLayout.xaml`
- `HardwareVision/Views/*/*Layout.xaml`
- `HardwareVision.Tests/MotionInfrastructureTests.cs`
- `HardwareVision.Tests/BugFixRegressionTests.cs`
- `HardwareVision.Tests/NestedScrollingTests.cs`
- `HardwareVision.Tests/SharedGpuHistoryTests.cs`
- `HardwareVision.Tests/ThemeTransitionTests.cs`
- `HardwareVision.Tests/XamlRuntimeSmokeTests.cs`
- `HardwareVision.Tests/MainShellStateTests.cs`
- `HardwareVision.Tests/TraceworkConfigurationPageTests.cs`

## J. Stage 5 Completion Boundary

Stage 5 reuses `MotionService` and inherited motion state for short, bounded SYSTEM REWIRE theme switching. Theme transition orchestration is intentionally independent from `ThemeService` and `MotionService`; it does not rewrite either service.

Stage 5 did not create a second PageHost, rebuild page ViewModels, add infinite animation, use shaders, introduce a heavy UI framework, add game visual assets, or change business statistics.

## K. Known Risks And Manual Checks

- Full-page visual inspection has not been performed.
- 125% and 150% DPI have not been manually checked.
- Real Tier0/Tier1 low-performance machines have not been manually validated.
- Windows animation-disabled behavior has not been manually validated.
- High contrast has not been manually validated.
- Remote desktop has not been manually validated.
- The formal administrator EXE was not launched in this stage.
- Before Stage 6, manually check theme switching, PageHost navigation, Settings motion changes, DPI, high contrast, remote desktop, and a low-tier graphics environment if visual validation becomes in scope.

## L. Continue Development Commands

```powershell
cd E:\Mine\PCINFO
git status --short --branch
git fetch origin
git log -8 --oneline --decorate
Get-Content .\docs\TRACEWORK_UI_HANDOFF.md -Raw
dotnet build .\HardwareVision\HardwareVision.csproj -c Release
dotnet run --project .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release
```
