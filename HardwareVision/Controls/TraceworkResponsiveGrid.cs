using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;

namespace HardwareVision.Controls;

public enum TraceworkResponsiveMode
{
    Narrow,
    Compact,
    Standard,
    Wide
}

public sealed class TraceworkResponsiveGrid : System.Windows.Controls.Panel
{
    public const double NarrowBreakpoint = 680d;
    public const double StandardBreakpoint = 960d;
    public const double WideBreakpoint = 1360d;

    public static readonly DependencyProperty ColumnGapProperty = DependencyProperty.Register(
        nameof(ColumnGap),
        typeof(double),
        typeof(TraceworkResponsiveGrid),
        LayoutMetadata(12d),
        IsValidGap);

    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(
        nameof(RowGap),
        typeof(double),
        typeof(TraceworkResponsiveGrid),
        LayoutMetadata(16d),
        IsValidGap);

    private static readonly DependencyPropertyKey CurrentModePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentMode),
        typeof(TraceworkResponsiveMode),
        typeof(TraceworkResponsiveGrid),
        new FrameworkPropertyMetadata(TraceworkResponsiveMode.Narrow));

    public static readonly DependencyProperty CurrentModeProperty = CurrentModePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ColumnCountPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ColumnCount),
        typeof(int),
        typeof(TraceworkResponsiveGrid),
        new FrameworkPropertyMetadata(1));

    public static readonly DependencyProperty ColumnCountProperty = ColumnCountPropertyKey.DependencyProperty;

    public static readonly DependencyProperty WideColumnProperty = RegisterPosition("WideColumn", 0);
    public static readonly DependencyProperty WideColumnSpanProperty = RegisterSpan("WideColumnSpan", 12);
    public static readonly DependencyProperty WideRowProperty = RegisterPosition("WideRow", 0);
    public static readonly DependencyProperty StandardColumnProperty = RegisterPosition("StandardColumn", 0);
    public static readonly DependencyProperty StandardColumnSpanProperty = RegisterSpan("StandardColumnSpan", 8);
    public static readonly DependencyProperty StandardRowProperty = RegisterPosition("StandardRow", 0);
    public static readonly DependencyProperty CompactColumnProperty = RegisterPosition("CompactColumn", 0);
    public static readonly DependencyProperty CompactColumnSpanProperty = RegisterSpan("CompactColumnSpan", 4);
    public static readonly DependencyProperty CompactRowProperty = RegisterPosition("CompactRow", 0);
    public static readonly DependencyProperty NarrowColumnProperty = RegisterPosition("NarrowColumn", 0);
    public static readonly DependencyProperty NarrowColumnSpanProperty = RegisterSpan("NarrowColumnSpan", 1);
    public static readonly DependencyProperty NarrowRowProperty = RegisterPosition("NarrowRow", 0);

    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    public TraceworkResponsiveMode CurrentMode => (TraceworkResponsiveMode)GetValue(CurrentModeProperty);

    public int ColumnCount => (int)GetValue(ColumnCountProperty);

    public static void SetWideColumn(DependencyObject element, int value) => element.SetValue(WideColumnProperty, value);
    public static int GetWideColumn(DependencyObject element) => (int)element.GetValue(WideColumnProperty);
    public static void SetWideColumnSpan(DependencyObject element, int value) => element.SetValue(WideColumnSpanProperty, value);
    public static int GetWideColumnSpan(DependencyObject element) => (int)element.GetValue(WideColumnSpanProperty);
    public static void SetWideRow(DependencyObject element, int value) => element.SetValue(WideRowProperty, value);
    public static int GetWideRow(DependencyObject element) => (int)element.GetValue(WideRowProperty);
    public static void SetStandardColumn(DependencyObject element, int value) => element.SetValue(StandardColumnProperty, value);
    public static int GetStandardColumn(DependencyObject element) => (int)element.GetValue(StandardColumnProperty);
    public static void SetStandardColumnSpan(DependencyObject element, int value) => element.SetValue(StandardColumnSpanProperty, value);
    public static int GetStandardColumnSpan(DependencyObject element) => (int)element.GetValue(StandardColumnSpanProperty);
    public static void SetStandardRow(DependencyObject element, int value) => element.SetValue(StandardRowProperty, value);
    public static int GetStandardRow(DependencyObject element) => (int)element.GetValue(StandardRowProperty);
    public static void SetCompactColumn(DependencyObject element, int value) => element.SetValue(CompactColumnProperty, value);
    public static int GetCompactColumn(DependencyObject element) => (int)element.GetValue(CompactColumnProperty);
    public static void SetCompactColumnSpan(DependencyObject element, int value) => element.SetValue(CompactColumnSpanProperty, value);
    public static int GetCompactColumnSpan(DependencyObject element) => (int)element.GetValue(CompactColumnSpanProperty);
    public static void SetCompactRow(DependencyObject element, int value) => element.SetValue(CompactRowProperty, value);
    public static int GetCompactRow(DependencyObject element) => (int)element.GetValue(CompactRowProperty);
    public static void SetNarrowColumn(DependencyObject element, int value) => element.SetValue(NarrowColumnProperty, value);
    public static int GetNarrowColumn(DependencyObject element) => (int)element.GetValue(NarrowColumnProperty);
    public static void SetNarrowColumnSpan(DependencyObject element, int value) => element.SetValue(NarrowColumnSpanProperty, value);
    public static int GetNarrowColumnSpan(DependencyObject element) => (int)element.GetValue(NarrowColumnSpanProperty);
    public static void SetNarrowRow(DependencyObject element, int value) => element.SetValue(NarrowRowProperty, value);
    public static int GetNarrowRow(DependencyObject element) => (int)element.GetValue(NarrowRowProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = NormalizeWidth(availableSize.Width);
        TraceworkResponsiveMode mode = ResolveMode(width);
        int columns = ColumnsFor(mode);
        SetValue(CurrentModePropertyKey, mode);
        SetValue(ColumnCountPropertyKey, columns);

        double measuredWidth = double.IsPositiveInfinity(width) ? WideBreakpoint : width;
        double columnWidth = CalculateColumnWidth(measuredWidth, columns);
        IReadOnlyList<Placement> placements = GetPlacements(mode, columns);
        double[] rowHeights = new double[GetRowCount(placements)];

        foreach (Placement placement in placements)
        {
            double childWidth = WidthForSpan(columnWidth, placement.Span);
            placement.Child.Measure(new Size(childWidth, double.PositiveInfinity));
            rowHeights[placement.Row] = Math.Max(rowHeights[placement.Row], placement.Child.DesiredSize.Height);
        }

        double desiredHeight = rowHeights.Sum() + (RowGap * Math.Max(0, rowHeights.Length - 1));
        return new Size(double.IsPositiveInfinity(width) ? measuredWidth : width, Math.Max(0d, desiredHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect());
            }
        }

        double width = Math.Max(0d, finalSize.Width);
        TraceworkResponsiveMode mode = ResolveMode(width);
        int columns = ColumnsFor(mode);
        SetValue(CurrentModePropertyKey, mode);
        SetValue(ColumnCountPropertyKey, columns);

        IReadOnlyList<Placement> placements = GetPlacements(mode, columns);
        if (placements.Count == 0)
        {
            return finalSize;
        }

        double columnWidth = CalculateColumnWidth(width, columns);
        double[] rowHeights = new double[GetRowCount(placements)];
        foreach (Placement placement in placements)
        {
            rowHeights[placement.Row] = Math.Max(rowHeights[placement.Row], placement.Child.DesiredSize.Height);
        }

        double[] rowOffsets = new double[rowHeights.Length];
        for (int row = 1; row < rowOffsets.Length; row++)
        {
            rowOffsets[row] = rowOffsets[row - 1] + rowHeights[row - 1] + RowGap;
        }

        foreach (Placement placement in placements)
        {
            double x = placement.Column * (columnWidth + ColumnGap);
            double childWidth = WidthForSpan(columnWidth, placement.Span);
            placement.Child.Arrange(new Rect(x, rowOffsets[placement.Row], childWidth, rowHeights[placement.Row]));
        }

        return finalSize;
    }

    public static TraceworkResponsiveMode ResolveMode(double width) => width switch
    {
        >= WideBreakpoint => TraceworkResponsiveMode.Wide,
        >= StandardBreakpoint => TraceworkResponsiveMode.Standard,
        >= NarrowBreakpoint => TraceworkResponsiveMode.Compact,
        _ => TraceworkResponsiveMode.Narrow
    };

    private IReadOnlyList<Placement> GetPlacements(TraceworkResponsiveMode mode, int columns)
    {
        List<Placement> placements = new(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            (int column, int span, int row) = GetRequestedPlacement(child, mode);
            int normalizedColumn = Math.Clamp(column, 0, Math.Max(0, columns - 1));
            int normalizedSpan = Math.Clamp(span, 1, columns - normalizedColumn);
            placements.Add(new Placement(child, normalizedColumn, normalizedSpan, Math.Max(0, row)));
        }

        return placements;
    }

    private static (int Column, int Span, int Row) GetRequestedPlacement(UIElement child, TraceworkResponsiveMode mode) => mode switch
    {
        TraceworkResponsiveMode.Wide => (GetWideColumn(child), GetWideColumnSpan(child), GetWideRow(child)),
        TraceworkResponsiveMode.Standard => (GetStandardColumn(child), GetStandardColumnSpan(child), GetStandardRow(child)),
        TraceworkResponsiveMode.Compact => (GetCompactColumn(child), GetCompactColumnSpan(child), GetCompactRow(child)),
        _ => (GetNarrowColumn(child), GetNarrowColumnSpan(child), GetNarrowRow(child))
    };

    private static int ColumnsFor(TraceworkResponsiveMode mode) => mode switch
    {
        TraceworkResponsiveMode.Wide => 12,
        TraceworkResponsiveMode.Standard => 8,
        TraceworkResponsiveMode.Compact => 4,
        _ => 1
    };

    private double CalculateColumnWidth(double width, int columns) =>
        Math.Max(0d, (width - (ColumnGap * Math.Max(0, columns - 1))) / columns);

    private double WidthForSpan(double columnWidth, int span) =>
        Math.Max(0d, (columnWidth * span) + (ColumnGap * Math.Max(0, span - 1)));

    private static int GetRowCount(IReadOnlyList<Placement> placements) =>
        placements.Count == 0 ? 0 : placements.Max(placement => placement.Row) + 1;

    private static double NormalizeWidth(double width) =>
        double.IsPositiveInfinity(width) ? width : Math.Max(0d, width);

    private static DependencyProperty RegisterPosition(string name, int defaultValue) => DependencyProperty.RegisterAttached(
        name,
        typeof(int),
        typeof(TraceworkResponsiveGrid),
        LayoutMetadata(defaultValue),
        value => value is int number && number >= 0);

    private static DependencyProperty RegisterSpan(string name, int defaultValue) => DependencyProperty.RegisterAttached(
        name,
        typeof(int),
        typeof(TraceworkResponsiveGrid),
        LayoutMetadata(defaultValue),
        value => value is int number && number > 0);

    private static FrameworkPropertyMetadata LayoutMetadata(object defaultValue) => new(
        defaultValue,
        FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange);

    private static bool IsValidGap(object value) =>
        value is double number && !double.IsNaN(number) && !double.IsInfinity(number) && number >= 0d;

    private sealed record Placement(UIElement Child, int Column, int Span, int Row);
}
