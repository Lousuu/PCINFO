using System.Windows;
using System.Windows.Controls;
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
    private long contentGeneration;

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

    internal event EventHandler? ContentPresented;

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
            Child = null;
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
        if (ReferenceEquals(Child, resolvedPresenter))
        {
            return;
        }

        if (Child is null && presenters.Count == 1)
        {
            Present(
                content,
                resolvedPresenter,
                cacheHit,
                resolutionClock,
                "Immediate");
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
                    "Render");
            }));
    }

    private void Present(
        object content,
        ContentPresenter presenter,
        bool cacheHit,
        System.Diagnostics.Stopwatch resolutionClock,
        string dispatcher)
    {
        Child = null;
        Child = presenter;
        ContentPresented?.Invoke(this, EventArgs.Empty);
        resolutionClock.Stop();
        AppLogger.LogKeyEvent(
            "MotionRuntime | event=PageViewCacheResolved; "
            + $"page={content.GetType().Name}; cacheHit={cacheHit}; "
            + $"entries={presenters.Count}; "
            + $"elapsed={resolutionClock.Elapsed.TotalMilliseconds:0.###}ms; "
            + $"dispatcher={dispatcher}");
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
