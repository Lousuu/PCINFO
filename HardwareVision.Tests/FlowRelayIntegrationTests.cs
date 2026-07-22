using System.Text.RegularExpressions;

namespace HardwareVision.Tests;

internal static class FlowRelayIntegrationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Flow integration 01 request and commit are split", RequestAndCommitAreSplit),
        ("Flow integration 02 request does not assign CurrentPage", RequestDoesNotAssignCurrentPage),
        ("Flow integration 03 commit owns CurrentPage", CommitOwnsCurrentPage),
        ("Flow integration 04 commit preserves activation order", CommitPreservesActivationOrder),
        ("Flow integration 05 LastSelectedPage is committed once", LastSelectedPageIsCommittedOnce),
        ("Flow integration 06 App creates one navigation service", AppCreatesOneNavigationService),
        ("Flow integration 07 service does not know business services", ServiceDoesNotKnowBusinessServices),
        ("Flow integration 08 Classic and Off bypass Flow Relay", ClassicAndOffBypassFlowRelay),
        ("Flow integration 09 theme transition has priority", ThemeTransitionHasPriority),
        ("Flow integration 10 theme and navigation services are isolated", ThemeAndNavigationServicesAreIsolated),
        ("Flow integration 11 report route preserves nested lifecycle", ReportRoutePreservesNestedLifecycle),
        ("Flow integration 12 navigation uses existing page keys", NavigationUsesExistingPageKeys)
    ];

    private static string Root => FindRoot();
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string Main => Read("HardwareVision", "ViewModels", "MainViewModel.cs");

    private static void RequestAndCommitAreSplit()
    {
        TestSupport.True(Main.Contains("private void RequestNavigation", StringComparison.Ordinal), "RequestNavigation");
        TestSupport.True(Main.Contains("private void CommitNavigation", StringComparison.Ordinal), "CommitNavigation");
    }

    private static void RequestDoesNotAssignCurrentPage()
    {
        string request = Slice(Main, "private void RequestNavigation", "private void CommitNavigation");
        TestSupport.False(request.Contains("CurrentPage =", StringComparison.Ordinal), "request CurrentPage");
        TestSupport.False(request.Contains("SetPageActive(", StringComparison.Ordinal), "request SetPageActive");
        TestSupport.False(request.Contains("LastSelectedPage =", StringComparison.Ordinal), "request LastSelectedPage");
    }

    private static void CommitOwnsCurrentPage()
    {
        string commit = Slice(Main, "private void CommitNavigation", "private void OnThemeChanged");
        TestSupport.Equal(1, Regex.Matches(commit, "CurrentPage\\s*=").Count, "CurrentPage commit assignment");
    }

    private static void CommitPreservesActivationOrder()
    {
        string commit = Slice(Main, "private void CommitNavigation", "private void OnThemeChanged");
        int stop = commit.IndexOf("SetPageActive(previousPage, false)", StringComparison.Ordinal);
        int current = commit.IndexOf("CurrentPage = page", StringComparison.Ordinal);
        int start = commit.IndexOf("SetPageActive(page, true)", StringComparison.Ordinal);
        TestSupport.True(stop >= 0 && current > stop && start > current, "activation order");
    }

    private static void LastSelectedPageIsCommittedOnce()
    {
        string commit = Slice(Main, "private void CommitNavigation", "private void OnThemeChanged");
        TestSupport.Equal(1, Regex.Matches(commit, "settings.LastSelectedPage\\s*=").Count, "settings assignment");
        TestSupport.Equal(1, Regex.Matches(commit, "updated.LastSelectedPage\\s*=").Count, "settings persistence");
    }

    private static void AppCreatesOneNavigationService()
    {
        string app = Read("HardwareVision", "App.xaml.cs");
        TestSupport.Equal(1, Regex.Matches(app, "NavigationTransitionService\\s*=\\s*new NavigationTransitionService\\(").Count, "formal instance");
        TestSupport.True(app.Contains("NavigationTransitionService as IDisposable", StringComparison.Ordinal), "service disposed");
    }

    private static void ServiceDoesNotKnowBusinessServices()
    {
        string service = Read("HardwareVision", "Services", "NavigationTransitionService.cs");
        foreach (string forbidden in new[] { "ThemeService", "MotionService", "PollingService", "PresentMon", "GameSession", "MainWindow", "CurrentPage", "SetPageActive", "DispatcherTimer" })
            TestSupport.False(service.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    private static void ClassicAndOffBypassFlowRelay()
    {
        string method = Slice(Main, "private bool ShouldUseFlowRelay", "private static bool TryGetRoute");
        TestSupport.True(method.Contains("currentTheme == AppTheme.Tracework", StringComparison.Ordinal), "Classic bypass");
        TestSupport.True(method.Contains("EffectiveLevel != MotionLevel.Off", StringComparison.Ordinal), "Off bypass");
    }

    private static void ThemeTransitionHasPriority()
    {
        string method = Slice(Main, "private bool ShouldUseFlowRelay", "private static bool TryGetRoute");
        TestSupport.True(method.Contains("!themeTransitionService.Current.IsActive", StringComparison.Ordinal), "theme priority gate");
        TestSupport.True(Main.Contains("PrepareForThemeTransitionAsync", StringComparison.Ordinal), "theme takeover callback");
    }

    private static void ThemeAndNavigationServicesAreIsolated()
    {
        string theme = Read("HardwareVision", "Services", "ThemeTransitionService.cs");
        string navigation = Read("HardwareVision", "Services", "NavigationTransitionService.cs");
        TestSupport.False(theme.Contains("NavigationTransitionService", StringComparison.Ordinal), "theme calls navigation");
        TestSupport.False(navigation.Contains("ThemeTransitionService", StringComparison.Ordinal), "navigation calls theme");
    }

    private static void ReportRoutePreservesNestedLifecycle()
    {
        string game = Read("HardwareVision", "ViewModels", "GamePerformanceViewModel.cs");
        TestSupport.True(game.Contains("reportNavigationCoordinator(true, CommitAsync)", StringComparison.Ordinal), "report open coordinator");
        TestSupport.True(game.Contains("reportNavigationCoordinator(false, CommitAsync)", StringComparison.Ordinal), "report close coordinator");
        TestSupport.True(game.Contains("await detail.LoadAsync()", StringComparison.Ordinal), "report load remains");
        TestSupport.True(game.Contains("detail.Dispose()", StringComparison.Ordinal), "report dispose remains");
    }

    private static void NavigationUsesExistingPageKeys()
    {
        string routes = Read("HardwareVision", "Models", "NavigationRouteDescriptor.cs");
        TestSupport.False(routes.Contains("enum Page", StringComparison.Ordinal), "second page identity enum");
        TestSupport.True(routes.Contains("string PageKey", StringComparison.Ordinal), "existing key identity");
    }

    private static string Slice(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        int last = source.IndexOf(end, first, StringComparison.Ordinal);
        TestSupport.True(first >= 0 && last > first, $"slice {start}");
        return source[first..last];
    }

    private static string FindRoot()
    {
        foreach (string origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(origin);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HardwareVision", "MainWindow.xaml")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
