using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Utilities;

namespace HardwareVision.Controls;

public sealed class CachedPagePresenter : Decorator
{
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(
            nameof(Content),
            typeof(object),
            typeof(CachedPagePresenter),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnContentChanged));

    public static readonly DependencyProperty ContentTemplateProperty =
        DependencyProperty.Register(
            nameof(ContentTemplate),
            typeof(DataTemplate),
            typeof(CachedPagePresenter),
            new FrameworkPropertyMetadata(null, OnPresenterPropertyChanged));

    public static readonly DependencyProperty ContentTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(ContentTemplateSelector),
            typeof(DataTemplateSelector),
            typeof(CachedPagePresenter),
            new FrameworkPropertyMetadata(null, OnPresenterPropertyChanged));

    public static readonly DependencyProperty ContentStringFormatProperty =
        DependencyProperty.Register(
            nameof(ContentStringFormat),
            typeof(string),
            typeof(CachedPagePresenter),
            new FrameworkPropertyMetadata(null, OnPresenterPropertyChanged));

    public static readonly DependencyProperty HorizontalContentAlignmentProperty =
        DependencyProperty.Register(
            nameof(HorizontalContentAlignment),
            typeof(System.Windows.HorizontalAlignment),
            typeof(CachedPagePresenter),
            new FrameworkPropertyMetadata(
                System.Windows.HorizontalAlignment.Left,
                OnPresenterPropertyChanged));

    public static readonly DependencyProperty VerticalContentAlignmentProperty =
        DependencyProperty.Register(
            nameof(VerticalContentAlignment),
            typeof(VerticalAlignment),
            typeof(CachedPagePresenter),
            new FrameworkPropertyMetadata(
                VerticalAlignment.Top,
                OnPresenterPropertyChanged));

    private readonly Dictionary<object, ContentPresenter> presenters =
        new(ReferenceEqualityComparer.Instance);
    private readonly Grid presentationLayer = new();
    private long contentGeneration;
    private long transitionVersion = -1;
    private ContentPresenter? presentedPresenter;
    private bool overlapPending;
    private EventHandler? overlapRenderingHandler;

    public CachedPagePresenter()
    {
        Child = presentationLayer;
    }

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    public DataTemplateSelector? ContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ContentTemplateSelectorProperty);
        set => SetValue(ContentTemplateSelectorProperty, value);
    }

    public string? ContentStringFormat
    {
        get => (string?)GetValue(ContentStringFormatProperty);
        set => SetValue(ContentStringFormatProperty, value);
    }

    public System.Windows.HorizontalAlignment HorizontalContentAlignment
    {
        get => (System.Windows.HorizontalAlignment)GetValue(
            HorizontalContentAlignmentProperty);
        set => SetValue(HorizontalContentAlignmentProperty, value);
    }

    public VerticalAlignment VerticalContentAlignment
    {
        get => (VerticalAlignment)GetValue(VerticalContentAlignmentProperty);
        set => SetValue(VerticalContentAlignmentProperty, value);
    }

    internal int CachedPageCount => presenters.Count;

    internal int PresentedVisualCount => presentationLayer.Children.Count;

    internal object? PresentedContent => presentedPresenter?.Content;

    internal FrameworkElement? PresentedRoot => presentedPresenter;

    internal bool HasPendingOverlap => overlapPending;

    internal event EventHandler? ContentPresented;

    internal event EventHandler? OverlapFramePresented;

    internal void BeginTransition(long version)
    {
        if (version >= transitionVersion)
        {
            transitionVersion = version;
        }
    }

    internal void EndTransition(long version)
    {
        if (version != transitionVersion)
        {
            return;
        }

        transitionVersion = -1;
        CompleteOverlap();
    }

    private static void OnContentChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        CachedPagePresenter host = (CachedPagePresenter)dependencyObject;
        host.Show(args.NewValue);
    }

    private static void OnPresenterPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        _ = args;
        CachedPagePresenter host = (CachedPagePresenter)dependencyObject;
        foreach (ContentPresenter presenter in host.presenters.Values)
        {
            host.ApplyPresenterProperties(presenter);
        }
    }

    private void Show(object? content)
    {
        long generation = ++contentGeneration;
        if (content is null)
        {
            transitionVersion = -1;
            CompleteOverlap();
            presentedPresenter = null;
            presentationLayer.Children.Clear();
            return;
        }

        System.Diagnostics.Stopwatch resolutionClock =
            System.Diagnostics.Stopwatch.StartNew();
        bool cacheHit = presenters.TryGetValue(
            content,
            out ContentPresenter? presenter);
        if (!cacheHit)
        {
            presenter = new ContentPresenter
            {
                Content = content
            };
            ApplyPresenterProperties(presenter);
            presenters.Add(content, presenter);
        }

        ContentPresenter resolvedPresenter = presenter
            ?? throw new InvalidOperationException(
                "The page presenter cache returned a null presenter.");
        if (ReferenceEquals(presentedPresenter, resolvedPresenter))
        {
            return;
        }

        if (presentedPresenter is null && presenters.Count == 1)
        {
            Present(
                content,
                resolvedPresenter,
                cacheHit,
                resolutionClock,
                "Immediate",
                generation);
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                if (generation != contentGeneration
                    || !ReferenceEquals(Content, content))
                {
                    return;
                }

                Present(
                    content,
                    resolvedPresenter,
                    cacheHit,
                    resolutionClock,
                    "Render",
                    generation);
            }));
    }

    private void Present(
        object content,
        ContentPresenter presenter,
        bool cacheHit,
        System.Diagnostics.Stopwatch resolutionClock,
        string dispatcher,
        long generation)
    {
        if (transitionVersion >= 0 && presentedPresenter is not null)
        {
            PresentOverlap(presenter, generation);
        }
        else
        {
            CompleteOverlap();
            presentationLayer.Children.Clear();
            presentationLayer.Children.Add(presenter);
            presentedPresenter = presenter;
        }

        ContentPresented?.Invoke(this, EventArgs.Empty);
        resolutionClock.Stop();
        AppLogger.LogKeyEvent(
            "MotionRuntime | event=PageViewCacheResolved; "
            + $"page={content.GetType().Name}; cacheHit={cacheHit}; "
            + $"entries={presenters.Count}; "
            + $"elapsed={resolutionClock.Elapsed.TotalMilliseconds:0.###}ms; "
            + $"dispatcher={dispatcher}");
    }

    private void PresentOverlap(ContentPresenter incoming, long generation)
    {
        CompleteOverlap();
        ContentPresenter? outgoing = presentedPresenter;
        if (outgoing is null || ReferenceEquals(outgoing, incoming))
        {
            presentationLayer.Children.Clear();
            presentationLayer.Children.Add(incoming);
            presentedPresenter = incoming;
            return;
        }

        if (!presentationLayer.Children.Contains(outgoing))
        {
            presentationLayer.Children.Clear();
            presentationLayer.Children.Add(outgoing);
        }
        presentationLayer.Children.Add(incoming);
        overlapPending = true;
        presentedPresenter = incoming;

        EventHandler? rendering = null;
        rendering = (_, _) =>
        {
            if (generation != contentGeneration
                || !overlapPending)
            {
                DetachOverlapRendering(rendering);
                return;
            }

            if (!incoming.IsLoaded
                || incoming.ActualWidth <= 0d
                || incoming.ActualHeight <= 0d)
            {
                return;
            }

            DetachOverlapRendering(rendering);
            OverlapFramePresented?.Invoke(this, EventArgs.Empty);
            AppLogger.LogKeyEvent(
                "MotionRuntime | event=PageOverlapFrameRendered; "
                + $"outgoing={outgoing.Content?.GetType().Name ?? "null"}; "
                + $"incoming={incoming.Content?.GetType().Name ?? "null"}; "
                + $"generation={generation}");
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    if (generation == contentGeneration
                        && overlapPending)
                    {
                        CompleteOverlap();
                    }
                }));
        };
        overlapRenderingHandler = rendering;
        CompositionTarget.Rendering += rendering;
    }

    private void CompleteOverlap()
    {
        DetachOverlapRendering(overlapRenderingHandler);
        if (!overlapPending)
        {
            return;
        }

        overlapPending = false;
        for (int index = presentationLayer.Children.Count - 1; index >= 0; index--)
        {
            if (!ReferenceEquals(presentationLayer.Children[index], presentedPresenter))
            {
                presentationLayer.Children.RemoveAt(index);
            }
        }
    }

    private void DetachOverlapRendering(EventHandler? rendering)
    {
        if (rendering is null)
        {
            return;
        }

        CompositionTarget.Rendering -= rendering;
        if (ReferenceEquals(overlapRenderingHandler, rendering))
        {
            overlapRenderingHandler = null;
        }
    }

    private void ApplyPresenterProperties(ContentPresenter presenter)
    {
        presenter.ContentTemplate = ContentTemplate;
        presenter.ContentTemplateSelector = ContentTemplateSelector;
        presenter.ContentStringFormat = ContentStringFormat;
        presenter.HorizontalAlignment = HorizontalContentAlignment;
        presenter.VerticalAlignment = VerticalContentAlignment;
    }
}
