using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;
using HardwareVision.ViewModels;
using WpfButton = System.Windows.Controls.Button;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfPoint = System.Windows.Point;

namespace HardwareVision.Views.Shell;

public partial class TraceworkSignalRail : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(NavigationTransitionSnapshot),
        typeof(TraceworkSignalRail),
        new PropertyMetadata(NavigationTransitionSnapshot.Idle(), OnSnapshotChanged));

    private long activeVersion = -1;

    public TraceworkSignalRail()
    {
        InitializeComponent();
        SetBinding(SnapshotProperty, new WpfBinding("NavigationTransition"));
        Focusable = false;
        Unloaded += (_, _) => RestoreCursor();
    }

    public NavigationTransitionSnapshot Snapshot
    {
        get => (NavigationTransitionSnapshot)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    private static void OnSnapshotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TraceworkSignalRail rail && e.NewValue is NavigationTransitionSnapshot snapshot)
        {
            rail.ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(NavigationTransitionSnapshot snapshot)
    {
        if (!snapshot.IsActive || !snapshot.Plan.ShowsSignalRailCursor)
        {
            RestoreCursor();
            return;
        }

        if (snapshot.HasCommitted)
        {
            LockAndHide(snapshot);
            return;
        }

        if (activeVersion == snapshot.Version)
        {
            return;
        }

        activeVersion = snapshot.Version;
        _ = Dispatcher.BeginInvoke(new Action(() => StartCursor(snapshot)));
    }

    private void StartCursor(NavigationTransitionSnapshot snapshot)
    {
        UpdateLayout();
        WpfButton? origin = FindButton(snapshot.OriginPage, requireSelected: true);
        WpfButton? target = FindButton(snapshot.TargetPage, requireSelected: false);
        if (origin is null || target is null)
        {
            RestoreCursor();
            return;
        }

        try
        {
            double start = CenterY(origin);
            double end = CenterY(target);
            RouteCursor.Visibility = Visibility.Visible;
            RouteCursor.Opacity = 1d;
            RouteCursor.Background = (WpfBrush)FindResource("AccentBrush");
            RouteCursorTranslate.Y = start;
            RouteCursorTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(start, end, new Duration(snapshot.Plan.CommitTime))
                {
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                },
                HandoffBehavior.SnapshotAndReplace);
        }
        catch (InvalidOperationException)
        {
            RestoreCursor();
        }
    }

    private void LockAndHide(NavigationTransitionSnapshot snapshot)
    {
        if (activeVersion != snapshot.Version || RouteCursor.Visibility != Visibility.Visible)
        {
            return;
        }

        WpfButton? target = FindButton(snapshot.TargetPage, requireSelected: true);
        if (target is null)
        {
            RestoreCursor();
            return;
        }

        RouteCursorTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RouteCursorTranslate.Y = CenterY(target);
        RouteCursor.Background = (WpfBrush)FindResource("SuccessBrush");
        DoubleAnimation fade = new(1d, 0d, new Duration(TimeSpan.FromMilliseconds(40)))
        {
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => RestoreCursor();
        RouteCursor.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private WpfButton? FindButton(string pageKey, bool requireSelected)
    {
        return FindVisualDescendants<WpfButton>(NavigationItemsControl).FirstOrDefault(button =>
            button.DataContext is NavigationItemViewModel item
            && item.Key == pageKey
            && (!requireSelected || item.IsSelected));
    }

    private double CenterY(FrameworkElement element)
    {
        WpfPoint origin = element.TransformToAncestor(this).Transform(new WpfPoint(0d, 0d));
        return origin.Y + (element.ActualHeight / 2d) - (RouteCursor.Height / 2d);
    }

    private void RestoreCursor()
    {
        RouteCursor.BeginAnimation(OpacityProperty, null);
        RouteCursorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        RouteCursorTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        RouteCursor.Opacity = 0d;
        RouteCursorTranslate.X = 0d;
        RouteCursorTranslate.Y = 0d;
        RouteCursor.Visibility = Visibility.Collapsed;
        RouteCursor.IsHitTestVisible = false;
        activeVersion = -1;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in FindVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
