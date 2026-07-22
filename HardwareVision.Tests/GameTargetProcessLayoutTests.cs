using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HardwareVision.Controls;
using HardwareVision.Views.GamePerformance;
using Size = System.Windows.Size;

namespace HardwareVision.Tests;

internal static class GameTargetProcessLayoutTests
{
    private static readonly string[] ButtonLabels = ["刷新", "识别游戏", "开始", "停止"];

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Game target 01 labels align with inputs", LabelsAlignWithInputs),
        ("Game target 02 input controls are sixty four pixels", InputControlsAreSixtyFourPixels),
        ("Game target 03 input controls share vertical bounds", InputControlsShareVerticalBounds),
        ("Game target 04 input text is vertically centered", InputTextIsVerticallyCentered),
        ("Game target 05 four buttons use required dimensions", FourButtonsUseRequiredDimensions),
        ("Game target 06 button spacing and final margin are correct", ButtonSpacingAndFinalMarginAreCorrect),
        ("Game target 07 duplicate status text is removed", DuplicateStatusTextIsRemoved),
        ("Game target 08 formal status binding remains", FormalStatusBindingRemains),
        ("Game target 09 commands and data bindings remain", CommandsAndDataBindingsRemain),
        ("Game target 10 long process names retain tooltip", LongProcessNamesRetainTooltip),
        ("Game target 11 minimum layout has no overlap", MinimumLayoutHasNoOverlap),
        ("Game target 12 wide layout remains two columns", WideLayoutRemainsTwoColumns)
    ];

    private static void LabelsAlignWithInputs() => WithLayout(1600d, 900d, layout =>
    {
        TextBlock searchLabel = FindNamed<TextBlock>(layout, "TraceworkSearchTargetLabel");
        TextBlock routeLabel = FindNamed<TextBlock>(layout, "TraceworkProcessRouteLabel");
        TextBox search = FindNamed<TextBox>(layout, "TraceworkProcessSearchBox");
        ComboBox route = FindNamed<ComboBox>(layout, "TraceworkProcessSelector");
        TraceworkPanel panel = TargetPanel(layout);

        TestSupport.Nearly(Bounds(panel, search).X, Bounds(panel, searchLabel).X, "SEARCH left edge");
        TestSupport.Nearly(Bounds(panel, route).X, Bounds(panel, routeLabel).X, "PROCESS ROUTE left edge");
        TestSupport.Nearly(Bounds(panel, searchLabel).Y, Bounds(panel, routeLabel).Y, "label top edge");
        TestSupport.Equal(HorizontalAlignment.Left, routeLabel.HorizontalAlignment, "PROCESS ROUTE horizontal alignment");
        TestSupport.Equal(VerticalAlignment.Bottom, routeLabel.VerticalAlignment, "PROCESS ROUTE vertical alignment");
    });

    private static void InputControlsAreSixtyFourPixels() => WithLayout(1600d, 900d, layout =>
    {
        TextBox search = FindNamed<TextBox>(layout, "TraceworkProcessSearchBox");
        ComboBox route = FindNamed<ComboBox>(layout, "TraceworkProcessSelector");
        TestSupport.Equal(64d, search.ActualHeight, "search height");
        TestSupport.Equal(64d, route.ActualHeight, "route height");
        TestSupport.Equal(64d, search.MinHeight, "search min height");
        TestSupport.Equal(64d, route.MinHeight, "route min height");
    });

    private static void InputControlsShareVerticalBounds() => WithLayout(1600d, 900d, layout =>
    {
        TraceworkPanel panel = TargetPanel(layout);
        Rect search = Bounds(panel, FindNamed<TextBox>(layout, "TraceworkProcessSearchBox"));
        Rect route = Bounds(panel, FindNamed<ComboBox>(layout, "TraceworkProcessSelector"));
        TestSupport.Nearly(search.Top, route.Top, "input top edge");
        TestSupport.Nearly(search.Bottom, route.Bottom, "input bottom edge");
    });

    private static void InputTextIsVerticallyCentered() => WithLayout(1600d, 900d, layout =>
    {
        TextBox search = FindNamed<TextBox>(layout, "TraceworkProcessSearchBox");
        ComboBox route = FindNamed<ComboBox>(layout, "TraceworkProcessSelector");
        TestSupport.Equal(VerticalAlignment.Center, search.VerticalContentAlignment, "search vertical content");
        TestSupport.Equal(VerticalAlignment.Center, route.VerticalContentAlignment, "route vertical content");
        TestSupport.Equal(new Thickness(18d, 0d, 18d, 0d), search.Padding, "search padding");
        TestSupport.Equal(new Thickness(18d, 0d, 18d, 0d), route.Padding, "route padding");
    });

    private static void FourButtonsUseRequiredDimensions() => WithLayout(1600d, 900d, layout =>
    {
        Button[] buttons = TargetButtons(layout);
        TestSupport.Equal(4, buttons.Length, "target button count");
        foreach (Button button in buttons)
        {
            TestSupport.Equal(64d, button.ActualHeight, $"{button.Content} height");
            TestSupport.Equal(172d, button.MinWidth, $"{button.Content} minimum width");
            TestSupport.Equal(VerticalAlignment.Center, button.VerticalContentAlignment, $"{button.Content} vertical content");
            TestSupport.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment, $"{button.Content} horizontal content");
        }
    });

    private static void ButtonSpacingAndFinalMarginAreCorrect() => WithLayout(1600d, 900d, layout =>
    {
        Button[] buttons = TargetButtons(layout);
        for (int index = 0; index < buttons.Length - 1; index++)
        {
            TestSupport.Equal(new Thickness(0d, 0d, 16d, 0d), buttons[index].Margin, $"button margin {index}");
        }

        TestSupport.Equal(new Thickness(), buttons[^1].Margin, "final button margin");
        WrapPanel panel = FindNamed<WrapPanel>(layout, "TraceworkTargetButtonPanel");
        TestSupport.Equal(20d, panel.Margin.Top, "button row top margin");
        TestSupport.Equal(76d, panel.ItemHeight, "button row pitch includes twelve pixel line gap");
    });

    private static void DuplicateStatusTextIsRemoved()
    {
        string xaml = ReadGameXaml();
        string targetSection = xaml[..xaml.IndexOf("GME.15", StringComparison.Ordinal)];
        TestSupport.Equal(1, Count(targetSection, "StatusText"), "TARGET PROCESS status binding count");
        TestSupport.False(targetSection.Contains("DETECTION ACTIVE", StringComparison.Ordinal), "duplicate detection text");
        TestSupport.False(targetSection.Contains("CAPTURE ACTIVE", StringComparison.Ordinal), "duplicate capture text");
    }

    private static void FormalStatusBindingRemains() => WithLayout(1600d, 900d, layout =>
    {
        TraceworkPanel panel = TargetPanel(layout);
        Binding binding = TestSupport.NotNull(BindingOperations.GetBinding(panel, TraceworkPanel.BadgeTextProperty), "status binding");
        TestSupport.Equal("StatusText", binding.Path.Path, "formal status path");
        TestSupport.Equal(80d, panel.BadgeMinWidth, "formal status minimum width");
        TestSupport.Equal(HorizontalAlignment.Right, panel.BadgeHorizontalAlignment, "formal status horizontal alignment");
        TestSupport.Equal(VerticalAlignment.Top, panel.BadgeVerticalAlignment, "formal status vertical alignment");
    });

    private static void CommandsAndDataBindingsRemain()
    {
        string xaml = ReadGameXaml();
        foreach (string command in new[] { "RefreshProcessesCommand", "DetectGameCommand", "StartCaptureCommand", "StopCaptureCommand" })
        {
            TestSupport.True(xaml.Contains($"Command=\"{{Binding {command}}}\"", StringComparison.Ordinal), $"{command} binding");
        }

        TestSupport.True(xaml.Contains("ItemsSource=\"{Binding ProcessOptions}\"", StringComparison.Ordinal), "process options binding");
        TestSupport.True(xaml.Contains("SelectedItem=\"{Binding SelectedProcess", StringComparison.Ordinal), "selected process binding");
        TestSupport.True(xaml.Contains("Text=\"{Binding ProcessSearchText", StringComparison.Ordinal), "process search binding");
    }

    private static void LongProcessNamesRetainTooltip()
    {
        string xaml = ReadGameXaml();
        TestSupport.True(xaml.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal), "process name trimming");
        TestSupport.True(xaml.Contains("ToolTip=\"{Binding DisplayLabel}\"", StringComparison.Ordinal), "complete process name tooltip");
        string resources = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "HardwareVision", "Themes", "Tracework", "GamePages.xaml"));
        TestSupport.True(resources.Contains("<ColumnDefinition Width=\"48\"", StringComparison.Ordinal), "independent arrow width");
    }

    private static void MinimumLayoutHasNoOverlap() => WithLayout(920d, 620d, layout =>
    {
        ScrollViewer scroll = FindVisualDescendants<ScrollViewer>(layout).Single(item => item.Name == "TraceworkGamePerformanceScrollViewer");
        TestSupport.Equal(0d, scroll.ScrollableWidth, "minimum horizontal overflow");
        Button[] buttons = TargetButtons(layout);
        WrapPanel panel = FindNamed<WrapPanel>(layout, "TraceworkTargetButtonPanel");
        Rect[] bounds = buttons.Select(button => Bounds(panel, button)).ToArray();
        for (int left = 0; left < bounds.Length; left++)
        {
            for (int right = left + 1; right < bounds.Length; right++)
            {
                TestSupport.False(bounds[left].IntersectsWith(bounds[right]), $"buttons {left} and {right} overlap");
            }
        }
    });

    private static void WideLayoutRemainsTwoColumns() => WithLayout(1600d, 900d, layout =>
    {
        TraceworkPanel panel = TargetPanel(layout);
        Rect search = Bounds(panel, FindNamed<TextBox>(layout, "TraceworkProcessSearchBox"));
        Rect route = Bounds(panel, FindNamed<ComboBox>(layout, "TraceworkProcessSelector"));
        TestSupport.True(route.X >= search.Right + 27d, "wide input column gap");
        TestSupport.Equal(0d, FindVisualDescendants<ScrollViewer>(layout).Single(item => item.Name == "TraceworkGamePerformanceScrollViewer").ScrollableWidth, "wide horizontal overflow");
    });

    private static void WithLayout(double width, double height, Action<TraceworkGamePerformanceLayout> test)
    {
        GetApplication();
        TraceworkGamePerformanceLayout layout = new();
        Window window = new()
        {
            Content = layout,
            Width = width,
            Height = height,
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
            window.ApplyTemplate();
            layout.ApplyTemplate();
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0d, 0d, width, height));
            window.UpdateLayout();
            layout.UpdateLayout();
            test(layout);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static TraceworkPanel TargetPanel(TraceworkGamePerformanceLayout layout) =>
        FindVisualDescendants<TraceworkPanel>(layout).Single(panel => panel.Title == "TARGET PROCESS");

    private static Button[] TargetButtons(TraceworkGamePerformanceLayout layout)
    {
        WrapPanel panel = FindNamed<WrapPanel>(layout, "TraceworkTargetButtonPanel");
        return panel.Children.Cast<Button>().Where(button => ButtonLabels.Contains(button.Content?.ToString())).ToArray();
    }

    private static T FindNamed<T>(DependencyObject root, string name) where T : FrameworkElement =>
        TestSupport.NotNull(FindVisualDescendants<T>(root).SingleOrDefault(item => item.Name == name), name);

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static Rect Bounds(UIElement ancestor, UIElement child) =>
        new(child.TranslatePoint(new Point(), ancestor), child.RenderSize);

    private static Application GetApplication()
    {
        if (Application.Current is not null) return Application.Current;
        HardwareVision.App app = new();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        return app;
    }

    private static string ReadGameXaml() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "HardwareVision", "Views", "GamePerformance", "TraceworkGamePerformanceLayout.xaml"));

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
