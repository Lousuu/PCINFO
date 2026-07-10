using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;
using WindowsSize = System.Windows.Size;

namespace HardwareVision.Controls;

public sealed class RealtimeLineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(IEnumerable),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty HasDataProperty =
        DependencyProperty.Register(
            nameof(HasData),
            typeof(bool),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(
            nameof(LineBrush),
            typeof(MediaBrush),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata(MediaBrushes.LightBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridLineBrushProperty =
        DependencyProperty.Register(
            nameof(GridLineBrush),
            typeof(MediaBrush),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata(MediaBrushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty =
        DependencyProperty.Register(
            nameof(TextBrush),
            typeof(MediaBrush),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata(MediaBrushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(
            nameof(EmptyText),
            typeof(string),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata("No data", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumValueProperty =
        DependencyProperty.Register(
            nameof(MinimumValue),
            typeof(double),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumValueProperty =
        DependencyProperty.Register(
            nameof(MaximumValue),
            typeof(double),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(RealtimeLineChart),
            new FrameworkPropertyMetadata(1.8d, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? subscribedValues;

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public bool HasData
    {
        get => (bool)GetValue(HasDataProperty);
        set => SetValue(HasDataProperty, value);
    }

    public MediaBrush LineBrush
    {
        get => (MediaBrush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public MediaBrush GridLineBrush
    {
        get => (MediaBrush)GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
    }

    public MediaBrush TextBrush
    {
        get => (MediaBrush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public double MinimumValue
    {
        get => (double)GetValue(MinimumValueProperty);
        set => SetValue(MinimumValueProperty, value);
    }

    public double MaximumValue
    {
        get => (double)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public RealtimeLineChart()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    protected override WindowsSize MeasureOverride(WindowsSize availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 260d : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height)
            ? 112d
            : Math.Min(112d, Math.Max(0d, availableSize.Height));
        return new WindowsSize(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        Rect bounds = new(0d, 0d, ActualWidth, ActualHeight);
        if (bounds.Width <= 0d || bounds.Height <= 0d)
        {
            return;
        }

        Rect plotArea = new(
            bounds.Left + 8d,
            bounds.Top + 8d,
            Math.Max(0d, bounds.Width - 16d),
            Math.Max(0d, bounds.Height - 16d));

        DrawGrid(drawingContext, plotArea);

        double[] values = GetFiniteValues();
        if (!HasData || values.Length == 0)
        {
            DrawEmptyText(drawingContext, bounds);
            return;
        }

        (double minimum, double maximum) = ResolveRange(values);
        if (maximum <= minimum)
        {
            minimum -= 1d;
            maximum += 1d;
        }

        if (values.Length == 1)
        {
            DrawSinglePoint(drawingContext, plotArea, values[0], minimum, maximum);
            return;
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(GetPoint(values[0], 0, values.Length, plotArea, minimum, maximum), false, false);

            for (int index = 1; index < values.Length; index++)
            {
                context.LineTo(GetPoint(values[index], index, values.Length, plotArea, minimum, maximum), true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, new MediaPen(LineBrush, StrokeThickness), geometry);
    }

    private static void OnValuesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        RealtimeLineChart chart = (RealtimeLineChart)dependencyObject;

        if (chart.subscribedValues is not null)
        {
            chart.subscribedValues.CollectionChanged -= chart.OnValuesCollectionChanged;
        }

        chart.subscribedValues = e.NewValue as INotifyCollectionChanged;

        if (chart.subscribedValues is not null)
        {
            chart.subscribedValues.CollectionChanged += chart.OnValuesCollectionChanged;
        }

        chart.InvalidateVisual();
    }

    private void OnValuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private double[] GetFiniteValues()
    {
        if (Values is null)
        {
            return [];
        }

        List<double> result = [];
        foreach (object? item in Values)
        {
            if (item is double value && !double.IsNaN(value) && !double.IsInfinity(value))
            {
                result.Add(value);
            }
        }

        return result.ToArray();
    }

    private (double Minimum, double Maximum) ResolveRange(IReadOnlyCollection<double> values)
    {
        double minimum = double.IsNaN(MinimumValue) ? values.Min() : MinimumValue;
        double maximum = double.IsNaN(MaximumValue) ? values.Max() : MaximumValue;
        return (minimum, maximum);
    }

    private static WindowsPoint GetPoint(double value, int index, int count, Rect plotArea, double minimum, double maximum)
    {
        double x = count <= 1
            ? plotArea.Left + plotArea.Width / 2d
            : plotArea.Left + plotArea.Width * index / (count - 1);
        double ratio = (value - minimum) / (maximum - minimum);
        double y = plotArea.Bottom - Math.Clamp(ratio, 0d, 1d) * plotArea.Height;
        return new WindowsPoint(x, y);
    }

    private void DrawGrid(DrawingContext drawingContext, Rect plotArea)
    {
        MediaPen gridPen = new(GridLineBrush, 0.6d);

        for (int index = 0; index <= 3; index++)
        {
            double y = plotArea.Top + plotArea.Height / 3d * index;
            drawingContext.DrawLine(gridPen, new WindowsPoint(plotArea.Left, y), new WindowsPoint(plotArea.Right, y));
        }
    }

    private void DrawEmptyText(DrawingContext drawingContext, Rect bounds)
    {
        FormattedText text = new(
            EmptyText,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI, Microsoft YaHei UI"),
            12d,
            TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        WindowsPoint textPoint = new(
            bounds.Left + Math.Max(0d, (bounds.Width - text.Width) / 2d),
            bounds.Top + Math.Max(0d, (bounds.Height - text.Height) / 2d));
        drawingContext.DrawText(text, textPoint);
    }

    private void DrawSinglePoint(DrawingContext drawingContext, Rect plotArea, double value, double minimum, double maximum)
    {
        WindowsPoint point = GetPoint(value, 0, 1, plotArea, minimum, maximum);
        drawingContext.DrawEllipse(LineBrush, null, point, StrokeThickness + 2d, StrokeThickness + 2d);
    }
}
