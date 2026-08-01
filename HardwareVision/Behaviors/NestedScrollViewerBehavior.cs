using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Runtime.CompilerServices;

namespace HardwareVision.Behaviors;

public static class NestedScrollViewerBehavior
{
    private const double BoundaryThreshold = 0.1d;

    public static readonly DependencyProperty BubbleMouseWheelAtBoundaryProperty =
        DependencyProperty.RegisterAttached(
            "BubbleMouseWheelAtBoundary",
            typeof(bool),
            typeof(NestedScrollViewerBehavior),
            new PropertyMetadata(false, OnForwardingPropertyChanged));

    public static readonly DependencyProperty ForwardAtBoundaryProperty =
        DependencyProperty.RegisterAttached(
            "ForwardAtBoundary",
            typeof(bool),
            typeof(NestedScrollViewerBehavior),
            new PropertyMetadata(false, OnForwardingPropertyChanged));

    private static readonly ConditionalWeakTable<DependencyObject, WheelAccumulator>
        WheelAccumulators = new();

    public static bool GetBubbleMouseWheelAtBoundary(DependencyObject element)
    {
        return (bool)element.GetValue(BubbleMouseWheelAtBoundaryProperty);
    }

    public static void SetBubbleMouseWheelAtBoundary(DependencyObject element, bool value)
    {
        element.SetValue(BubbleMouseWheelAtBoundaryProperty, value);
    }

    public static bool GetForwardAtBoundary(DependencyObject element)
    {
        return (bool)element.GetValue(ForwardAtBoundaryProperty);
    }

    public static void SetForwardAtBoundary(DependencyObject element, bool value)
    {
        element.SetValue(ForwardAtBoundaryProperty, value);
    }

    internal static bool CanScrollInWheelDirection(ScrollViewer scrollViewer, int delta)
    {
        return delta > 0
            ? scrollViewer.VerticalOffset > BoundaryThreshold
            : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - BoundaryThreshold;
    }

    internal static bool ShouldForwardAtBoundary(double verticalOffset, double scrollableHeight, int delta, bool isComboBoxDropDownOpen)
    {
        if (isComboBoxDropDownOpen)
        {
            return false;
        }

        return delta > 0
            ? verticalOffset <= BoundaryThreshold
            : verticalOffset >= scrollableHeight - BoundaryThreshold;
    }

    internal static bool IsOpenComboBoxDropDown(DependencyObject? source)
    {
        System.Windows.Controls.ComboBox? comboBox = FindAncestorOrSelf<System.Windows.Controls.ComboBox>(source);
        return comboBox?.IsDropDownOpen == true
            || FindAncestorOrSelf<Popup>(source)?.PlacementTarget is System.Windows.Controls.ComboBox { IsDropDownOpen: true };
    }

    internal static int WheelStepCount(int delta) =>
        Math.Abs(delta) / Mouse.MouseWheelDeltaForOneLine;

    internal static int AccumulateWheelDeltaForDiagnostics(
        int accumulatedDelta,
        int delta,
        out int remainder)
    {
        int total = accumulatedDelta + delta;
        int notches = total / Mouse.MouseWheelDeltaForOneLine;
        remainder = total % Mouse.MouseWheelDeltaForOneLine;
        return notches;
    }

    private static void OnForwardingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if (GetBubbleMouseWheelAtBoundary(element) || GetForwardAtBoundary(element))
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled
            || e.Delta == 0
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || Mouse.LeftButton == MouseButtonState.Pressed
            || sender is not DependencyObject owner)
        {
            return;
        }

        DependencyObject? source = e.OriginalSource as DependencyObject;
        if (IsOpenComboBoxDropDown(source))
        {
            return;
        }

        if (TryForwardWheel(source, owner, e.Delta))
        {
            e.Handled = true;
        }
    }

    internal static bool TryForwardWheel(
        DependencyObject? source,
        DependencyObject owner,
        int delta)
    {
        List<ScrollViewer> chain = BuildScrollChain(source, owner);
        if (chain.Count < 2)
        {
            return false;
        }

        if (CanScrollInWheelDirection(chain[0], delta))
        {
            return false;
        }

        WheelAccumulator accumulator = WheelAccumulators.GetOrCreateValue(owner);
        int notches = AccumulateWheelDeltaForDiagnostics(
            accumulator.Remainder,
            delta,
            out int remainder);
        accumulator.Remainder = remainder;
        if (notches == 0)
        {
            return true;
        }

        int direction = Math.Sign(notches);
        int wheelLines = SystemParameters.WheelScrollLines;
        int commandCount = Math.Abs(notches) * Math.Max(1, wheelLines);
        for (int chainIndex = 1; chainIndex < chain.Count; chainIndex++)
        {
            ScrollViewer candidate = chain[chainIndex];
            if (!CanScrollInWheelDirection(candidate, direction))
            {
                continue;
            }

            double before = candidate.VerticalOffset;
            if (wheelLines < 0)
            {
                if (direction > 0)
                {
                    candidate.PageUp();
                }
                else
                {
                    candidate.PageDown();
                }
            }
            else
            {
                for (int command = 0; command < commandCount; command++)
                {
                    if (direction > 0)
                    {
                        candidate.LineUp();
                    }
                    else
                    {
                        candidate.LineDown();
                    }
                }
            }

            if (Math.Abs(candidate.VerticalOffset - before) > BoundaryThreshold)
            {
                return true;
            }
        }

        return true;
    }

    private static List<ScrollViewer> BuildScrollChain(
        DependencyObject? source,
        DependencyObject owner)
    {
        List<ScrollViewer> chain = [];
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer
                && !chain.Contains(scrollViewer))
            {
                chain.Add(scrollViewer);
            }

            bool reachedOwner = ReferenceEquals(current, owner);
            current = GetParent(current);
            if (reachedOwner && current is null)
            {
                break;
            }
        }

        return chain;
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? source) where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject source)
    {
        if (source is FrameworkElement frameworkElement && frameworkElement.Parent is not null)
        {
            return frameworkElement.Parent;
        }

        if (source is FrameworkContentElement contentElement && contentElement.Parent is not null)
        {
            return contentElement.Parent;
        }

        return source is Visual or Visual3D ? VisualTreeHelper.GetParent(source) : null;
    }

    private sealed class WheelAccumulator
    {
        public int Remainder { get; set; }
    }
}
