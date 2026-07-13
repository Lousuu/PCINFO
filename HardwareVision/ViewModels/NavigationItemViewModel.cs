using CommunityToolkit.Mvvm.ComponentModel;

namespace HardwareVision.ViewModels;

public sealed class NavigationItemViewModel : ObservableObject
{
    private readonly object pageLock = new();
    private Func<object>? pageFactory;
    private object? page;
    private bool isEnabled = true;

    public NavigationItemViewModel(string key, string title, string subtitle, object page)
        : this(key, title, subtitle, () => page)
    {
    }

    public NavigationItemViewModel(string key, string title, string subtitle, Func<object> pageFactory)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        this.pageFactory = pageFactory;
    }

    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public object Page
    {
        get
        {
            if (page is not null)
            {
                return page;
            }

            lock (pageLock)
            {
                page ??= pageFactory?.Invoke()
                    ?? throw new InvalidOperationException($"Page factory for {Key} is unavailable.");
                pageFactory = null;
                return page;
            }
        }
    }

    public bool IsPageCreated => page is not null;

    public object? CreatedPage => page;

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }
}
