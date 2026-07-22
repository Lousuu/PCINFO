using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace HardwareVision.Behaviors;

public static class NestedScrollViewerBehavior
{
    public static readonly DependencyProperty BubbleMouseWheelAtBoundaryProperty =
        DependencyProperty.RegisterAttached(
            "BubbleMouseWheelAtBoundary",
            typeof(bool),
            typeof(NestedScrollViewerBehavior),
            new PropertyMetadata(false, OnBubbleMouseWheelAtBoundaryChanged));

    private static bool isForwarding;

    public static bool GetBubbleMouseWheelAtBoundary(DependencyObject element)
    {
        return (bool)element.GetValue(BubbleMouseWheelAtBoundaryProperty);
    }

    public static void SetBubbleMouseWheelAtBoundary(DependencyObject element, bool value)
    {
        element.SetValue(BubbleMouseWheelAtBoundaryProperty, value);
    }

    internal static bool CanScrollInWheelDirection(ScrollViewer scrollViewer, int delta)
    {
        return delta > 0
            ? scrollViewer.VerticalOffset > 0d
            : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
    }

    internal static bool ShouldForwardAtBoundary(double verticalOffset, double scrollableHeight, int delta, bool isComboBoxDropDownOpen)
    {
        if (isComboBoxDropDownOpen)
        {
            return false;
        }

        return delta > 0
            ? verticalOffset <= 0d
            : verticalOffset >= scrollableHeight;
    }

    internal static bool IsOpenComboBoxDropDown(DependencyObject? source)
    {
        System.Windows.Controls.ComboBox? comboBox = FindAncestorOrSelf<System.Windows.Controls.ComboBox>(source);
        return comboBox?.IsDropDownOpen == true
            || FindAncestorOrSelf<Popup>(source)?.PlacementTarget is System.Windows.Controls.ComboBox { IsDropDownOpen: true };
    }

    private static void OnBubbleMouseWheelAtBoundaryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (e.OldValue is true)
        {
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        if (e.NewValue is true)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (isForwarding || e.Handled || sender is not DependencyObject owner)
        {
            return;
        }

        DependencyObject? source = e.OriginalSource as DependencyObject;
        if (IsOpenComboBoxDropDown(source))
        {
            return;
        }

        ScrollViewer? inner = FindNearestInnerScrollViewer(source, owner);
        if (inner is null)
        {
            inner = FindDescendant<ScrollViewer>(owner);
        }

        ScrollViewer? outer = inner is null
            ? FindAncestor<ScrollViewer>(owner)
            : FindAncestor<ScrollViewer>(VisualTreeHelper.GetParent(inner));
        if (outer is null || ReferenceEquals(inner, outer))
        {
            return;
        }

        if (inner is not null
            && !ShouldForwardAtBoundary(inner.VerticalOffset, inner.ScrollableHeight, e.Delta, isComboBoxDropDownOpen: false))
        {
            return;
        }

        e.Handled = true;
        isForwarding = true;
        try
        {
            MouseWheelEventArgs forwarded = new(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            outer.RaiseEvent(forwarded);
        }
        finally
        {
            isForwarding = false;
        }
    }

    private static ScrollViewer? FindNearestInnerScrollViewer(DependencyObject? source, DependencyObject owner)
    {
        DependencyObject? current = source;
        while (current is not null && !ReferenceEquals(current, owner))
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            current = GetParent(current);
        }

        return null;
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

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
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

    private static T? FindDescendant<T>(DependencyObject source) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(source);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(source, index);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
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
}
