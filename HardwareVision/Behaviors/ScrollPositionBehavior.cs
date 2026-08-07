using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace HardwareVision.Behaviors;

public static class ScrollPositionBehavior
{
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "VerticalOffset",
            typeof(double),
            typeof(ScrollPositionBehavior),
            new FrameworkPropertyMetadata(
                double.NaN,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnVerticalOffsetChanged));

    private static readonly DependencyProperty IsAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsAttached",
            typeof(bool),
            typeof(ScrollPositionBehavior));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(ScrollPositionBehavior));

    public static double GetVerticalOffset(DependencyObject element) =>
        (double)element.GetValue(VerticalOffsetProperty);

    public static void SetVerticalOffset(DependencyObject element, double value) =>
        element.SetValue(VerticalOffsetProperty, value);

    private static void OnVerticalOffsetChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
        {
            return;
        }

        EnsureAttached(scrollViewer);
        if (!(bool)scrollViewer.GetValue(IsUpdatingProperty)
            && args.NewValue is double offset
            && double.IsFinite(offset)
            && offset >= 0d)
        {
            scrollViewer.ScrollToVerticalOffset(offset);
        }
    }

    private static void EnsureAttached(ScrollViewer scrollViewer)
    {
        if ((bool)scrollViewer.GetValue(IsAttachedProperty))
        {
            return;
        }

        scrollViewer.SetValue(IsAttachedProperty, true);
        scrollViewer.Loaded += OnLoaded;
        scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is ScrollViewer scrollViewer)
        {
            double offset = GetVerticalOffset(scrollViewer);
            if (double.IsFinite(offset) && offset >= 0d)
            {
                scrollViewer.ScrollToVerticalOffset(offset);
            }
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _ = e;
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.SetValue(IsUpdatingProperty, true);
        scrollViewer.SetCurrentValue(VerticalOffsetProperty, scrollViewer.VerticalOffset);
        BindingOperations.GetBindingExpression(scrollViewer, VerticalOffsetProperty)?.UpdateSource();
        scrollViewer.SetValue(IsUpdatingProperty, false);
    }
}
