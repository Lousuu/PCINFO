namespace HardwareVision.Tests;

internal static class StartupSequenceContractTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup contract 01 App owns one startup service", () => AppContains("StartupSequenceService = new StartupSequenceService")),
        ("Startup contract 02 sequence starts once after Show", ServiceStartsOnceAfterShow),
        ("Startup contract 03 theme milestone uses applied theme", () => AppContains("StartupMilestoneId.ThemeResources")),
        ("Startup contract 04 service graph milestone is real", () => AppContains("StartupMilestoneId.ServiceGraph")),
        ("Startup contract 05 page router milestone is real", () => WindowContains("StartupMilestoneId.PageRouter")),
        ("Startup contract 06 sensor bus reuses polling event", () => AppContains("PollingService.ReadingsUpdated += handler")),
        ("Startup contract 07 sensor bus observes polling failure", () => AppContains("PollingService.PollingFailed += failureHandler")),
        ("Startup contract 08 history uses shared polling source", () => AppContains("new SensorHistoryService(PollingService)")),
        ("Startup contract 09 shell readiness uses layout", ShellUsesLayoutReadiness),
        ("Startup contract 10 no startup DispatcherTimer", () => StartupSourcesExclude("DispatcherTimer")),
        ("Startup contract 11 no startup rendering loop", () => StartupSourcesExclude("CompositionTarget.Rendering")),
        ("Startup contract 12 startup uses Task.Delay clock", () => Contains(Read("HardwareVision", "Services", "SystemStartupSequenceClock.cs"), "Task.Delay(delay, cancellationToken)")),
        ("Startup contract 13 no second window", () => TestSupport.Equal(1, Count("HardwareVision", "MainWindow.xaml", "<Window "), "window count")),
        ("Startup contract 14 one MainShellHost", () => TestSupport.Equal(1, Count("HardwareVision", "MainWindow.xaml", "<shell:MainShellHost"), "shell count")),
        ("Startup contract 15 one PageHost", () => TestSupport.Equal(1, Count("HardwareVision", "Views", "Shell", "MainShellHost.xaml", "x:Name=\"PageHost\""), "PageHost count")),
        ("Startup contract 16 one CurrentPage binding", () => TestSupport.Equal(1, Count("HardwareVision", "Views", "Shell", "MainShellHost.xaml", "Content=\"{Binding CurrentPage}\""), "CurrentPage binding count")),
        ("Startup contract 17 startup overlay Z is 120", () => ShellXamlContains("Panel.ZIndex=\"120\"")),
        ("Startup contract 18 system rewire Z is 100", () => ShellXamlContains("Panel.ZIndex=\"100\"")),
        ("Startup contract 19 flow relay Z is 40", () => ShellXamlContains("Panel.ZIndex=\"40\"")),
        ("Startup contract 20 navigation blocked while active", () => MainContains("|| IsStartupSequenceActive && !isInitialNavigation")),
        ("Startup contract 21 hidden window completes sequence", () => WindowContains("CompleteForHiddenWindow")),
        ("Startup contract 22 close cancels sequence", () => WindowContains("startupSequenceService.Cancel()")),
        ("Startup contract 23 startup service disposed", () => AppContains("startup-sequence-service")),
        ("Startup contract 24 no scale animation", () => StartupSourcesExclude("ScaleTransform")),
        ("Startup contract 25 no blur", () => StartupSourcesExclude("BlurEffect")),
        ("Startup contract 26 no shader", () => StartupSourcesExclude("ShaderEffect")),
        ("Startup contract 27 no screenshot copy", () => StartupSourcesExclude("RenderTargetBitmap")),
        ("Startup contract 28 no page VisualBrush", () => StartupSourcesExclude("VisualBrush")),
        ("Startup contract 29 overlay is not focusable", () => OverlayContains("Focusable=\"False\"", "IsTabStop=\"False\"")),
        ("Startup contract 30 overlay has live announcement", () => OverlayContains("AutomationProperties.LiveSetting=\"Assertive\"")),
        ("Startup contract 31 fixed status copy", () => OverlayContains("SYS/BOOT.00", "TRACEWORK", "启动中", "COLD START / LOCAL", "INITIAL PROJECTION")),
        ("Startup contract 32 no fake progress", () => OverlayExcludes("ProgressBar", "%")),
        ("Startup contract 33 no circular spinner", () => OverlayExcludes("Ellipse")),
        ("Startup contract 34 no centered logo", () => OverlayExcludes("Logo")),
        ("Startup contract 35 one shared six-row matrix", OneSharedRouteMatrix),
        ("Startup contract 36 commit lock structure", () => OverlayContains("x:Name=\"CommitLock\"", "Width=\"22\" Height=\"1\"", "Width=\"1\" Height=\"22\"", "x:Name=\"CommitCenter\"")),
        ("Startup contract 37 reduced has opacity branch", () => OverlayCodeContains("MotionLevel.Reduced", "BeginAnimation(OpacityProperty")),
        ("Startup contract 38 Off collapses overlay", () => OverlayCodeContains("snapshot.MotionLevel == MotionLevel.Off", "RestoreFinalState()")),
        ("Startup contract 39 Classic uses plain reveal", () => RevealContains("snapshot.CurrentTheme == AppTheme.Classic", "TimeSpan.FromMilliseconds(120)")),
        ("Startup contract 40 reveal restores hit testing", () => RevealContains("target.IsHitTestVisible = true")),
        ("Startup contract 41 projection uses three live segments", ProjectionUsesThreeLiveSegments),
        ("Startup contract 42 all surface entry points converge", SurfaceEntryPointsConverge),
        ("Startup contract 43 ContentRendered never starts sequence", () => Excludes(Window, "startupSequenceService.StartAsync")),
        ("Startup contract 44 Phase uses explicit presentation", () => OverlayCodeContains("StartupPhasePresentation.Create", "BottomPhaseCode.Text")),
        ("Startup contract 45 COMMIT is one conditional group", CommitIsConditionalGroup)
    ];

    private static string Read(params string[] parts) => TraceworkPilotSource.Read(parts);
    private static string App => Read("HardwareVision", "App.xaml.cs");
    private static string Window => Read("HardwareVision", "MainWindow.xaml.cs");
    private static string Main => Read("HardwareVision", "ViewModels", "MainViewModel.cs");
    private static string Shell => Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml");
    private static string Overlay => Read("HardwareVision", "Views", "Shell", "TraceworkStartupSequenceOverlay.xaml");
    private static string OverlayCode => Read("HardwareVision", "Views", "Shell", "TraceworkStartupSequenceOverlay.xaml.cs");
    private static string MilestoneRow => Read("HardwareVision", "Views", "Shell", "StartupMilestoneRow.xaml");
    private static string Reveal => Read("HardwareVision", "Controls", "StartupShellRevealCoordinator.cs");
    private static string Service => Read("HardwareVision", "Services", "StartupSequenceService.cs");
    private static void AppContains(params string[] values) => Contains(App, values);
    private static void WindowContains(params string[] values) => Contains(Window, values);
    private static void MainContains(params string[] values) => Contains(Main, values);
    private static void ShellXamlContains(params string[] values) => Contains(Shell, values);
    private static void OverlayContains(params string[] values) => Contains(Overlay, values);
    private static void OverlayCodeContains(params string[] values) => Contains(OverlayCode, values);
    private static void MilestoneRowContains(params string[] values) => Contains(MilestoneRow, values);
    private static void RevealContains(params string[] values) => Contains(Reveal, values);
    private static void ServiceContains(params string[] values) => Contains(Service, values);
    private static void OverlayExcludes(params string[] values) => Excludes(Overlay, values);

    private static void Contains(string source, params string[] values)
    {
        foreach (string value in values) TestSupport.True(source.Contains(value, StringComparison.Ordinal), value);
    }

    private static void Excludes(string source, params string[] values)
    {
        foreach (string value in values) TestSupport.False(source.Contains(value, StringComparison.Ordinal), value);
    }

    private static void StartupSourcesExclude(string value)
    {
        foreach (string source in new[] { Service, Overlay, OverlayCode, Reveal }) Excludes(source, value);
    }

    private static int Count(params string[] partsAndValue)
    {
        string value = partsAndValue[^1];
        string source = Read(partsAndValue[..^1]);
        return TraceworkPilotSource.Count(source, value);
    }

    private static void ServiceStartsOnceAfterShow()
    {
        int start = App.IndexOf("StartupSequenceService.StartAsync", StringComparison.Ordinal);
        int show = App.IndexOf("mainWindow.Show()", StringComparison.Ordinal);
        TestSupport.True(show >= 0 && start > show, "startup begins after Show");
        TestSupport.Equal(1, TraceworkPilotSource.Count(App, "StartupSequenceService.StartAsync"), "App start count");
        Contains(Window, "ContentRendered += OnContentRendered", "TryReportStartupSurfaceReady");
        Excludes(Window, "startupSequenceService.StartAsync()");
        Contains(Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml.cs"),
            "StartupSequenceOverlay.IsLoaded", "ReportStartupSurfaceReady");
    }

    private static void ShellUsesLayoutReadiness()
    {
        string source = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml.cs");
        Contains(source, "LayoutUpdated += OnLayoutUpdated", "ActualWidth > 0d", "PageHost.ActualWidth > 0d", "TryReportStartupSurfaceReady");
        Excludes(source, "Task.Delay");
    }

    private static void OneSharedRouteMatrix()
    {
        TestSupport.Equal(1, TraceworkPilotSource.Count(Overlay, "ItemsSource=\"{Binding Milestones}\""), "milestone ItemsControl count");
        Excludes(Overlay, "UniformGrid");
        Contains(MilestoneRow, "<ColumnDefinition Width=\"24\" />", "<ColumnDefinition Width=\"180\" />", "<ColumnDefinition Width=\"72\" />");
    }

    private static void SurfaceEntryPointsConverge()
    {
        string source = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml.cs");
        Contains(source,
            "MainShellHost.Loaded / DispatcherPriority.Loaded",
            "MainShellHost.SizeChanged",
            "MainShellHost.LayoutUpdated",
            "TryReportStartupSurfaceReady");
        Contains(Window, "MainWindow.ContentRendered / DispatcherPriority.Render", "TryReportStartupSurfaceReady");
    }

    private static void CommitIsConditionalGroup()
    {
        OverlayContains("x:Name=\"CommitGroup\"");
        TestSupport.Equal(1, TraceworkPilotSource.Count(Overlay, "Text=\"COMMIT\""), "COMMIT text count");
        OverlayCodeContains("snapshot.Phase == StartupSequencePhase.Lock && snapshot.CanCommit", "CommitGroup.Visibility");
    }

    private static void ProjectionUsesThreeLiveSegments()
    {
        OverlayContains(
            "x:Name=\"ProjectionSourceHorizontalSegment\"",
            "x:Name=\"ProjectionVerticalBridgeSegment\"",
            "x:Name=\"ProjectionTargetHorizontalSegment\"",
            "x:Name=\"ProjectionInputPort\"",
            "Width=\"5\"",
            "Height=\"5\"");
        OverlayExcludes("x:Name=\"ProjectionPulseTrack\"");
        MilestoneRowContains("x:Name=\"RouteOutputPort\"", "x:Name=\"PendingFrame\"");
        OverlayCodeContains(
            "TryCreateProjectionRoute",
            "projectionPulsePending",
            "lastPresentedResolvedCount",
            "ResolveProjectionRouteDurationMilliseconds");
    }
}
