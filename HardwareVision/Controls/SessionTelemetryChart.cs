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
        if (series.Points.Count == 0) return;
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            SessionChartPoint first = series.Points[0];
            context.BeginFigure(Point(first, plot, duration, minimum, maximum), false, false);
            for (int index = 1; index < series.Points.Count; index++)
            {
                context.LineTo(Point(series.Points[index], plot, duration, minimum, maximum), true, false);
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, GetSeriesPen(seriesIndex), geometry);
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
        DrawText(drawingContext, "00:00", new Point(plot.Left, plot.Bottom + 7d), 10d);
        DrawText(drawingContext, FormatElapsed(duration / 2d), new Point(plot.Left + plot.Width / 2d - 18d, plot.Bottom + 7d), 10d);
        DrawText(drawingContext, FormatElapsed(duration), new Point(plot.Right - 36d, plot.Bottom + 7d), 10d);
    }

    private void DrawValueAxis(DrawingContext drawingContext, Rect plot, double minimum, double maximum)
    {
        DrawText(drawingContext, maximum.ToString("0.#", CultureInfo.CurrentCulture), new Point(3d, plot.Top - 7d), 10d);
        DrawText(drawingContext, minimum.ToString("0.#", CultureInfo.CurrentCulture), new Point(3d, plot.Bottom - 7d), 10d);
    }

    private void DrawText(DrawingContext drawingContext, string text, Point point, double size)
    {
        FormattedText formatted = new(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
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
        builder.Append(FormatElapsed(elapsed));
        for (int index = 0; index < model.LimitIntervals.Count; index++)
        {
            SessionLimitInterval interval = model.LimitIntervals[index];
            if (elapsed >= interval.StartSeconds && elapsed <= interval.EndSeconds)
            {
                builder.AppendLine().Append(interval.ToolTip);
            }
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
        for (int index = 0; index < model.LimitIntervals.Count; index++)
        {
            SessionLimitInterval interval = model.LimitIntervals[index];
            if (elapsed >= interval.StartSeconds && elapsed <= interval.EndSeconds)
            {
                selected = interval.EventId;
                ToolTip = interval.ToolTip;
                break;
            }
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

    private bool TryResolveRange(SessionChartModel model, out double minimum, out double maximum)
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
        return double.IsFinite(minimum) && double.IsFinite(maximum);
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

    private static string FormatElapsed(double seconds)
    {
        TimeSpan value = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return value.TotalHours >= 1d ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    }

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
    }

    private static void OnModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        SessionTelemetryChart chart = (SessionTelemetryChart)dependencyObject;
        chart.selectedEventId = null;
        chart.ToolTip = null;
    }
}
