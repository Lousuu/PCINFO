using System.Windows;
using System.Windows.Controls;
using HardwareVision.Controls;

namespace HardwareVision.Tests;

internal static class TraceworkResponsiveGridAllocationShapeTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Responsive allocation 01 Placement is value type", PlacementValueType),
        ("Responsive allocation 02 row sum avoids LINQ", NoLinqSum),
        ("Responsive allocation 03 row max avoids LINQ", NoLinqMax),
        ("Responsive allocation 04 no ArrayPool complexity", () => Absent("ArrayPool")),
        ("Responsive allocation 05 no placement cache", () => Absent("placementCache")),
        ("Responsive allocation 06 Wide geometry equivalent", () => AssertGeometry(1600, 12)),
        ("Responsive allocation 07 Standard geometry equivalent", () => AssertGeometry(1100, 8)),
        ("Responsive allocation 08 Compact geometry equivalent", () => AssertGeometry(920, 4)),
        ("Responsive allocation 09 Narrow geometry equivalent", () => AssertGeometry(679, 1)),
        ("Responsive allocation 10 attached properties still invalidate layout", () => Contains("AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange"))
    ];

    private static string Source => TraceworkPilotSource.Read("HardwareVision", "Controls", "TraceworkResponsiveGrid.cs");
    private static void Contains(string value) => TestSupport.True(Source.Contains(value, StringComparison.Ordinal), value);
    private static void Absent(string value) => TestSupport.False(Source.Contains(value, StringComparison.Ordinal), value);
    private static void PlacementValueType() { Contains("private readonly record struct Placement"); Absent("private sealed record Placement"); }
    private static void NoLinqSum() => Absent("rowHeights.Sum()");
    private static void NoLinqMax() => Absent("placements.Max(");

    private static void AssertGeometry(double width, int columns)
    {
        TraceworkResponsiveGrid grid = new() { ColumnGap = 12, RowGap = 16 };
        Border first = new() { Height = 30 };
        Border second = new() { Height = 50 };
        grid.Children.Add(first);
        grid.Children.Add(second);
        SetPlacement(first, width, 0, Math.Min(2, columns), 0);
        SetPlacement(second, width, Math.Min(2, columns - 1), 1, 1);
        grid.Measure(new Size(width, double.PositiveInfinity));
        grid.Arrange(new Rect(0, 0, width, grid.DesiredSize.Height));
        TestSupport.Equal(columns, grid.ColumnCount, "column count");
        TestSupport.True(first.RenderSize.Width > second.RenderSize.Width || columns == 1, "span width");
        TestSupport.True(second.TranslatePoint(new Point(), grid).Y >= first.RenderSize.Height + grid.RowGap - 0.001, "row offset");
    }

    private static void SetPlacement(UIElement child, double width, int column, int span, int row)
    {
        switch (TraceworkResponsiveGrid.ResolveMode(width))
        {
            case TraceworkResponsiveMode.Wide: TraceworkResponsiveGrid.SetWideColumn(child, column); TraceworkResponsiveGrid.SetWideColumnSpan(child, span); TraceworkResponsiveGrid.SetWideRow(child, row); break;
            case TraceworkResponsiveMode.Standard: TraceworkResponsiveGrid.SetStandardColumn(child, column); TraceworkResponsiveGrid.SetStandardColumnSpan(child, span); TraceworkResponsiveGrid.SetStandardRow(child, row); break;
            case TraceworkResponsiveMode.Compact: TraceworkResponsiveGrid.SetCompactColumn(child, column); TraceworkResponsiveGrid.SetCompactColumnSpan(child, span); TraceworkResponsiveGrid.SetCompactRow(child, row); break;
            default: TraceworkResponsiveGrid.SetNarrowColumn(child, 0); TraceworkResponsiveGrid.SetNarrowColumnSpan(child, 1); TraceworkResponsiveGrid.SetNarrowRow(child, row); break;
        }
    }
}
