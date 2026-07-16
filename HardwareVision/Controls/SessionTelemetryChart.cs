using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HardwareVision.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace HardwareVision.Controls;

public sealed class SessionTelemetryChart : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model),
        typeof(SessionChartModel),
        typeof(SessionTelemetryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnModelChanged));

    public static readonly DependencyProperty PrimaryLineBrushProperty = RegisterBrush(nameof(PrimaryLineBrush), Brushes.DeepSkyBlue);
    public static readonly DependencyProperty SecondaryLineBrushProperty = RegisterBrush(nameof(SecondaryLineBrush), Brushes.LimeGreen);
    public static readonly DependencyProperty TertiaryLineBrushProperty = RegisterBrush(nameof(TertiaryLineBrush), Brushes.Gold);
    public static readonly DependencyProperty GridLineBrushProperty = RegisterBrush(nameof(GridLineBrush), Brushes.DimGray);
    public static readonly DependencyProperty TextBrushProperty = RegisterBrush(nameof(TextBrush), Brushes.Gray);
    public static readonly DependencyProperty LimitBrushProperty = RegisterBrush(nameof(LimitBrush), Brushes.OrangeRed);

    private Pen? gridPen;
    private Pen?[] seriesPens = new Pen?[3];
    private Brush? limitFill;
    private Pen? selectedLimitPen;
    private Rect lastPlotArea;
    private long? selectedEventId;
    private SessionChartModel? geometryModel;
    private Size geometrySize;
    private StreamGeometry?[] geometryCache = [];
    private readonly Dictionary<(string Text, double Size), FormattedText> textCache = [];
    private int geometryBuildCount;

    internal int GeometryBuildCount => geometryBuildCount;

    internal void BuildGeometryForDiagnostics(SessionChartModel model, Size plotSize)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!TryResolveRange(model, out double minimum, out double maximum)) return;
        if (maximum <= minimum)
        {
            minimum -= 1d;
            maximum += 1d;
        }
        EnsureGeometryCache(
            model,
            new Rect(0d, 0d, Math.Max(1d, plotSize.Width), Math.Max(1d, plotSize.Height)),
            ResolveDuration(model),
            minimum,
            maximum);
    }

    internal static int FindIntervalForDiagnostics(
        IReadOnlyList<SessionLimitInterval> intervals,
        double elapsedSeconds) => FindIntervalIndex(intervals, elapsedSeconds);

    internal static SessionChartPoint FindNearestForDiagnostics(
        IReadOnlyList<SessionChartPoint> points,
        double elapsedSeconds) => FindNearest(points, elapsedSeconds);

    internal static IReadOnlyList<string> GetTimeLabelsForDiagnostics(double durationSeconds, double width) =>
        CreateTimeTicks(durationSeconds, width).Select(static tick => tick.Text).ToArray();

    internal static (double Minimum, double Maximum) ResolveRangeForDiagnostics(SessionChartModel model)
    {
        if (!TryResolveRange(model, out double minimum, out double maximum)) return (double.NaN, double.NaN);
        return (minimum, maximum);
    }

    internal static bool ShouldDrawPointMarkersForDiagnostics(int pointCount) =>
        pointCount is > 0 and <= 12;

    public SessionChartModel? Model
    {
        get => (SessionChartModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public Brush PrimaryLineBrush
    {
        get => (Brush)GetValue(PrimaryLineBrushProperty);
        set => SetValue(PrimaryLineBrushProperty, value);
    }

    public Brush SecondaryLineBrush
    {
        get => (Brush)GetValue(SecondaryLineBrushProperty);
        set => SetValue(SecondaryLineBrushProperty, value);
    }

    public Brush TertiaryLineBrush
    {
        get => (Brush)GetValue(TertiaryLineBrushProperty);
        set => SetValue(TertiaryLineBrushProperty, value);
    }

    public Brush GridLineBrush
    {
        get => (Brush)GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
    }

    public Brush TextBrush
    {
        get => (Brush)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public Brush LimitBrush
    {
        get => (Brush)GetValue(LimitBrushProperty);
        set => SetValue(LimitBrushProperty, value);
    }

    public SessionTelemetryChart()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => ToolTip = null;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 720d : availableSize.Width;
        return new Size(width, 270d);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (!IsVisible || ActualWidth <= 0d || ActualHeight <= 0d) return;
        Rect plot = new(48d, 28d, Math.Max(0d, ActualWidth - 62d), Math.Max(0d, ActualHeight - 58d));
        lastPlotArea = plot;
        DrawGrid(drawingContext, plot);

        SessionChartModel? model = Model;
        if (model is null || !model.HasData)
        {
            DrawText(drawingContext, model?.EmptyText ?? "--", new Point(plot.Left + 8d, plot.Top + 8d), 12d);
            return;
        }

        double duration = ResolveDuration(model);
        DrawIntervals(drawingContext, model, plot, duration);
        if (!TryResolveRange(model, out double minimum, out double maximum))
        {
            DrawText(drawingContext, model.EmptyText, new Point(plot.Left + 8d, plot.Top + 8d), 12d);
            DrawTimeAxis(drawingContext, plot, duration);
            return;
        }

        if (maximum <= minimum)
        {
            minimum -= 1d;
            maximum += 1d;
        }
        EnsureGeometryCache(model, plot, duration, minimum, maximum);
        DrawValueAxis(drawingContext, plot, minimum, maximum);
        DrawTimeAxis(drawingContext, plot, duration);
        for (int seriesIndex = 0; seriesIndex < model.Series.Count && seriesIndex < 3; seriesIndex++)
        {
            DrawSeries(drawingContext, model.Series[seriesIndex], seriesIndex, plot, duration, minimum, maximum);
            DrawLegend(drawingContext, model.Series[seriesIndex], seriesIndex, plot);
        }
    }

    private void DrawGrid(DrawingContext drawingContext, Rect plot)
    {
        Pen pen = gridPen ??= CreatePen(GridLineBrush, 1d);
        for (int index = 0; index <= 4; index++)
        {
            double x = plot.Left + plot.Width * index / 4d;
            double y = plot.Top + plot.Height * index / 4d;
            drawingContext.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            drawingContext.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
    }

    private void DrawIntervals(DrawingContext drawingContext, SessionChartModel model, Rect plot, double duration)
    {
        Brush fill = limitFill ??= CreateOpacityBrush(LimitBrush, 0.18d);
        for (int index = 0; index < model.LimitIntervals.Count; index++)
        {
            SessionLimitInterval interval = model.LimitIntervals[index];
            double left = X(interval.StartSeconds, plot, duration);
            double right = X(interval.EndSeconds, plot, duration);
            Pen? border = selectedEventId == interval.EventId
                ? selectedLimitPen ??= CreatePen(LimitBrush, 1.5d)
                : null;
            drawingContext.DrawRectangle(fill, border, new Rect(left, plot.Top, Math.Max(2d, right - left), plot.Height));
        }
    }

    private void DrawSeries(
        DrawingContext drawingContext,
        SessionChartSeries series,
        int seriesIndex,
        Rect plot,
        double duration,
        double minimum,
        double maximum)
    {
        if (series.Points.Count == 0 || seriesIndex >= geometryCache.Length) return;
        drawingContext.DrawGeometry(null, GetSeriesPen(seriesIndex), geometryCache[seriesIndex]);
        if (ShouldDrawPointMarkersForDiagnostics(series.Points.Count))
        {
            Brush brush = GetSeriesPen(seriesIndex).Brush;
            for (int index = 0; index < series.Points.Count; index++)
            {
                Point marker = Point(series.Points[index], plot, duration, minimum, maximum);
                drawingContext.DrawEllipse(brush, null, marker, 2.8d, 2.8d);
            }
        }
    }

    private void DrawLegend(DrawingContext drawingContext, SessionChartSeries series, int index, Rect plot)
    {
        double x = plot.Left + index * 190d;
        Pen pen = GetSeriesPen(index);
        drawingContext.DrawLine(pen, new Point(x, 14d), new Point(x + 18d, 14d));
        DrawText(drawingContext, series.Name, new Point(x + 24d, 5d), 11d);
    }

    private void DrawTimeAxis(DrawingContext drawingContext, Rect plot, double duration)
    {
        IReadOnlyList<TimeTick> ticks = CreateTimeTicks(duration, plot.Width);
        for (int index = 0; index < ticks.Count; index++)
        {
            TimeTick tick = ticks[index];
            double x = plot.Left + plot.Width * tick.Ratio;
            double estimatedWidth = tick.Text.Length * 5.8d;
            DrawText(
                drawingContext,
                tick.Text,
                new Point(Math.Clamp(x - estimatedWidth / 2d, plot.Left, plot.Right - estimatedWidth), plot.Bottom + 7d),
                10d);
        }
    }

    private void DrawValueAxis(DrawingContext drawingContext, Rect plot, double minimum, double maximum)
    {
        DrawText(drawingContext, maximum.ToString("0.#", CultureInfo.CurrentCulture), new Point(3d, plot.Top - 7d), 10d);
        DrawText(drawingContext, minimum.ToString("0.#", CultureInfo.CurrentCulture), new Point(3d, plot.Bottom - 7d), 10d);
    }

    private void DrawText(DrawingContext drawingContext, string text, Point point, double size)
    {
        if (!textCache.TryGetValue((text, size), out FormattedText? formatted))
        {
            formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                TextBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (textCache.Count < 32) textCache[(text, size)] = formatted;
        }
        drawingContext.DrawText(formatted, point);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        SessionChartModel? model = Model;
        Point position = e.GetPosition(this);
        if (model is null || !lastPlotArea.Contains(position))
        {
            ToolTip = null;
            return;
        }

        double duration = ResolveDuration(model);
        double elapsed = Math.Clamp((position.X - lastPlotArea.Left) / Math.Max(1d, lastPlotArea.Width), 0d, 1d) * duration;
        StringBuilder builder = new();
        builder.Append(FormatElapsed(elapsed, duration));
        int intervalIndex = FindIntervalIndex(model.LimitIntervals, elapsed);
        if (intervalIndex >= 0)
        {
            builder.AppendLine().Append(model.LimitIntervals[intervalIndex].ToolTip);
        }
        for (int index = 0; index < model.Series.Count; index++)
        {
            SessionChartSeries series = model.Series[index];
            if (series.Points.Count == 0) continue;
            SessionChartPoint nearest = FindNearest(series.Points, elapsed);
            builder.AppendLine().Append(series.Name).Append("：")
                .Append(nearest.Value.ToString("0.##", CultureInfo.CurrentCulture)).Append(' ').Append(series.Unit);
        }
        ToolTip = builder.ToString();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SessionChartModel? model = Model;
        Point position = e.GetPosition(this);
        if (model is null || !lastPlotArea.Contains(position)) return;
        double elapsed = Math.Clamp(
            (position.X - lastPlotArea.Left) / Math.Max(1d, lastPlotArea.Width),
            0d,
            1d) * ResolveDuration(model);
        long? selected = null;
        int intervalIndex = FindIntervalIndex(model.LimitIntervals, elapsed);
        if (intervalIndex >= 0)
        {
            SessionLimitInterval interval = model.LimitIntervals[intervalIndex];
            selected = interval.EventId;
            ToolTip = interval.ToolTip;
        }
        selectedEventId = selected;
        InvalidateVisual();
    }

    private static SessionChartPoint FindNearest(IReadOnlyList<SessionChartPoint> points, double elapsed)
    {
        int low = 0;
        int high = points.Count - 1;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (points[middle].ElapsedSeconds < elapsed) low = middle + 1;
            else high = middle;
        }
        if (low > 0 && Math.Abs(points[low - 1].ElapsedSeconds - elapsed) < Math.Abs(points[low].ElapsedSeconds - elapsed)) return points[low - 1];
        return points[low];
    }

    private void EnsureGeometryCache(
        SessionChartModel model,
        Rect plot,
        double duration,
        double minimum,
        double maximum)
    {
        Size size = new(plot.Width, plot.Height);
        if (ReferenceEquals(geometryModel, model) && geometrySize.Equals(size)) return;
        geometryModel = model;
        geometrySize = size;
        geometryCache = new StreamGeometry?[Math.Min(3, model.Series.Count)];
        for (int seriesIndex = 0; seriesIndex < geometryCache.Length; seriesIndex++)
        {
            IReadOnlyList<SessionChartPoint> points = model.Series[seriesIndex].Points;
            if (points.Count == 0) continue;
            StreamGeometry geometry = new();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(Point(points[0], plot, duration, minimum, maximum), false, false);
                for (int index = 1; index < points.Count; index++)
                {
                    Point point = Point(points[index], plot, duration, minimum, maximum);
                    if (points[index].BreakBefore) context.BeginFigure(point, false, false);
                    else context.LineTo(point, true, false);
                }
            }
            geometry.Freeze();
            geometryCache[seriesIndex] = geometry;
            geometryBuildCount++;
        }
        textCache.Clear();
    }

    private static int FindIntervalIndex(IReadOnlyList<SessionLimitInterval> intervals, double elapsed)
    {
        int low = 0;
        int high = intervals.Count - 1;
        int candidate = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (intervals[middle].StartSeconds <= elapsed)
            {
                candidate = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        for (int index = candidate; index >= 0 && index >= candidate - 8; index--)
        {
            if (elapsed <= intervals[index].EndSeconds) return index;
        }
        return -1;
    }

    private static bool TryResolveRange(SessionChartModel model, out double minimum, out double maximum)
    {
        minimum = double.PositiveInfinity;
        maximum = double.NegativeInfinity;
        for (int seriesIndex = 0; seriesIndex < model.Series.Count; seriesIndex++)
        {
            IReadOnlyList<SessionChartPoint> points = model.Series[seriesIndex].Points;
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                double value = points[pointIndex].Value;
                if (!double.IsFinite(value)) continue;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum)) return false;
        double padding;
        if (maximum.Equals(minimum))
        {
            padding = ResolveFlatRangePadding(model, maximum);
        }
        else
        {
            padding = Math.Max((maximum - minimum) * 0.08d, ResolveFlatRangePadding(model, maximum) * 0.05d);
        }
        minimum -= padding;
        maximum += padding;
        if (model.IsNonNegative && minimum < 0d) minimum = 0d;
        if (maximum <= minimum) maximum = minimum + Math.Max(0.001d, padding * 2d);
        return true;
    }

    private static double ResolveDuration(SessionChartModel model)
    {
        if (model.DurationSeconds > 0d) return model.DurationSeconds;
        double duration = 1d;
        for (int index = 0; index < model.Series.Count; index++)
        {
            IReadOnlyList<SessionChartPoint> points = model.Series[index].Points;
            if (points.Count > 0) duration = Math.Max(duration, points[^1].ElapsedSeconds);
        }
        return duration;
    }

    private static Point Point(SessionChartPoint point, Rect plot, double duration, double minimum, double maximum) =>
        new(X(point.ElapsedSeconds, plot, duration), plot.Bottom - Math.Clamp((point.Value - minimum) / (maximum - minimum), 0d, 1d) * plot.Height);

    private static double X(double elapsed, Rect plot, double duration) =>
        plot.Left + Math.Clamp(elapsed / Math.Max(0.001d, duration), 0d, 1d) * plot.Width;

    private Pen GetSeriesPen(int index)
    {
        int slot = Math.Clamp(index, 0, 2);
        return seriesPens[slot] ??= CreatePen(slot switch
        {
            0 => PrimaryLineBrush,
            1 => SecondaryLineBrush,
            _ => TertiaryLineBrush
        }, 1.8d);
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        Pen pen = new(brush, thickness);
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    private static Brush CreateOpacityBrush(Brush brush, double opacity)
    {
        Brush result = brush.Clone();
        result.Opacity = opacity;
        if (result.CanFreeze) result.Freeze();
        return result;
    }

    private static string FormatElapsed(double seconds, double totalDuration)
    {
        TimeSpan value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        if (totalDuration < 60d) return value.ToString(@"mm\:ss\.f", CultureInfo.CurrentCulture);
        return value.TotalHours >= 1d || totalDuration >= 3600d
            ? value.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture)
            : value.ToString(@"mm\:ss", CultureInfo.CurrentCulture);
    }

    private static IReadOnlyList<TimeTick> CreateTimeTicks(double duration, double width)
    {
        duration = Math.Max(0d, duration);
        int desiredCount = width switch
        {
            < 520d => 4,
            < 850d => 5,
            _ => 6
        };
        List<TimeTick> ticks = new(desiredCount);
        string? previous = null;
        for (int index = 0; index < desiredCount; index++)
        {
            double ratio = desiredCount == 1 ? 0d : index / (double)(desiredCount - 1);
            string text = FormatElapsed(duration * ratio, duration);
            if (string.Equals(text, previous, StringComparison.Ordinal)) continue;
            ticks.Add(new TimeTick(ratio, text));
            previous = text;
        }
        return ticks;
    }

    private static double ResolveFlatRangePadding(SessionChartModel model, double value)
    {
        string unit = model.Series.FirstOrDefault()?.Unit ?? string.Empty;
        if (unit.Equals("MHz", StringComparison.OrdinalIgnoreCase)) return Math.Max(25d, Math.Abs(value) * 0.05d);
        if (unit.Equals("FPS", StringComparison.OrdinalIgnoreCase)) return Math.Max(1d, Math.Abs(value) * 0.05d);
        if (unit.Equals("ms", StringComparison.OrdinalIgnoreCase)) return Math.Max(0.1d, Math.Abs(value) * 0.05d);
        if (unit.Equals("W", StringComparison.OrdinalIgnoreCase)) return Math.Max(0.5d, Math.Abs(value) * 0.05d);
        if (unit.Equals("℃", StringComparison.OrdinalIgnoreCase)) return Math.Max(1d, Math.Abs(value) * 0.05d);
        return Math.Max(1d, Math.Abs(value) * 0.05d);
    }

    private readonly record struct TimeTick(double Ratio, string Text);

    private static DependencyProperty RegisterBrush(string name, Brush defaultValue) => DependencyProperty.Register(
        name,
        typeof(Brush),
        typeof(SessionTelemetryChart),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender, OnBrushChanged));

    private static void OnBrushChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        SessionTelemetryChart chart = (SessionTelemetryChart)dependencyObject;
        chart.gridPen = null;
        chart.seriesPens = new Pen?[3];
        chart.limitFill = null;
        chart.selectedLimitPen = null;
        chart.geometryModel = null;
        chart.geometryCache = [];
        chart.textCache.Clear();
    }

    private static void OnModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        SessionTelemetryChart chart = (SessionTelemetryChart)dependencyObject;
        chart.selectedEventId = null;
        chart.ToolTip = null;
        chart.geometryModel = null;
        chart.geometryCache = [];
        chart.textCache.Clear();
    }
}
