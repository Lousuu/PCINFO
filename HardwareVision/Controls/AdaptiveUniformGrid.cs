using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;

namespace HardwareVision.Controls;

public sealed class AdaptiveUniformGrid : System.Windows.Controls.Panel
{
    private const double TwoColumnBreakpoint = 680d;
    private const double ThreeColumnBreakpoint = 1080d;

    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth),
        typeof(double),
        typeof(AdaptiveUniformGrid),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsValidNonNegativeDouble);

    public static readonly DependencyProperty HorizontalGapProperty = DependencyProperty.Register(
        nameof(HorizontalGap),
        typeof(double),
        typeof(AdaptiveUniformGrid),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsValidNonNegativeDouble);

    public static readonly DependencyProperty VerticalGapProperty = DependencyProperty.Register(
        nameof(VerticalGap),
        typeof(double),
        typeof(AdaptiveUniformGrid),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        IsValidNonNegativeDouble);

    public static readonly DependencyProperty MaximumColumnsProperty = DependencyProperty.Register(
        nameof(MaximumColumns),
        typeof(int),
        typeof(AdaptiveUniformGrid),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        value => value is int columns && columns > 0);

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double HorizontalGap
    {
        get => (double)GetValue(HorizontalGapProperty);
        set => SetValue(HorizontalGapProperty, value);
    }

    public double VerticalGap
    {
        get => (double)GetValue(VerticalGapProperty);
        set => SetValue(VerticalGapProperty, value);
    }

    public int MaximumColumns
    {
        get => (int)GetValue(MaximumColumnsProperty);
        set => SetValue(MaximumColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        List<UIElement> children = GetVisibleChildren();
        if (children.Count == 0)
        {
            return new Size();
        }

        double availableWidth = NormalizeWidth(availableSize.Width);
        int columns = GetColumnCount(availableWidth);
        double cellWidth = GetCellWidth(availableWidth, columns);
        double desiredWidth = 0d;

        foreach (UIElement child in children)
        {
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
        }

        double desiredHeight = CalculateRowsHeight(children, columns);
        if (!double.IsPositiveInfinity(availableWidth))
        {
            desiredWidth = availableWidth;
        }
        else
        {
            desiredWidth = (Math.Max(MinItemWidth, desiredWidth) * columns)
                + (HorizontalGap * Math.Max(0, columns - 1));
        }

        return new Size(Math.Max(0d, desiredWidth), Math.Max(0d, desiredHeight));
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

        List<UIElement> children = GetVisibleChildren();
        if (children.Count == 0)
        {
            return finalSize;
        }

        double width = NormalizeWidth(finalSize.Width);
        int columns = GetColumnCount(width);
        double cellWidth = GetCellWidth(width, columns);
        double y = 0d;

        for (int rowStart = 0; rowStart < children.Count; rowStart += columns)
        {
            int rowEnd = Math.Min(rowStart + columns, children.Count);
            double rowHeight = 0d;
            for (int index = rowStart; index < rowEnd; index++)
            {
                rowHeight = Math.Max(rowHeight, children[index].DesiredSize.Height);
            }

            for (int index = rowStart; index < rowEnd; index++)
            {
                int column = index - rowStart;
                double x = column * (cellWidth + HorizontalGap);
                children[index].Arrange(new Rect(x, y, cellWidth, rowHeight));
            }

            y += rowHeight;
            if (rowEnd < children.Count)
            {
                y += VerticalGap;
            }
        }

        return finalSize;
    }

    private List<UIElement> GetVisibleChildren()
    {
        List<UIElement> children = new(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                children.Add(child);
            }
        }

        return children;
    }

    private int GetColumnCount(double availableWidth)
    {
        if (double.IsPositiveInfinity(availableWidth))
        {
            return MaximumColumns;
        }

        int breakpointColumns = availableWidth >= ThreeColumnBreakpoint
            ? MaximumColumns
            : availableWidth >= TwoColumnBreakpoint
                ? Math.Min(2, MaximumColumns)
                : 1;

        if (MinItemWidth <= 0d)
        {
            return Math.Max(1, breakpointColumns);
        }

        int widthColumns = Math.Max(
            1,
            (int)Math.Floor((availableWidth + HorizontalGap) / (MinItemWidth + HorizontalGap)));
        return Math.Max(1, Math.Min(breakpointColumns, widthColumns));
    }

    private double GetCellWidth(double availableWidth, int columns)
    {
        if (double.IsPositiveInfinity(availableWidth))
        {
            return Math.Max(0d, MinItemWidth);
        }

        double gaps = HorizontalGap * Math.Max(0, columns - 1);
        return Math.Max(0d, (availableWidth - gaps) / columns);
    }

    private double CalculateRowsHeight(IReadOnlyList<UIElement> children, int columns)
    {
        double height = 0d;
        int rowCount = (children.Count + columns - 1) / columns;
        for (int row = 0; row < rowCount; row++)
        {
            double rowHeight = 0d;
            int rowEnd = Math.Min((row + 1) * columns, children.Count);
            for (int index = row * columns; index < rowEnd; index++)
            {
                rowHeight = Math.Max(rowHeight, children[index].DesiredSize.Height);
            }

            height += rowHeight;
        }

        height += VerticalGap * Math.Max(0, rowCount - 1);
        return height;
    }

    private static double NormalizeWidth(double width) =>
        double.IsPositiveInfinity(width) ? width : Math.Max(0d, width);

    private static bool IsValidNonNegativeDouble(object value) =>
        value is double number && !double.IsNaN(number) && !double.IsInfinity(number) && number >= 0d;
}
