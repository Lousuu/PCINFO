using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Themes;

namespace HardwareVision.Tests;

internal static class BugFixRegressionTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Bugfix motion 01 pending navigation replays after load", PendingNavigationReplaysAfterLoad),
        ("Bugfix motion 02 multiple pending navigations keep latest", MultiplePendingNavigationsKeepLatest),
        ("Bugfix motion 03 unloaded pending navigation does not replay", UnloadedPendingNavigationDoesNotReplay),
        ("Bugfix pagehost 01 surface clips and covers background", MotionSurfaceClipsAndCoversBackground),
        ("Bugfix pagehost 02 fast navigation keeps final content", FastNavigationKeepsFinalContent)
    ];

    private static void PendingNavigationReplaysAfterLoad()
    {
        MotionTransitionHost host = CreateHost();
        host.Content = new Border();
        Border pending = new();
        host.Content = pending;

        TestSupport.True(host.PendingTransition, "pending before load");

        WithHostedHost(host, _ =>
        {
            DrainDispatcher(host.Dispatcher);
            TestSupport.False(host.PendingTransition, "pending after load");
            TestSupport.Equal(1, host.TransitionExecutionCount, "single replay");
            TestSupport.True(ReferenceEquals(pending, host.Content), "pending content retained");
        });
    }

    private static void MultiplePendingNavigationsKeepLatest()
    {
        MotionTransitionHost host = CreateHost();
        host.Content = new Border();
        Border first = new();
        Border second = new();
        host.Content = first;
        host.Content = second;

        WithHostedHost(host, _ =>
        {
            DrainDispatcher(host.Dispatcher);
            TestSupport.Equal(1, host.TransitionExecutionCount, "single replay for collapsed pending transitions");
            TestSupport.True(ReferenceEquals(second, host.Content), "latest pending content");
        });
    }

    private static void UnloadedPendingNavigationDoesNotReplay()
    {
        MotionTransitionHost host = CreateHost();
        host.Content = new Border();
        host.Content = new Border();
        host.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        TestSupport.False(host.PendingTransition, "pending cleared on unload");
        DrainDispatcher(host.Dispatcher);
        TestSupport.Equal(0, host.TransitionExecutionCount, "no replay after unload");
    }

    private static void MotionSurfaceClipsAndCoversBackground()
    {
        MotionTransitionHost host = CreateHost();
        host.Content = new Border();

        WithHostedHost(host, _ =>
        {
            Border surface = TestSupport.NotNull(
                host.Template.FindName("MotionSurface", host) as Border,
                "MotionSurface");
            TestSupport.True(host.ClipToBounds, "host clips displaced content");
            TestSupport.True(surface.Background is Brush, "surface has non-transparent brush");
            TestSupport.Equal(HorizontalAlignment.Stretch, surface.HorizontalAlignment, "surface horizontal stretch");
            TestSupport.Equal(VerticalAlignment.Stretch, surface.VerticalAlignment, "surface vertical stretch");
            TestSupport.Equal(0d, surface.MinWidth, "surface min width");
            TestSupport.Equal(0d, surface.MinHeight, "surface min height");
        });
    }

    private static void FastNavigationKeepsFinalContent()
    {
        MotionTransitionHost host = CreateHost();
        Border initial = new();
        Border first = new();
        Border second = new();
        host.Content = initial;

        WithHostedHost(host, _ =>
        {
            host.Content = first;
            host.Content = second;
            DrainDispatcher(host.Dispatcher);

            TestSupport.True(ReferenceEquals(second, host.Content), "final content after rapid navigation");
            TestSupport.False(host.PendingTransition, "no pending transition after rapid navigation");
            TestSupport.True(host.TransitionExecutionCount >= 1, "rapid navigation executes latest transition");
            TestSupport.False(host.HasAnimatedProperties, "animated properties restored");
        });
    }

    private static MotionTransitionHost CreateHost()
    {
        MotionTransitionHost host = new()
        {
            Style = (Style)GetApplication().FindResource("MotionTransitionHostStyle"),
            IsTransitionEnabled = true
        };
        MotionContext.SetCurrentProfile(host, MotionProfile.Create(MotionLevel.Standard, MotionLevel.Standard, string.Empty));
        return host;
    }

    private static void WithHostedHost(MotionTransitionHost host, Action<Window> test)
    {
        Window window = new()
        {
            Content = new UserControl { Content = host },
            Width = 920d,
            Height = 620d,
            Left = -32000d,
            Top = -32000d,
            Opacity = 0d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };

        try
        {
            window.Show();
            host.ApplyTemplate();
            window.UpdateLayout();
            test(window);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static System.Windows.Application GetApplication()
    {
        if (System.Windows.Application.Current is not null)
        {
            return System.Windows.Application.Current;
        }

        HardwareVision.App application = new();
        application.InitializeComponent();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        return application;
    }
}
