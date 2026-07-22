using System.Windows;
using System.Windows.Controls;

namespace HardwareVision.Tests;

internal static class TraceworkPageSpacingTests
{
    private static readonly string[] ScrollingLayouts =
    [
        "Dashboard/TraceworkDashboardLayout.xaml",
        "Cpu/TraceworkCpuLayout.xaml",
        "Gpu/TraceworkGpuLayout.xaml",
        "Memory/TraceworkMemoryLayout.xaml",
        "Disk/TraceworkDiskLayout.xaml",
        "Network/TraceworkNetworkLayout.xaml",
        "Motherboard/TraceworkMotherboardLayout.xaml",
        "GamePerformance/TraceworkGamePerformanceLayout.xaml",
        "Settings/TraceworkSettingsLayout.xaml",
        "GameSessionReport/TraceworkGameSessionReportLayout.xaml"
    ];

    private static readonly string[] AllTraceworkLayouts =
    [
        .. ScrollingLayouts,
        "AdvancedSensors/TraceworkAdvancedSensorsLayout.xaml",
        "MetricVisibility/TraceworkMetricVisibilityLayout.xaml"
    ];

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Page spacing 01 shared scroll style has safe areas", SharedScrollStyleHasSafeAreas),
        ("Page spacing 02 all long pages use shared scroll style", AllLongPagesUseSharedScrollStyle),
        ("Page spacing 03 all Tracework roots stretch", AllTraceworkRootsStretch),
        ("Page spacing 04 fixed-grid pages use shared content host", FixedGridPagesUseSharedContentHost),
        ("Page spacing 05 page horizontal scrolling is disabled", PageHorizontalScrollingIsDisabled),
        ("Page spacing 06 safe areas hold at minimum width", SafeAreasHoldAtMinimumWidth),
        ("Page spacing 07 safe areas hold at wide width", SafeAreasHoldAtWideWidth),
        ("Page spacing 08 shell keeps one PageHost and binding", ShellKeepsOnePageHostAndBinding),
        ("Page spacing 09 TimeRibbon remains outside page content", TimeRibbonRemainsOutsidePageContent),
        ("SignalRail 01 navigation titles trim without wrapping", NavigationTitlesTrimWithoutWrapping),
        ("SignalRail 02 all items expose full title metadata", AllItemsExposeFullTitleMetadata),
        ("SignalRail 03 advanced sensors keeps full accessible name", AdvancedSensorsKeepsFullAccessibleName),
        ("SignalRail 04 code and title columns do not overlap", CodeAndTitleColumnsDoNotOverlap),
        ("SignalRail 05 keyboard focus border remains", KeyboardFocusBorderRemains)
    ];

    private static void SharedScrollStyleHasSafeAreas()
    {
        Application application = GetApplication();
        ScrollViewer scroll = new() { Style = (Style)application.FindResource("TraceworkPageScrollViewerStyle") };
        TestSupport.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility, "vertical scrollbar");
        TestSupport.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility, "horizontal scrollbar");
        TestSupport.Equal(PanningMode.VerticalOnly, scroll.PanningMode, "panning mode");
        TestSupport.Equal(new Thickness(0d, 0d, 12d, 24d), scroll.Padding, "shared safe padding");
    }

    private static void AllLongPagesUseSharedScrollStyle()
    {
        foreach (string layout in ScrollingLayouts)
        {
            string xaml = ReadLayout(layout);
            TestSupport.True(xaml.Contains("Style=\"{DynamicResource TraceworkPageScrollViewerStyle}\"", StringComparison.Ordinal), $"shared scroll style in {layout}");
            TestSupport.True(xaml.Contains("Style=\"{DynamicResource TraceworkPageContentStackStyle}\"", StringComparison.Ordinal), $"shared content stack in {layout}");
        }
    }

    private static void AllTraceworkRootsStretch()
    {
        foreach (string layout in AllTraceworkLayouts)
        {
            string xaml = ReadLayout(layout);
            TestSupport.True(xaml.Contains("Style=\"{DynamicResource TraceworkPageRootStyle}\"", StringComparison.Ordinal), $"root style in {layout}");
        }

        string pages = ReadFile("HardwareVision", "Themes", "Tracework", "Pages.xaml");
        TestSupport.True(pages.Contains("<Setter Property=\"HorizontalAlignment\" Value=\"Stretch\"", StringComparison.Ordinal), "root horizontal stretch");
        TestSupport.True(pages.Contains("<Setter Property=\"VerticalAlignment\" Value=\"Stretch\"", StringComparison.Ordinal), "root vertical stretch");
        TestSupport.True(pages.Contains("<Setter Property=\"MinWidth\" Value=\"0\"", StringComparison.Ordinal), "root minimum width");
        TestSupport.True(pages.Contains("<Setter Property=\"MinHeight\" Value=\"0\"", StringComparison.Ordinal), "root minimum height");
    }

    private static void FixedGridPagesUseSharedContentHost()
    {
        foreach (string layout in new[] { "AdvancedSensors/TraceworkAdvancedSensorsLayout.xaml", "MetricVisibility/TraceworkMetricVisibilityLayout.xaml" })
        {
            TestSupport.True(ReadLayout(layout).Contains("TraceworkPageContentHostStyle", StringComparison.Ordinal), $"content host in {layout}");
        }
    }

    private static void PageHorizontalScrollingIsDisabled()
    {
        foreach (string layout in AllTraceworkLayouts)
        {
            TestSupport.False(ReadLayout(layout).Contains("HorizontalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal), $"no horizontal auto scroll in {layout}");
        }
    }

    private static void SafeAreasHoldAtMinimumWidth() => AssertScrollStyleAtWidth(920d);

    private static void SafeAreasHoldAtWideWidth() => AssertScrollStyleAtWidth(1600d);

    private static void ShellKeepsOnePageHostAndBinding()
    {
        string shell = ReadFile("HardwareVision", "Views", "Shell", "MainShellHost.xaml");
        TestSupport.Equal(1, Count(shell, "x:Name=\"PageHost\""), "PageHost count");
        TestSupport.Equal(1, Count(shell, "Content=\"{Binding CurrentPage}\""), "CurrentPage binding count");
    }

    private static void TimeRibbonRemainsOutsidePageContent()
    {
        string chrome = ReadFile("HardwareVision", "Views", "Shell", "TraceworkShellChrome.xaml");
        string host = ReadFile("HardwareVision", "Views", "Shell", "MainShellHost.xaml");
        TestSupport.True(chrome.Contains("<shell:TraceworkTimeRibbon Grid.Row=\"2\" Grid.Column=\"1\"", StringComparison.Ordinal), "TimeRibbon shell row");
        TestSupport.True(host.Contains("<Setter Property=\"Margin\" Value=\"120,72,16,46\"", StringComparison.Ordinal), "Tracework PageHost bottom boundary");
    }

    private static void NavigationTitlesTrimWithoutWrapping()
    {
        string shell = ReadFile("HardwareVision", "Themes", "Tracework", "Shell.xaml");
        TestSupport.True(shell.Contains("<Style x:Key=\"TraceworkNavigationTitleTextStyle\"", StringComparison.Ordinal), "navigation title style");
        TestSupport.True(shell.Contains("<Setter Property=\"TextTrimming\" Value=\"CharacterEllipsis\"", StringComparison.Ordinal), "navigation trimming");
        TestSupport.True(shell.Contains("<Setter Property=\"TextWrapping\" Value=\"NoWrap\"", StringComparison.Ordinal), "navigation no wrap");
    }

    private static void AllItemsExposeFullTitleMetadata()
    {
        string rail = ReadFile("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml");
        TestSupport.True(rail.Contains("ToolTip=\"{Binding Title}\"", StringComparison.Ordinal), "all-item title tooltip binding");
        TestSupport.True(rail.Contains("AutomationProperties.Name=\"{Binding Title}\"", StringComparison.Ordinal), "all-item automation binding");
        TestSupport.Equal(1, Count(rail, "<DataTemplate DataType=\"{x:Type viewModels:NavigationItemViewModel}\""), "single navigation template");
    }

    private static void AdvancedSensorsKeepsFullAccessibleName()
    {
        string main = ReadFile("HardwareVision", "ViewModels", "MainViewModel.cs");
        TestSupport.True(main.Contains("\"AdvancedSensors\", \"09\", \"高级传感器\"", StringComparison.Ordinal), "advanced sensors full title");
        string rail = ReadFile("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml");
        TestSupport.True(rail.Contains("ToolTip=\"{Binding Title}\"", StringComparison.Ordinal), "advanced title tooltip source");
        TestSupport.True(rail.Contains("AutomationProperties.Name=\"{Binding Title}\"", StringComparison.Ordinal), "advanced automation source");
    }

    private static void CodeAndTitleColumnsDoNotOverlap()
    {
        string rail = ReadFile("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml");
        TestSupport.True(rail.Contains("<ColumnDefinition Width=\"22\"", StringComparison.Ordinal), "fixed code column");
        TestSupport.True(rail.Contains("<ColumnDefinition Width=\"4\"", StringComparison.Ordinal), "code-title gap");
        TestSupport.True(rail.Contains("<ColumnDefinition Width=\"*\"", StringComparison.Ordinal), "bounded title column");
    }

    private static void KeyboardFocusBorderRemains()
    {
        string shell = ReadFile("HardwareVision", "Themes", "Tracework", "Shell.xaml");
        TestSupport.True(shell.Contains("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\"", StringComparison.Ordinal), "keyboard focus trigger");
        TestSupport.True(shell.Contains("<Setter TargetName=\"Root\" Property=\"BorderBrush\" Value=\"{DynamicResource AccentBrush}\"", StringComparison.Ordinal), "focus border brush");
    }

    private static void AssertScrollStyleAtWidth(double width)
    {
        Application application = GetApplication();
        ScrollViewer scroll = new()
        {
            Width = width,
            Height = 620d,
            Style = (Style)application.FindResource("TraceworkPageScrollViewerStyle"),
            Content = new Border { Width = width * 2d, Height = 900d }
        };
        scroll.Measure(new Size(width, 620d));
        scroll.Arrange(new Rect(0d, 0d, width, 620d));
        scroll.UpdateLayout();
        TestSupport.Equal(0d, scroll.ScrollableWidth, $"horizontal overflow at {width}");
        TestSupport.True(scroll.Padding.Right >= 12d, $"right safe area at {width}");
        TestSupport.True(scroll.Padding.Bottom >= 24d, $"bottom safe area at {width}");
    }

    private static Application GetApplication()
    {
        if (Application.Current is not null) return Application.Current;
        HardwareVision.App app = new();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        return app;
    }

    private static string ReadLayout(string layout) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "HardwareVision", "Views", layout.Replace('/', Path.DirectorySeparatorChar)));

    private static string ReadFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));

    private static int Count(string text, string value) => text.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryRoot()
    {
        foreach (string origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (DirectoryInfo? candidate = new(origin); candidate is not null; candidate = candidate.Parent)
            {
                if (File.Exists(Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml"))) return candidate.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
