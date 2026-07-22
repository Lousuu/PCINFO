# HardwareVision Performance, Lifecycle, and Logic Review

> Review baseline: `58d1b80535ef37aa009d230216823278d58ee7b9` on `feature/tracework-ui`. This is a source-level and deterministic-test review. The formal administrator EXE, real hardware sensor load, manual visual acceptance, screenshots, and real-DPI environments were not used.

## Method

The review traced ownership from `App` through `MainWindow`/`MainViewModel`, then followed page activation, service event subscriptions, cancellation owners, background tasks, Dispatcher crossings, collection mutation, WPF virtualization/rendering, polling/history, PresentMon/recorder/report I/O, settings writes, theme/motion transitions, tray/window state, and shutdown. Findings require a verified code path and concrete runtime effect; style preferences are excluded.

## Findings

### Finding 1

Severity: High
Area: Advanced Sensors refresh
File / method: `AdvancedSensorsViewModel.ApplyReadingsAsync`; former `ViewModelHelpers.UpdateSensorRows` / `FindSensorRowIndex`
Verified cause: every eligible refresh created up to 500 temporary row ViewModels, then performed a forward linear search for each desired ID and issued incremental collection operations on the UI Dispatcher.
Runtime impact: worst-case O(n²) reconciliation, allocation churn, many UI notifications, and visible first-open/refresh stalls.
Fix: immutable worker-thread snapshots, one current-row dictionary, stable object reuse, changed-field updates, and a one-Reset bulk collection for structural changes.
Validation: 500-row one-Reset, identical-refresh zero-notification, value-only reuse, add/delete/reorder, source-shape, cancellation and status tests.
Residual risk: real administrator hardware may expose provider/driver latency outside this UI reconciliation path.

### Finding 2

Severity: Medium
Area: Advanced Sensors virtualization
File / method: `TraceworkAdvancedSensorsLayout.xaml`
Verified cause: a virtualized DataGrid was measured through a panel that supplies infinite child height; virtualization cannot bound work if the list is effectively unbounded.
Runtime impact: excessive desired height and avoidable row realization/layout pressure.
Fix: finite maximum grid height, fixed 34-DIP rows, item scroll unit, and retained row/column recycling without an outer ScrollViewer.
Validation: runtime XAML plus explicit property/no-wrapper guards.
Residual risk: viewport feel still requires real-DPI/manual validation.

### Finding 3

Severity: Medium
Area: Tracework layout stability
File / method: GPU and Game Session Report layouts
Verified cause: `TraceworkResponsiveGrid` intentionally gives all children in a logical row the row maximum; short selector/summary panels shared rows with tall hero/timeline content.
Runtime impact: large blank areas and extra panel layout area; GPU metric star columns also separated related text.
Fix: independent vertical column stacks, fixed compact order, top-aligned content-sized report summary, and adjacent adaptive GPU metric columns.
Validation: structure/order/binding/runtime responsive tests.
Residual risk: final perceptual density must be checked manually.

### Finding 4

Severity: Low
Area: Network matrix and Settings visual tree
File / method: `TraceworkNetworkLayout.xaml`; `TraceworkSettingsLayout.xaml`
Verified cause: hidden metric inner content left its item container allocated; Settings added seven non-interactive duplicated labels.
Runtime impact: matrix holes/misalignment and unnecessary layout elements.
Fix: collapse the item container, unify badge geometry, and remove the static settings index.
Validation: container visibility, badge bool-binding, full-width workspace, and binding-preservation guards.
Residual risk: final visual appearance requires manual confirmation.

### Finding 5

Severity: Low
Area: Responsive layout allocations
File / method: `TraceworkResponsiveGrid.MeasureOverride` / `ArrangeOverride`
Verified cause: each pass allocated a reference-type `Placement` per visible child and used LINQ for row sum/max.
Runtime impact: small but repeatable layout-time garbage during resizing/theme/page layout.
Fix: `readonly record struct Placement` and ordinary loops; breakpoints, visibility handling, attached properties, and geometry are unchanged.
Validation: four-mode geometry equivalence and allocation-shape source guards.
Residual risk: per-pass lists/row arrays remain intentionally simple and uncached to avoid invalidation bugs.

### Finding 6

Severity: Low
Area: Other metric collection reconciliation
File / method: `ViewModelHelpers.UpdateMetricCollection`
Verified cause: structural lookup is linear, but current callers supply small bounded device/metric groups; stable rows update in place and there is no evidence of a 500-item hot path.
Runtime impact: negligible at current bounded sizes.
Fix: recorded only; no behavioral rewrite without runtime evidence.
Validation: call-site and collection-size review; existing metric/page tests remain.
Residual risk: revisit if any caller grows to hundreds of items.

### Finding 7

Severity: Low
Area: Realtime chart snapshots
File / method: `RealtimeMetricChartViewModel.RefreshSnapshot`
Verified cause: accepted samples copy the selected ring tail to a new array for immutable binding notification. The bound is 240 doubles and active refresh is 2 Hz.
Runtime impact: bounded small allocation per active chart update.
Fix: recorded only; changing the binding contract would add complexity and risk.
Validation: source bound review; chart rendering skips hidden controls and freezes cached Pens/Geometry.
Residual risk: profile on low-tier hardware if chart count or frequency changes.

## No-issue evidence

- Startup/ownership: `App` creates the formal polling, history, PresentMon/recorder, transition, refresh and window graph once; shutdown is guarded and disposes owned services.
- Lazy pages: `MainViewModel` caches page ViewModels with `??=` and centralizes activation through `SetPageActive`; inactive pages unsubscribe/stop page-only timers.
- Cancellation/tasks: Advanced Sensors, report loading, settings directory scan, dashboard refresh, polling, navigation and capture owners observe cancellation and/or log task exceptions. CTS owners are disposed on completion.
- Polling/history: one polling task and execution lock prevent reentry; `SensorHistoryService` has one `ReadingsUpdated` subscription and removes it on dispose. GPU history remains sourced from the shared polling readings path.
- PresentMon: lifecycle serialization prevents duplicate capture sessions; capture process, CTS and reader tasks are stopped/observed and disposed.
- Recorder/I/O: the recorder uses a bounded channel and a dedicated writer task; report loading/parsing runs off the UI thread, caches four reports, bounds limit events and chart output, and preserves partial results.
- Settings: writes are serialized by `SettingsService`; property setters save only after actual value changes. Theme and motion persistence remain separate and recoverable failures are logged.
- Rendering/resources: inactive game UI stops its DispatcherTimer; realtime charts return early when hidden and freeze reusable drawing resources. Theme switching keeps one replaceable resource dictionary.
- Window/tray/disposal: window close/hide/minimize routes preserve existing semantics; `MainWindow` disposes its DataContext and monitor, while `App` owns service shutdown.

## Automated and local validation

Deterministic tests cover operation/notification counts rather than machine-time thresholds. They also guard lifecycle ownership, forbidden APIs, one PageHost/CurrentPage binding, virtualized lists, theme scrollbar resources, unchanged dependency count, and responsive geometry. Clean Release and Debug application builds pass with `0 warnings / 0 errors`; two independent Release test processes both pass `1235 / 0 / 1235`, 99 above the 1136 baseline. `git diff --check` passes. Final CI and the full final SHA are recorded in Draft PR #7 and the final task report.

## Environment boundary

No screenshot was viewed or analyzed. No formal administrator EXE was launched. Manual visual validation, real-DPI validation, real PresentMon capture validation, and real administrator Advanced Sensors smoothness validation were not performed. These remain explicit residual checks rather than inferred automated successes.
