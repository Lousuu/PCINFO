using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardwareVision.Controls;
using HardwareVision.ViewModels;
using HardwareVision.Views.Memory;
using Size = System.Windows.Size;

namespace HardwareVision.Tests;

internal static class TraceworkMemoryLayoutTests
{
    private static readonly string[] FieldOrder =
    [
        "插槽位置", "容量", "厂商", "PartNumber", "Speed",
        "ConfiguredClockSpeed", "FormFactor", "MemoryType", "位宽"
    ];

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Memory layout 01 Tracework layout instantiates", TraceworkLayoutInstantiates),
        ("Memory layout 02 nine fields keep required order", NineFieldsKeepRequiredOrder),
        ("Memory layout 03 modules share one item template", ModulesShareOneItemTemplate),
        ("Memory layout 04 collapsed field compacts at runtime", CollapsedFieldCompactsAtRuntime),
        ("Memory layout 05 responsive runtime smoke stays compact", ResponsiveRuntimeSmokeStaysCompact),
        ("Memory layout 06 second module is scroll accessible", SecondModuleIsScrollAccessible),
        ("Memory layout 07 long text does not create horizontal scroll", LongTextDoesNotCreateHorizontalScroll),
        ("Memory layout 08 complete value tooltips are retained", CompleteValueTooltipsAreRetained),
        ("Memory layout 09 bottom safe area is inherited", BottomSafeAreaIsInherited),
        ("Memory layout 10 Classic structure is untouched", ClassicStructureIsUntouched)
    ];

    private static void TraceworkLayoutInstantiates()
    {
        GetApplication();
        TraceworkMemoryLayout layout = new();
        TestSupport.NotNull(layout, "Tracework Memory layout");
    }

    private static void NineFieldsKeepRequiredOrder() => WithLayout(1600d, 900d, (layout, _) =>
    {
        AdaptiveUniformGrid panel = FindVisualDescendants<AdaptiveUniformGrid>(layout).First();
        string[] labels = panel.Children.Cast<FrameworkElement>()
            .Where(child => child.Visibility == Visibility.Visible)
            .Select(child => ((DetailMetricViewModel)child.DataContext).Label)
            .ToArray();
        TestSupport.True(FieldOrder.SequenceEqual(labels), "memory field order");
    });

    private static void ModulesShareOneItemTemplate()
    {
        string xaml = ReadRepositoryFile("HardwareVision", "Views", "Memory", "TraceworkMemoryLayout.xaml");
        TestSupport.Equal(1, Count(xaml, "<ItemsControl.ItemTemplate>"), "module ItemTemplate count");
        TestSupport.True(xaml.Contains("ItemTemplate=\"{StaticResource MemoryModuleMetricCellTemplate}\"", StringComparison.Ordinal), "shared metric template");
        TestSupport.True(xaml.Contains("<controls:AdaptiveUniformGrid", StringComparison.Ordinal), "adaptive panel usage");
    }

    private static void CollapsedFieldCompactsAtRuntime() => WithLayout(1600d, 900d, (layout, _) =>
    {
        AdaptiveUniformGrid panel = FindVisualDescendants<AdaptiveUniformGrid>(layout).First();
        FrameworkElement hidden = panel.Children.Cast<FrameworkElement>().ElementAt(1);
        ((DetailMetricViewModel)hidden.DataContext).IsVisible = false;
        layout.UpdateLayout();

        FrameworkElement third = panel.Children.Cast<FrameworkElement>().ElementAt(2);
        Rect firstBounds = Bounds(panel, panel.Children[0]);
        Rect thirdBounds = Bounds(panel, third);
        TestSupport.Equal(Visibility.Collapsed, hidden.Visibility, "container visibility follows metric");
        TestSupport.Nearly(firstBounds.Right + 12d, thirdBounds.X, "third metric compacts into second slot");
    });

    private static void ResponsiveRuntimeSmokeStaysCompact()
    {
        AssertRuntimeLayout(2048d, 1189d);
        AssertRuntimeLayout(920d, 620d);
        AssertRuntimeLayout(640d, 620d);
    }

    private static void SecondModuleIsScrollAccessible() => WithLayout(920d, 620d, (layout, _) =>
    {
        ScrollViewer scroll = FindVisualDescendants<ScrollViewer>(layout).Single(item => item.Name == "TraceworkMemoryScrollViewer");
        AdaptiveUniformGrid[] panels = FindVisualDescendants<AdaptiveUniformGrid>(layout).ToArray();
        TestSupport.Equal(2, panels.Length, "module panel count");
        TestSupport.True(scroll.ScrollableHeight > 0d, "memory page is vertically scrollable");

        scroll.ScrollToEnd();
        layout.UpdateLayout();
        Rect second = Bounds(scroll, panels[1]);
        TestSupport.True(second.Bottom > 0d && second.Top < scroll.ViewportHeight, "second module reaches viewport");
    });

    private static void LongTextDoesNotCreateHorizontalScroll() => WithLayout(920d, 620d, (layout, _) =>
    {
        ScrollViewer scroll = FindVisualDescendants<ScrollViewer>(layout).Single(item => item.Name == "TraceworkMemoryScrollViewer");
        TestSupport.Equal(0d, scroll.ScrollableWidth, "memory horizontal overflow");
        TestSupport.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility, "horizontal scrollbar");
    }, longValues: true);

    private static void CompleteValueTooltipsAreRetained()
    {
        string xaml = ReadRepositoryFile("HardwareVision", "Views", "Memory", "TraceworkMemoryLayout.xaml");
        TestSupport.True(Count(xaml, "ToolTip=\"{Binding Value}\"") >= 2, "full value tooltip bindings");
        TestSupport.True(xaml.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal), "long values trim");
        TestSupport.True(xaml.Contains("TextWrapping=\"NoWrap\"", StringComparison.Ordinal), "long values do not wrap");
    }

    private static void BottomSafeAreaIsInherited() => WithLayout(920d, 620d, (layout, _) =>
    {
        ScrollViewer scroll = FindVisualDescendants<ScrollViewer>(layout).Single(item => item.Name == "TraceworkMemoryScrollViewer");
        TestSupport.True(scroll.Padding.Right >= 12d, "memory right safe area");
        TestSupport.True(scroll.Padding.Bottom >= 24d, "memory bottom safe area");
    });

    private static void ClassicStructureIsUntouched()
    {
        string xaml = ReadRepositoryFile("HardwareVision", "Views", "Memory", "ClassicMemoryLayout.xaml");
        TestSupport.False(xaml.Contains("AdaptiveUniformGrid", StringComparison.Ordinal), "Classic does not use adaptive panel");
        TestSupport.True(xaml.Contains("ItemsSource=\"{Binding MemoryModules}\"", StringComparison.Ordinal), "Classic module binding remains");
    }

    private static void AssertRuntimeLayout(double width, double height) => WithLayout(width, height, (layout, _) =>
    {
        ScrollViewer scroll = FindVisualDescendants<ScrollViewer>(layout).Single(item => item.Name == "TraceworkMemoryScrollViewer");
        AdaptiveUniformGrid panel = FindVisualDescendants<AdaptiveUniformGrid>(layout).First();
        UIElement[] visible = panel.Children.Cast<UIElement>()
            .Where(child => child.Visibility == Visibility.Visible)
            .ToArray();
        Rect[] bounds = visible.Select(child => Bounds(panel, child)).ToArray();
        double[] columns = bounds.Select(item => Math.Round(item.X, 3)).Distinct().Order().ToArray();
        double[] rows = bounds.Select(item => Math.Round(item.Y, 3)).Distinct().Order().ToArray();

        TestSupport.True(panel.RenderSize.Width > 0d && panel.RenderSize.Height > 0d, $"memory panel is arranged at {width}");
        TestSupport.Equal(0d, scroll.ScrollableWidth, $"memory horizontal overflow at {width}");
        for (int index = 0; index < bounds.Length; index++)
        {
            TestSupport.True(bounds[index].Width > 0d && bounds[index].Height > 0d, $"memory card {index} is arranged at {width}");
            TestSupport.Nearly(columns[index % columns.Length], bounds[index].X, $"memory card {index} compact X at {width}");
            TestSupport.Nearly(rows[index / columns.Length], bounds[index].Y, $"memory card {index} compact Y at {width}");
        }

        for (int first = 0; first < bounds.Length; first++)
        {
            for (int second = first + 1; second < bounds.Length; second++)
            {
                TestSupport.False(bounds[first].IntersectsWith(bounds[second]), $"memory cards {first} and {second} do not overlap at {width}");
            }
        }
    });

    private static void WithLayout(double width, double height, Action<TraceworkMemoryLayout, Window> test, bool longValues = false)
    {
        GetApplication();
        TraceworkMemoryLayout layout = new() { DataContext = CreateData(longValues) };
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
            test(layout, window);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static object CreateData(bool longValues)
    {
        ObservableCollection<MemoryModuleViewModel> modules =
        [
            CreateModule("DIMM_A1", longValues),
            CreateModule("DIMM_B1", longValues)
        ];
        return new { MemoryModules = modules, HasMemoryModules = true };
    }

    private static MemoryModuleViewModel CreateModule(string slot, bool longValues)
    {
        MemoryModuleViewModel module = new() { ModuleName = "Synthetic DDR5", SlotName = slot };
        foreach (string label in FieldOrder)
        {
            string value = longValues
                ? $"{label}-VERY-LONG-SYNTHETIC-VALUE-WITHOUT-BREAKS-0123456789"
                : $"{label}-value";
            module.Metrics.Add(new DetailMetricViewModel { Label = label, Value = value, IsVisible = true });
        }

        return module;
    }

    private static Application GetApplication()
    {
        if (Application.Current is not null) return Application.Current;
        HardwareVision.App app = new();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        return app;
    }

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

    private static int Count(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadRepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));

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
