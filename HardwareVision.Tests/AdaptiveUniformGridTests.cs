using System.Windows;
using System.Windows.Controls;
using HardwareVision.Controls;
using Size = System.Windows.Size;

namespace HardwareVision.Tests;

internal static class AdaptiveUniformGridTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Adaptive grid 01 nine items form three by three", NineItemsFormThreeByThree),
        ("Adaptive grid 02 medium width uses two columns", MediumWidthUsesTwoColumns),
        ("Adaptive grid 03 narrow width uses one column", NarrowWidthUsesOneColumn),
        ("Adaptive grid 04 collapsed item occupies no slot", CollapsedItemOccupiesNoSlot),
        ("Adaptive grid 05 hidden middle items compact", HiddenMiddleItemsCompact),
        ("Adaptive grid 06 final row fills from left", FinalRowFillsFromLeft),
        ("Adaptive grid 07 source order is preserved", SourceOrderIsPreserved),
        ("Adaptive grid 08 resize reflows columns", ResizeReflowsColumns),
        ("Adaptive grid 09 configured gaps are exact", ConfiguredGapsAreExact),
        ("Adaptive grid 10 dimensions never become negative", DimensionsNeverBecomeNegative),
        ("Adaptive grid 11 single item lays out", SingleItemLaysOut),
        ("Adaptive grid 12 eighteen items lay out", EighteenItemsLayOut),
        ("Adaptive grid 13 removal leaves no slot", RemovalLeavesNoSlot)
    ];

    private static void NineItemsFormThreeByThree()
    {
        AdaptiveUniformGrid panel = CreatePanel(9);
        Layout(panel, 1080d);
        TestSupport.Equal(1080d, panel.RenderSize.Width, "wide panel layout width");
        AssertGrid(panel, 3, 3);
    }

    private static void MediumWidthUsesTwoColumns()
    {
        AdaptiveUniformGrid panel = CreatePanel(9);
        Layout(panel, 680d);
        TestSupport.Equal(680d, panel.RenderSize.Width, "medium panel layout width");
        AssertGrid(panel, 2, 5);
    }

    private static void NarrowWidthUsesOneColumn()
    {
        AdaptiveUniformGrid panel = CreatePanel(4);
        Layout(panel, 679d);
        TestSupport.Equal(679d, panel.RenderSize.Width, "narrow panel layout width");
        AssertGrid(panel, 1, 4);
    }

    private static void CollapsedItemOccupiesNoSlot()
    {
        AdaptiveUniformGrid panel = CreatePanel(5);
        panel.Children[1].Visibility = Visibility.Collapsed;
        Layout(panel, 1080d);

        Rect third = Bounds(panel, panel.Children[2]);
        TestSupport.Nearly(364d, third.X, "third child compacts into second slot");
        TestSupport.Equal(0d, panel.Children[1].RenderSize.Width, "collapsed width");
    }

    private static void HiddenMiddleItemsCompact()
    {
        AdaptiveUniformGrid panel = CreatePanel(9);
        panel.Children[1].Visibility = Visibility.Collapsed;
        panel.Children[3].Visibility = Visibility.Collapsed;
        panel.Children[5].Visibility = Visibility.Collapsed;
        Layout(panel, 1080d);

        UIElement[] visible = panel.Children.Cast<UIElement>().Where(child => child.Visibility == Visibility.Visible).ToArray();
        for (int index = 0; index < visible.Length; index++)
        {
            Rect bounds = Bounds(panel, visible[index]);
            TestSupport.Nearly((index % 3) * 364d, bounds.X, $"compacted X {index}");
            TestSupport.Nearly((index / 3) * 32d, bounds.Y, $"compacted Y {index}");
        }
    }

    private static void FinalRowFillsFromLeft()
    {
        AdaptiveUniformGrid panel = CreatePanel(5);
        Layout(panel, 1080d);

        TestSupport.Nearly(0d, Bounds(panel, panel.Children[3]).X, "final row first X");
        TestSupport.Nearly(364d, Bounds(panel, panel.Children[4]).X, "final row second X");
    }

    private static void SourceOrderIsPreserved()
    {
        AdaptiveUniformGrid panel = CreatePanel(8);
        Layout(panel, 1080d);

        UIElement[] ordered = panel.Children.Cast<UIElement>()
            .OrderBy(child => Bounds(panel, child).Y)
            .ThenBy(child => Bounds(panel, child).X)
            .ToArray();
        TestSupport.True(panel.Children.Cast<UIElement>().SequenceEqual(ordered), "visual order follows child order");
    }

    private static void ResizeReflowsColumns()
    {
        AdaptiveUniformGrid panel = CreatePanel(6);
        Layout(panel, 1080d);
        TestSupport.Nearly(364d, Bounds(panel, panel.Children[1]).X, "wide second child X");

        Layout(panel, 600d);
        TestSupport.Nearly(0d, Bounds(panel, panel.Children[1]).X, "narrow second child X");
        TestSupport.Nearly(32d, Bounds(panel, panel.Children[1]).Y, "narrow second child Y");
    }

    private static void ConfiguredGapsAreExact()
    {
        AdaptiveUniformGrid panel = CreatePanel(4);
        Layout(panel, 1080d);

        Rect first = Bounds(panel, panel.Children[0]);
        Rect second = Bounds(panel, panel.Children[1]);
        Rect fourth = Bounds(panel, panel.Children[3]);
        TestSupport.Nearly(12d, second.X - first.Right, "horizontal gap");
        TestSupport.Nearly(12d, fourth.Y - first.Bottom, "vertical gap");
    }

    private static void DimensionsNeverBecomeNegative()
    {
        AdaptiveUniformGrid panel = CreatePanel(3);
        Layout(panel, 1d);
        foreach (UIElement child in panel.Children)
        {
            TestSupport.True(child.RenderSize.Width >= 0d && child.RenderSize.Height >= 0d, "nonnegative child size");
        }

        TestSupport.True(panel.DesiredSize.Width >= 0d && panel.DesiredSize.Height >= 0d, "nonnegative panel size");
    }

    private static void SingleItemLaysOut()
    {
        AdaptiveUniformGrid panel = CreatePanel(1);
        Layout(panel, 1080d);
        Rect bounds = Bounds(panel, panel.Children[0]);
        TestSupport.Nearly(0d, bounds.X, "single X");
        TestSupport.Nearly(352d, bounds.Width, "single cell width");
    }

    private static void EighteenItemsLayOut()
    {
        AdaptiveUniformGrid panel = CreatePanel(18);
        Layout(panel, 1080d);
        AssertGrid(panel, 3, 6);
        TestSupport.Nearly(160d, Bounds(panel, panel.Children[17]).Y, "eighteenth item row");
    }

    private static void RemovalLeavesNoSlot()
    {
        AdaptiveUniformGrid panel = CreatePanel(6);
        UIElement removed = panel.Children[1];
        panel.Children.Remove(removed);
        Layout(panel, 1080d);

        TestSupport.Nearly(364d, Bounds(panel, panel.Children[1]).X, "following child moves into removed slot");
        TestSupport.Equal(5, panel.Children.Count, "child count after removal");
    }

    private static AdaptiveUniformGrid CreatePanel(int count)
    {
        AdaptiveUniformGrid panel = new()
        {
            MinItemWidth = 280d,
            HorizontalGap = 12d,
            VerticalGap = 12d,
            MaximumColumns = 3
        };
        for (int index = 0; index < count; index++)
        {
            panel.Children.Add(new Border { Height = 20d, Tag = index });
        }

        return panel;
    }

    private static void Layout(AdaptiveUniformGrid panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0d, 0d, width, panel.DesiredSize.Height));
        panel.UpdateLayout();
    }

    private static void AssertGrid(AdaptiveUniformGrid panel, int columns, int rows)
    {
        double[] x = panel.Children.Cast<UIElement>().Select(child => Bounds(panel, child).X).Distinct().ToArray();
        double[] y = panel.Children.Cast<UIElement>().Select(child => Bounds(panel, child).Y).Distinct().ToArray();
        TestSupport.Equal(columns, x.Length, "column count");
        TestSupport.Equal(rows, y.Length, "row count");
    }

    private static Rect Bounds(AdaptiveUniformGrid panel, UIElement child) =>
        new(child.TranslatePoint(new Point(), panel), child.RenderSize);
}
