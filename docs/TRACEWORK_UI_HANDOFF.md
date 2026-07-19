# TRACEWORK UI Handoff

## A. Current Repository State

- Repository: `https://github.com/Lousuu/PCINFO`
- Branch: `feature/tracework-ui`
- Draft PR: `https://github.com/Lousuu/PCINFO/pull/7`
- Base branch: `main`
- Stage 5 baseline: `97309228770f81565decbb40b8151aca6e742ae0`
- Stage 5 commit message: `feat(ui): add System Rewire theme transition and polish`
- Current commit: run `git rev-parse HEAD`; this file is updated by the Stage 5 commit.
- Working-tree expectation: clean after the Stage 5 commit is pushed.
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

`MotionContext` exposes inherited attached properties so pages and controls receive motion state from the tree, not from `Application.Current` or a static mutable profile. `MotionTransitionHost` only animates its internal `MotionSurface` opacity and translate transform. It uses no scale, blur, shader, layout property animation, double-buffered page copy, or continuous animation.

Current Stage 4 product integration is limited to PageHost enter motion. Classic never plays the PageHost transition. Reduced uses opacity only. Off is immediate.

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
- Final local test count: `643 passed, 0 failed, 643 total`.
- GitHub Actions result: pass for run `29676111020` on `b87bd0ebd0754e9af16f662490703c51a736e6e9`.
- Manual visual validation: not performed by user request.

Validation coverage includes the custom console runner, WPF runtime smoke tests, side-effect counting tests, Motion parser tests, effective downgrade matrix tests, MotionChanged tests, MotionContext tests, MotionTransitionHost tests, PageHost persistence tests, ThemeTransition phase/result tests, Rewire XAML 01..12 runtime tests, and static architecture checks.

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
