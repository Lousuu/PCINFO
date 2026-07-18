# TRACEWORK UI Handoff

## A. Current Repository State

- Repository: `https://github.com/Lousuu/PCINFO`
- Branch: `feature/tracework-ui`
- Draft PR: `https://github.com/Lousuu/PCINFO/pull/7`
- Base branch: `main`
- Stage 4 baseline: `ecd44de024cde4204a4199e6f0dee2421f3c2726`
- Stage 4 commit message: `feat(motion): add adaptive motion infrastructure`
- Current commit: run `git rev-parse HEAD`; this file is updated by the Stage 4 commit.
- Working-tree expectation: clean after the Stage 4 commit is pushed.
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
- Stage 5: not complete.
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

Current Stage 4 product integration is limited to PageHost enter motion. Classic never plays the new transition. Reduced uses opacity only. Off is immediate. SYSTEM REWIRE is not implemented.

## G. Business Invariants

Stage 4 does not rebuild the service graph, does not rebuild page ViewModels, and does not change sensor collection, `PollingService`, PresentMon, FPS or session statistics, game session file formats, device selection persistence, metric IDs, units, ordering, or session report load lifecycle. Motion is a display-layer behavior after navigation has already completed.

## H. Tests And Validation

- Release build result: pass locally before commit.
- Debug build result: pass locally before commit.
- Final local test count: `619 passed, 0 failed, 619 total`.
- GitHub Actions result: pass for run `29658371008` on `6ddb63d510bca9ce006d7ae5a3a35713f3e63496`.
- Manual visual validation: not performed by user request.

Validation coverage includes the custom console runner, WPF runtime smoke tests, side-effect counting tests, Motion parser tests, effective downgrade matrix tests, MotionChanged tests, MotionContext tests, MotionTransitionHost tests, PageHost persistence tests, and static architecture checks.

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
- `HardwareVision/Themes/Tracework/Motion.xaml`
- `HardwareVision/ViewModels/MainViewModel.cs`
- `HardwareVision/ViewModels/SettingsViewModel.cs`
- `HardwareVision/Views/Settings/ClassicSettingsLayout.xaml`
- `HardwareVision/Views/Settings/TraceworkSettingsLayout.xaml`
- `HardwareVision/Views/*/*Layout.xaml`
- `HardwareVision.Tests/MotionInfrastructureTests.cs`
- `HardwareVision.Tests/XamlRuntimeSmokeTests.cs`
- `HardwareVision.Tests/MainShellStateTests.cs`
- `HardwareVision.Tests/TraceworkConfigurationPageTests.cs`

## J. Stage 5 Boundary

Stage 5 may reuse `MotionService`, `MotionContext`, and `MotionTransitionHost` for short, bounded motion such as SYSTEM REWIRE, theme-switch masking, Trace/Latch/Splice micro transitions, local shell transitions, status indicator micro motion, and final visual density/polish.

Stage 5 must not rewrite `ThemeService`, rewrite `MotionService`, create a second PageHost, rebuild page ViewModels, add infinite animation, use shaders, introduce a heavy UI framework, add game visual assets, or change business statistics.

## K. Known Risks And Manual Checks

- Full-page visual inspection has not been performed.
- 125% and 150% DPI have not been manually checked.
- Real Tier0/Tier1 low-performance machines have not been manually validated.
- Windows animation-disabled behavior has not been manually validated.
- High contrast has not been manually validated.
- Remote desktop has not been manually validated.
- The formal administrator EXE was not launched in this stage.
- Before Stage 5, manually check theme switching, PageHost navigation, Settings motion changes, DPI, high contrast, remote desktop, and a low-tier graphics environment.

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
