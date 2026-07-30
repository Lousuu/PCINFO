using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Themes;
using HardwareVision.Views.AdvancedSensors;
using HardwareVision.Views.Cpu;
using HardwareVision.Views.Dashboard;
using HardwareVision.Views.Disk;
using HardwareVision.Views.GamePerformance;
using HardwareVision.Views.GameSessionReport;
using HardwareVision.Views.Gpu;
using HardwareVision.Views.Memory;
using HardwareVision.Views.MetricVisibility;
using HardwareVision.Views.Motherboard;
using HardwareVision.Views.Network;
using HardwareVision.Views.Settings;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class MotionRuntimeIntegrationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Motion runtime 01 real page roles resolve after layout", RealPageRolesResolveAfterLayout),
        ("Motion runtime 02 navigation samples survive host resize", NavigationSamplesSurviveHostResize),
        ("Motion runtime 03 startup snapshots preserve prepared Dashboard", StartupSnapshotsPreservePreparedDashboard),
        ("Motion runtime 04 projection pulse produces real geometry frames", ProjectionPulseProducesRealGeometryFrames),
        ("Motion runtime 05 rapid navigation keeps latest real page", RapidNavigationKeepsLatestRealPage),
        ("Motion runtime 06 CPU to GPU has real intermediate frames", CpuToGpuHasRealIntermediateFrames),
        ("Motion runtime 07 Relay overlaps real page visuals for one rendered frame", RelayOverlapsRealPageVisuals)
    ];

    private static void RealPageRolesResolveAfterLayout()
    {
        EnsureApplication();
        (FrameworkElement Page, string Primary, string Secondary)[] pages =
        [
            (new TraceworkDashboardLayout(), "DashboardPrimaryRegion", "DashboardSecondaryRegion"),
            (new TraceworkCpuLayout(), "CpuPrimaryChartField", "CpuSecondaryRegion"),
            (new TraceworkGpuLayout(), "GpuPrimaryColumn", "GpuSecondaryColumn"),
            (new TraceworkMemoryLayout(), "MemoryCapacityField", "MemoryModuleSpecificationMatrix"),
            (new TraceworkDiskLayout(), "StorageHealthGrid", "StorageSecondaryTopologyRegion"),
            (new TraceworkNetworkLayout(), "NetworkPrimaryThroughputField", "NetworkSecondaryIdentityRegion"),
            (new TraceworkMotherboardLayout(), "MotherboardIdentityPlate", "MotherboardFirmwareRegion"),
            (new TraceworkAdvancedSensorsLayout(), "AdvancedSensorSignalMatrix", "AdvancedSensorFilterRail"),
            (new TraceworkGamePerformanceLayout(), "GamePrimaryControlWorkspace", "GameSecondaryTelemetryRegion"),
            (new TraceworkGameSessionReportLayout(), "SessionRightColumn", "SessionLeftColumn"),
            (new TraceworkSettingsLayout(), "SettingsControlWorkspace", "SettingsProfileStateRegion"),
            (new TraceworkMetricVisibilityLayout(), "MetricControlMatrix", "MetricCategorySummary")
        ];

        WithShell((shell, window, pageHost) =>
        {
            foreach ((FrameworkElement page, string expectedPrimary, string expectedSecondary) in pages)
            {
                pageHost.Content = page;
                Pump(TimeSpan.FromMilliseconds(25));
                window.UpdateLayout();
                FrameworkElement primary = TestSupport.NotNull(
                    pageHost.FindRoleElement(NavigationMotionRole.Primary),
                    $"{page.GetType().Name} primary");
                FrameworkElement secondary = TestSupport.NotNull(
                    pageHost.FindRoleElement(NavigationMotionRole.Secondary),
                    $"{page.GetType().Name} secondary");
                TestSupport.Equal(expectedPrimary, primary.Name, $"{page.GetType().Name} primary name");
                TestSupport.Equal(expectedSecondary, secondary.Name, $"{page.GetType().Name} secondary name");
                TestSupport.True(
                    primary.IsLoaded && primary.ActualWidth > 0d && primary.ActualHeight > 0d,
                    $"{expectedPrimary} arranged");
                TestSupport.True(
                    secondary.IsLoaded && secondary.ActualWidth > 0d && secondary.ActualHeight > 0d,
                    $"{expectedSecondary} arranged");
            }
        });
    }

    private static void NavigationSamplesSurviveHostResize()
    {
        WithShell((_, window, pageHost) =>
        {
            NavigationTransitionPlan plan = NavigationTransitionPlan.Create(
                MotionProfile.Create(MotionLevel.Full, MotionLevel.Full, string.Empty));
            pageHost.Content = new TraceworkDashboardLayout();
            Pump(TimeSpan.FromMilliseconds(30));

            const long version = 41;
            pageHost.PrepareNavigation(plan, NavigationTransitionDirection.FromBottom, version);
            FrameworkElement oldRoot = TestSupport.NotNull(pageHost.ActiveRoot, "exit root");
            FrameworkElement oldPrimary = TestSupport.NotNull(pageHost.ActivePrimary, "exit primary");
            FrameworkElement oldSecondary = TestSupport.NotNull(pageHost.ActiveSecondary, "exit secondary");
            TranslateTransform rootTransform = (TranslateTransform)pageHost.Template.FindName(
                "MotionTranslateTransform",
                pageHost);
            TestSupport.True(
                Math.Abs(oldSecondary.Opacity - 1d) <= 0.001d,
                "old secondary starts fully visible");
            pageHost.PlayExit(plan, NavigationTransitionDirection.FromBottom, version);
            List<double> oldRootSamples = [];
            List<double> oldPrimarySamples = [];
            List<double> oldSecondarySamples = [];
            List<double> oldTranslateSamples = [];
            for (int index = 0; index < 5; index++)
            {
                Pump(TimeSpan.FromMilliseconds(18));
                if (index == 0)
                {
                    TestSupport.True(
                        DependencyPropertyHelper
                            .GetValueSource(oldSecondary, UIElement.OpacityProperty)
                            .IsAnimated,
                        "old secondary opacity is animated after exit starts");
                }
                oldRootSamples.Add(oldRoot.Opacity);
                oldPrimarySamples.Add(oldPrimary.Opacity);
                oldSecondarySamples.Add(oldSecondary.Opacity);
                oldTranslateSamples.Add(rootTransform.Y);
            }
            TestSupport.True(pageHost.Content is TraceworkDashboardLayout, "Dashboard remains before Relay");
            TestSupport.True(Distinct(oldRootSamples) >= 3, "three old root opacity frames");
            TestSupport.True(Distinct(oldPrimarySamples) >= 3, "three old primary opacity frames");
            VerifySecondaryExit(pageHost, oldSecondary, plan, version, oldSecondarySamples);
            TestSupport.True(Distinct(oldTranslateSamples) >= 2, "two old translate frames");

            pageHost.Content = new TraceworkCpuLayout();
            pageHost.PrepareCommittedContent(plan, NavigationTransitionDirection.FromBottom, version);
            Pump(TimeSpan.FromMilliseconds(35));
            TestSupport.True(pageHost.Content is TraceworkCpuLayout, "CPU committed at Relay");
            pageHost.PlayEnter(plan, NavigationTransitionDirection.FromBottom, version);

            FrameworkElement root = TestSupport.NotNull(pageHost.ActiveRoot, "enter root");
            FrameworkElement primary = TestSupport.NotNull(pageHost.ActivePrimary, "enter primary");
            FrameworkElement secondary = TestSupport.NotNull(pageHost.ActiveSecondary, "enter secondary");
            List<double> rootSamples = [];
            List<double> primarySamples = [];
            List<double> secondarySamples = [];
            List<double> translateSamples = [];
            double rootBeforeResize = 0d;
            double primaryBeforeResize = 0d;
            for (int index = 0; index < 5; index++)
            {
                Pump(TimeSpan.FromMilliseconds(24));
                rootSamples.Add(root.Opacity);
                primarySamples.Add(primary.Opacity);
                secondarySamples.Add(secondary.Opacity);
                translateSamples.Add(rootTransform.Y);
                if (index == 1)
                {
                    rootBeforeResize = root.Opacity;
                    primaryBeforeResize = primary.Opacity;
                    window.Width = 1080d;
                    window.Height = 700d;
                    Pump(TimeSpan.FromMilliseconds(5));
                }
            }
            TestSupport.True(Distinct(rootSamples) >= 3, "three new root opacity frames");
            TestSupport.True(Distinct(primarySamples) >= 3, "three new primary opacity frames");
            TestSupport.True(Distinct(secondarySamples) >= 3, "three new secondary opacity frames");
            TestSupport.True(Distinct(translateSamples) >= 2, "two new translate frames");
            TestSupport.Equal("CpuPrimaryChartField", primary.Name, "CPU primary runtime role");
            TestSupport.Equal("CpuSecondaryRegion", secondary.Name, "CPU secondary runtime role");

            TestSupport.Equal(version, pageHost.ActiveNavigationVersion, "resize preserves navigation version");
            TestSupport.True(
                pageHost.Diagnostics.Any(item =>
                    item.EventName == "HostSizeChangedDuringMotion" && item.Reason == "Continue"),
                "resize continuation diagnostic");
            TestSupport.True(root.Opacity >= rootBeforeResize, "root continues after resize");
            TestSupport.True(primary.Opacity >= primaryBeforeResize, "primary continues after resize");

            pageHost.CompleteNavigation(version);
            Pump(TimeSpan.FromMilliseconds(190));
            AssertFinal(pageHost);
            TestSupport.Equal(
                1,
                pageHost.Diagnostics.Count(item => item.EventName == "RelayCommitted"
                    && item.NavigationVersion == version),
                "single relay commit");
            foreach (string eventName in new[]
                     {
                         "NavigationPrepared",
                         "PageExitStarted",
                         "PageExitSampled",
                         "PageExitCompleted",
                         "RelayCommitted",
                         "CommittedContentPrepared",
                         "PageEnterStarted",
                         "PageEnterSampled",
                         "PageEnterCompleted",
                         "NavigationFinalized"
                     })
            {
                TestSupport.True(
                    pageHost.Diagnostics.Any(item =>
                        item.EventName == eventName && item.NavigationVersion == version),
                    eventName);
            }
        });
    }

    private static void VerifySecondaryExit(
        MotionTransitionHost pageHost,
        FrameworkElement secondary,
        NavigationTransitionPlan plan,
        long navigationVersion,
        List<double> samples)
    {
        const double epsilon = 0.001d;
        double target = plan.SecondaryCommitOpacity;
        TimeSpan deadline = TimeSpan.FromMilliseconds(
            Math.Max(
                plan.PageExitDuration.TotalMilliseconds,
                Math.Max(
                    (plan.PrimaryExitDelay + plan.PrimaryExitDuration).TotalMilliseconds,
                    (plan.SecondaryExitDelay + plan.SecondaryExitDuration).TotalMilliseconds))
            + 500d);
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (Math.Abs(secondary.Opacity - target) > epsilon &&
               stopwatch.Elapsed < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(5));
            samples.Add(secondary.Opacity);
            TestSupport.Equal(
                navigationVersion,
                pageHost.ActiveNavigationVersion,
                "secondary exit preserves navigation version");
        }

        TestSupport.True(samples.Count > 0, "secondary exit produced a dispatcher sample");
        foreach (double sample in samples)
        {
            TestSupport.True(
                sample >= target - epsilon && sample <= 1d + epsilon,
                $"secondary exit opacity stays within [{target:0.###}, 1]: {sample:0.###}");
        }
        bool firstDispatcherSampleReachedTarget =
            Math.Abs(samples[0] - target) <= epsilon;
        bool observedOpacityChange =
            samples.Any(sample => Math.Abs(sample - 1d) > epsilon);
        TestSupport.True(
            observedOpacityChange || firstDispatcherSampleReachedTarget,
            "secondary exit changes opacity or reaches commit target on first dispatcher sample");
        TestSupport.True(
            Math.Abs(secondary.Opacity - target) <= epsilon,
            $"secondary exit reaches commit opacity {target:0.###}");
        TestSupport.Equal(
            navigationVersion,
            pageHost.ActiveNavigationVersion,
            "secondary exit completion preserves navigation version");
    }

    private static void StartupSnapshotsPreservePreparedDashboard()
    {
        WithShell((shell, _, pageHost) =>
        {
            pageHost.Content = new TraceworkDashboardLayout();
            Pump(TimeSpan.FromMilliseconds(30));
            TraceworkStartupSequenceOverlay overlay = TestSupport.NotNull(
                shell.FindName("StartupSequenceOverlay") as TraceworkStartupSequenceOverlay,
                "startup overlay");
            MethodInfo apply = TestSupport.NotNull(
                typeof(MainShellHost).GetMethod(
                    "ApplyStartupSequence",
                    BindingFlags.Instance | BindingFlags.NonPublic),
                "ApplyStartupSequence");

            foreach (StartupSequencePhase phase in new[]
                     {
                         StartupSequencePhase.Index,
                         StartupSequencePhase.Route,
                         StartupSequencePhase.Bind,
                         StartupSequencePhase.Lock
                     })
            {
                StartupSequenceSnapshot snapshot =
                    ActiveStartup(phase) with { CanCommit = false };
                overlay.Snapshot = snapshot;
                apply.Invoke(shell, [snapshot]);
                Pump(TimeSpan.FromMilliseconds(12));
            }

            TestSupport.True(pageHost.IsStartupRevealPrepared, "startup prepared");
            TestSupport.Equal(0.32d, TestSupport.NotNull(pageHost.ActiveRoot, "startup root").Opacity, "root preserved");
            TestSupport.Equal(0.42d, TestSupport.NotNull(pageHost.ActivePrimary, "startup primary").Opacity, "primary preserved");
            TestSupport.Equal(0.24d, TestSupport.NotNull(pageHost.ActiveSecondary, "startup secondary").Opacity, "secondary preserved");
            TestSupport.True(
                pageHost.Diagnostics.Count(item => item.EventName == "StartupRevealPrepared") == 1,
                "startup prepared once");
            TestSupport.True(
                pageHost.Diagnostics.Any(item => item.EventName == "StartupRevealStatePreserved"),
                "active snapshots preserve state");

            StartupSequenceSnapshot reveal =
                ActiveStartup(StartupSequencePhase.Reveal) with { CanCommit = false };
            overlay.Snapshot = reveal;
            apply.Invoke(shell, [reveal]);
            FrameworkElement overlayContent = overlay;
            TraceworkShellChrome chrome = (TraceworkShellChrome)shell.FindName("TraceworkChrome");
            FrameworkElement shellTarget = chrome.StartupSignalRailTarget;
            List<double> overlaySamples = [];
            List<double> rootSamples = [];
            List<double> primarySamples = [];
            List<double> secondarySamples = [];
            List<double> shellSamples = [];
            for (int index = 0; index < 45; index++)
            {
                Pump(TimeSpan.FromMilliseconds(10));
                overlaySamples.Add(overlayContent.Opacity);
                rootSamples.Add(pageHost.ActiveRoot?.Opacity ?? 1d);
                primarySamples.Add(pageHost.ActivePrimary?.Opacity ?? 1d);
                secondarySamples.Add(pageHost.ActiveSecondary?.Opacity ?? 1d);
                shellSamples.Add(shellTarget.Opacity);
            }
            TestSupport.True(Distinct(overlaySamples) >= 4, "four overlay exit frames");
            TestSupport.True(Distinct(rootSamples) >= 4, "four Dashboard root enter frames");
            TestSupport.True(Distinct(primarySamples) >= 4, "four Dashboard primary enter frames");
            TestSupport.True(
                Distinct(secondarySamples) >= 4,
                $"four Dashboard secondary enter frames (distinct={Distinct(secondarySamples)})");
            TestSupport.True(Distinct(shellSamples) >= 3, "shell target enters continuously");
            int primaryStart = Enumerable.Range(0, primarySamples.Count)
                .First(index => primarySamples[index] > 0.421d);
            int secondaryStart = Enumerable.Range(0, secondarySamples.Count)
                .First(index => secondarySamples[index] > 0.241d);
            TestSupport.True(
                primaryStart < secondaryStart,
                "Dashboard primary establishes before secondary");
            TestSupport.Equal(
                Visibility.Collapsed,
                overlay.Visibility,
                "overlay collapsed after Dashboard begins");
            AssertFinal(pageHost);
        });
    }

    private static void ProjectionPulseProducesRealGeometryFrames()
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new();
        using WindowScope scope = new(overlay);
        overlay.Snapshot = ActiveStartup(StartupSequencePhase.Index);
        Pump(TimeSpan.FromMilliseconds(230));
        overlay.Snapshot = ActiveStartup(StartupSequencePhase.Route);
        Pump(TimeSpan.FromMilliseconds(230));
        overlay.Snapshot = BindSnapshot(71, pollingVersion: 0, resolvedCount: 0);
        PumpUntil(() => overlay.IsProjectionLedgerReady, TimeSpan.FromSeconds(1), "projection ledger ready");
        overlay.Snapshot = BindSnapshot(72, pollingVersion: 2, resolvedCount: 1);
        Pump(TimeSpan.FromMilliseconds(30));
        TestSupport.False(
            overlay.IsProjectionPulsePlaybackLatched
                || overlay.IsProjectionPulseActive
                || overlay.IsProjectionPulsePending,
            "partial projection starts no route pulse");
        overlay.Snapshot = BindSnapshot(73, pollingVersion: 2, resolvedCount: 6);
        PumpUntil(
            () => overlay.IsProjectionPulseActive && overlay.LastProjectionRoute is not null,
            TimeSpan.FromSeconds(1),
            "projection pulse active");

        FrameworkElement source = (FrameworkElement)overlay.FindName("ProjectionSourceHorizontalSegment");
        List<double> intermediateWidths = [];
        List<double> visibleOpacities = [];
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(260);
        while (DateTime.UtcNow < deadline && intermediateWidths.Distinct().Count() < 2)
        {
            Pump(TimeSpan.FromMilliseconds(10));
            if (source.Clip is RectangleGeometry clip
                && clip.Rect.Width > 0d
                && clip.Rect.Width < source.Width)
            {
                intermediateWidths.Add(clip.Rect.Width);
                visibleOpacities.Add(source.Opacity);
            }
        }

        TestSupport.True(source.IsLoaded && source.Visibility == Visibility.Visible, "projection source visible");
        TestSupport.True(overlay.LastProjectionRoute!.Value.TotalRouteLength > 24d, "finite projection geometry");
        TestSupport.True(source.Width > 1d || source.Height > 1d, "projection geometry has visible bounds");
        TestSupport.True(intermediateWidths.Distinct().Count() >= 2, "two distinct projection intermediate frames");
        TestSupport.True(visibleOpacities.Any(value => value > 0d), "projection opacity becomes visible");
        PumpUntil(() => !overlay.IsProjectionPulseActive, TimeSpan.FromSeconds(2), "projection pulse completes");
    }

    private static void RapidNavigationKeepsLatestRealPage()
    {
        WithShell((_, _, pageHost) =>
        {
            NavigationTransitionPlan plan = NavigationTransitionPlan.Create(
                MotionProfile.Create(MotionLevel.Full, MotionLevel.Full, string.Empty));
            pageHost.Content = new TraceworkDashboardLayout();
            Pump(TimeSpan.FromMilliseconds(20));

            StartSupersedingTransition(pageHost, plan, new TraceworkCpuLayout(), 81);
            StartSupersedingTransition(pageHost, plan, new TraceworkGpuLayout(), 82);
            StartSupersedingTransition(pageHost, plan, new TraceworkMemoryLayout(), 83);
            StartSupersedingTransition(pageHost, plan, new TraceworkSettingsLayout(), 84);
            Pump(TimeSpan.FromMilliseconds(230));
            pageHost.CompleteNavigation(84);
            Pump(TimeSpan.FromMilliseconds(90));

            TestSupport.True(pageHost.Content is TraceworkSettingsLayout, "latest Settings page remains committed");
            TestSupport.True(
                pageHost.Diagnostics.Any(item =>
                    item.EventName == "NavigationCancelled"
                    && item.NavigationVersion == 81
                    && item.Reason == "Superseded"),
                "superseded navigation cancellation");
            TestSupport.True(
                pageHost.Diagnostics.Count(item =>
                    item.EventName == "NavigationCancelled"
                    && item.Reason == "Superseded") >= 3,
                "all stale targets are cancelled");
            TestSupport.Equal(
                1,
                pageHost.Diagnostics.Count(item =>
                    item.EventName == "NavigationFinalized"
                    && item.NavigationVersion == 84),
                "latest navigation finalizes once");
            AssertFinal(pageHost);
        });
    }

    private static void CpuToGpuHasRealIntermediateFrames()
    {
        WithShell((_, _, pageHost) =>
        {
            NavigationTransitionPlan plan = NavigationTransitionPlan.Create(
                MotionProfile.Create(MotionLevel.Full, MotionLevel.Full, string.Empty));
            pageHost.Content = new TraceworkCpuLayout();
            Pump(TimeSpan.FromMilliseconds(25));
            pageHost.PrepareNavigation(plan, NavigationTransitionDirection.FromBottom, 91);
            pageHost.PlayExit(plan, NavigationTransitionDirection.FromBottom, 91);
            Pump(TimeSpan.FromMilliseconds(80));
            pageHost.Content = new TraceworkGpuLayout();
            pageHost.PrepareCommittedContent(plan, NavigationTransitionDirection.FromBottom, 91);
            pageHost.PlayEnter(plan, NavigationTransitionDirection.FromBottom, 91);
            List<double> root = [];
            List<double> primary = [];
            List<double> secondary = [];
            for (int index = 0; index < 7; index++)
            {
                Pump(TimeSpan.FromMilliseconds(24));
                root.Add(pageHost.ActiveRoot?.Opacity ?? 1d);
                primary.Add(pageHost.ActivePrimary?.Opacity ?? 1d);
                secondary.Add(pageHost.ActiveSecondary?.Opacity ?? 1d);
            }
            TestSupport.True(Distinct(root) >= 3, "GPU root intermediate frames");
            TestSupport.True(Distinct(primary) >= 3, "GPU primary intermediate frames");
            TestSupport.True(Distinct(secondary) >= 3, "GPU secondary intermediate frames");
            pageHost.CompleteNavigation(91);
            Pump(TimeSpan.FromMilliseconds(130));
            AssertFinal(pageHost);
        });
    }

    private static void RelayOverlapsRealPageVisuals()
    {
        WithShell((_, _, pageHost) =>
        {
            NavigationTransitionPlan plan = NavigationTransitionPlan.Create(
                MotionProfile.Create(MotionLevel.Full, MotionLevel.Full, string.Empty));
            pageHost.Content = new TraceworkDashboardLayout();
            Pump(TimeSpan.FromMilliseconds(25));
            CachedPagePresenter presenter = TestSupport.NotNull(
                pageHost.Template.FindName("PagePresenter", pageHost) as CachedPagePresenter,
                "cached page presenter");
            int overlapFrames = 0;
            bool outgoingArranged = false;
            bool incomingArranged = false;
            bool incomingIsCpu = false;
            int visualCountAtRender = 0;
            TraceworkCpuLayout cpu = new();
            int cpuLoadedCount = 0;
            cpu.Loaded += (_, _) => cpuLoadedCount++;
            EventHandler? rendering = null;
            rendering = (_, _) =>
            {
                if (!presenter.HasPendingOverlap
                    || presenter.Child is not Grid layer
                    || layer.Children.Count != 2)
                {
                    return;
                }

                CompositionTarget.Rendering -= rendering;
                overlapFrames++;
                visualCountAtRender = presenter.PresentedVisualCount;
                ContentPresenter outgoing = (ContentPresenter)layer.Children[0];
                ContentPresenter incoming = (ContentPresenter)layer.Children[1];
                outgoingArranged = outgoing.IsLoaded
                    && outgoing.ActualWidth > 0d
                    && outgoing.ActualHeight > 0d;
                incomingArranged = incoming.IsLoaded
                    && incoming.ActualWidth > 0d
                    && incoming.ActualHeight > 0d;
                incomingIsCpu = incoming.Content is TraceworkCpuLayout;
            };
            CompositionTarget.Rendering += rendering;

            try
            {
                const long version = 101;
                pageHost.PrepareNavigation(
                    plan,
                    NavigationTransitionDirection.FromBottom,
                    version);
                pageHost.PlayExit(
                    plan,
                    NavigationTransitionDirection.FromBottom,
                    version);
                Pump(TimeSpan.FromMilliseconds(24));
                pageHost.Content = cpu;
                pageHost.PrepareCommittedContent(
                    plan,
                    NavigationTransitionDirection.FromBottom,
                    version);
                pageHost.PlayEnter(
                    plan,
                    NavigationTransitionDirection.FromBottom,
                    version);

                PumpUntil(
                    () => overlapFrames == 1,
                    TimeSpan.FromSeconds(1),
                    "one real overlap frame");
                TestSupport.Equal(2, visualCountAtRender, "two live visuals at overlap render");
                TestSupport.True(outgoingArranged, "outgoing visual remains arranged");
                TestSupport.True(incomingArranged, "incoming visual is arranged");
                TestSupport.True(incomingIsCpu, "incoming CPU is the visible target");
                TestSupport.True(
                    pageHost.ActiveRoot?.Opacity >= plan.PageStartOpacity,
                    "content surface does not enter a dark hold");

                PumpUntil(
                    () => !presenter.HasPendingOverlap,
                    TimeSpan.FromSeconds(1),
                    "overlap cleanup after rendered frame");
                TestSupport.Equal(1, presenter.PresentedVisualCount, "single visual after overlap cleanup");
                TestSupport.True(
                    presenter.PresentedContent is TraceworkCpuLayout,
                    "incoming CPU remains after cleanup");
                TestSupport.Equal(1, cpuLoadedCount, "incoming page is not reloaded by overlap cleanup");
                pageHost.CompleteNavigation(version);
                Pump(TimeSpan.FromMilliseconds(150));
                TestSupport.Equal(1, overlapFrames, "overlap is rendered exactly once");
                AssertFinal(pageHost);
            }
            finally
            {
                CompositionTarget.Rendering -= rendering;
            }
        });
    }

    private static void StartSupersedingTransition(
        MotionTransitionHost pageHost,
        NavigationTransitionPlan plan,
        FrameworkElement target,
        long version)
    {
        pageHost.PrepareNavigation(plan, NavigationTransitionDirection.FromBottom, version);
        pageHost.PlayExit(plan, NavigationTransitionDirection.FromBottom, version);
        Pump(TimeSpan.FromMilliseconds(18));
        pageHost.Content = target;
        pageHost.PrepareCommittedContent(plan, NavigationTransitionDirection.FromBottom, version);
        pageHost.PlayEnter(plan, NavigationTransitionDirection.FromBottom, version);
        Pump(TimeSpan.FromMilliseconds(38));
    }

    private static StartupSequenceSnapshot ActiveStartup(StartupSequencePhase phase) =>
        StartupSequenceSnapshot.Dormant(AppTheme.Tracework, MotionLevel.Full) with
        {
            Version = (long)phase + 1,
            Phase = phase,
            IsActive = true,
            StartedAt = DateTimeOffset.UtcNow,
            SurfaceMeasured = true,
            FirstFrameGateReleased = true,
            FirstFrameGateReleaseReason = "RuntimeTest",
            VisualReady = true,
            CanCommit = phase is StartupSequencePhase.Lock or StartupSequencePhase.Reveal
        };

    private static StartupSequenceSnapshot BindSnapshot(
        long version,
        long pollingVersion,
        int resolvedCount)
    {
        StartupSequenceSnapshot dormant =
            StartupSequenceSnapshot.Dormant(AppTheme.Tracework, MotionLevel.Full);
        StartupProjectionSlotSnapshot[] slots = dormant.InitialProjection.Slots
            .Select((slot, index) => index < resolvedCount
                ? slot with { State = StartupProjectionState.Value, Detail = "Ready" }
                : slot)
            .ToArray();
        StartupMilestoneSnapshot[] milestones = dormant.Milestones
            .Select(item => item.Id == StartupMilestoneId.SensorBus
                ? item with
                {
                    State = StartupMilestoneState.Ready,
                    StatusText = StartupMilestoneSnapshot.GetStatusText(StartupMilestoneState.Ready),
                    Detail = "Runtime test"
                }
                : item)
            .ToArray();
        return dormant with
        {
            Version = version,
            Phase = StartupSequencePhase.Bind,
            IsActive = true,
            StartedAt = DateTimeOffset.UtcNow,
            SurfaceMeasured = true,
            FirstFrameGateReleased = true,
            FirstFrameGateReleaseReason = "RuntimeTest",
            VisualReady = true,
            Milestones = milestones,
            InitialProjection = new StartupInitialProjectionSnapshot(
                pollingVersion,
                slots,
                DispatcherApplied: true,
                PostDataLayoutObserved: true)
        };
    }

    private static void WithShell(Action<MainShellHost, Window, MotionTransitionHost> test)
    {
        EnsureApplication();
        MainShellHost shell = new();
        using WindowScope scope = new(shell);
        MotionTransitionHost pageHost = TestSupport.NotNull(
            shell.FindName("PageHost") as MotionTransitionHost,
            "MainShellHost PageHost");
        MotionContext.SetCurrentProfile(
            pageHost,
            MotionProfile.Create(MotionLevel.Full, MotionLevel.Full, string.Empty));
        test(shell, scope.Window, pageHost);
    }

    private static void AssertFinal(MotionTransitionHost host)
    {
        FrameworkElement root = TestSupport.NotNull(host.ActiveRoot, "final root");
        TestSupport.Equal(1d, root.Opacity, "final root opacity");
        TestSupport.Equal(1d, host.ActivePrimary?.Opacity ?? 1d, "final primary opacity");
        TestSupport.Equal(1d, host.ActiveSecondary?.Opacity ?? 1d, "final secondary opacity");
        TranslateTransform transform = (TranslateTransform)host.Template.FindName(
            "MotionTranslateTransform",
            host);
        TestSupport.Equal(0d, transform.X, "final translate X");
        TestSupport.Equal(0d, transform.Y, "final translate Y");
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App app = new();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string label)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(5));
        }
        TestSupport.True(condition(), label);
    }

    private static void Pump(TimeSpan duration)
    {
        DispatcherFrame frame = new();
        DispatcherTimer timer = new(
            duration,
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static int Distinct(IEnumerable<double> values) =>
        values.Select(value => Math.Round(value, 3)).Distinct().Count();

    private sealed class WindowScope : IDisposable
    {
        public WindowScope(FrameworkElement content)
        {
            Window = new Window
            {
                Content = content,
                Width = 1120d,
                Height = 720d,
                Left = -32000d,
                Top = -32000d,
                Opacity = 1d,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            Window.Show();
            Window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(30));
        }

        public Window Window { get; }

        public void Dispose()
        {
            Window.Content = null;
            Window.Close();
            Pump(TimeSpan.FromMilliseconds(5));
        }
    }
}
