using System.Windows;
using HardwareVision.Models;
using HardwareVision.Themes;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class ThemeService : IThemeService
{
    private static readonly IReadOnlyDictionary<AppTheme, Uri> ThemeUris =
        new Dictionary<AppTheme, Uri>
        {
            [AppTheme.Classic] = new("/HardwareVision;component/Themes/Colors.xaml", UriKind.Relative),
            [AppTheme.Tracework] = new("/HardwareVision;component/Themes/Tracework/Colors.xaml", UriKind.Relative)
        };

    private static readonly string[] RequiredResourceKeys =
    [
        "AppBackground", "PanelBackground", "CardBackground", "CardBorder", "AccentColor",
        "AccentSoftColor", "TextPrimary", "TextSecondary", "TextMuted", "WarningColor",
        "CriticalColor", "SuccessColor", "PanelBackgroundAlt", "CardBackgroundSoft",
        "DividerColor", "DisabledColor", "AppBackgroundBrush", "AppShellBrush",
        "PanelBackgroundBrush", "PanelBackgroundAltBrush", "CardBackgroundBrush",
        "CardBackgroundSoftBrush", "CardSurfaceBrush", "CardBorderBrush", "DividerBrush",
        "AccentBrush", "AccentSoftBrush", "TextPrimaryBrush", "TextSecondaryBrush",
        "TextMutedBrush", "SuccessBrush", "WarningBrush", "CriticalBrush", "DisabledBrush",
        "NavigationRailBrush", "ContentBackgroundBrush", "HeaderBackgroundBrush",
        "FooterBackgroundBrush", "PanelBrush", "SidePanelBrush", "SubtleSurfaceBrush",
        "AccentSurfaceBrush", "BorderBrush", "SoftBorderBrush", "AccentBorderBrush",
        "PrimaryTextBrush", "MutedTextBrush", "AccentTextBrush", "DisabledTextBrush",
        "AppFontFamily"
    ];

    private static readonly IReadOnlyList<ThemeDescriptor> Descriptors =
    [
        new(
            AppTheme.Classic,
            "经典复古",
            "保留当前暖色复古硬件仪表风格。",
            ["#EEF1E6", "#1E4D63", "#E85D3F", "#FFF8E8"]),
        new(
            AppTheme.Tracework,
            "迹构",
            "以信号轨迹、硬件模块和实时遥测为核心的深色主题。",
            ["#0A0D10", "#20272E", "#8D7CFF", "#65D9BC"])
    ];

    private readonly System.Windows.Application application;
    private readonly Func<AppTheme, ThemeResourceDictionary> dictionaryLoader;
    private readonly Action<IList<ResourceDictionary>, int, ThemeResourceDictionary> dictionaryReplacer;
    private ThemeResourceDictionary? activeThemeDictionary;

    public ThemeService(System.Windows.Application application)
        : this(application, LoadThemeDictionary)
    {
    }

    internal ThemeService(
        System.Windows.Application application,
        Func<AppTheme, ThemeResourceDictionary> dictionaryLoader,
        Action<IList<ResourceDictionary>, int, ThemeResourceDictionary>? dictionaryReplacer = null)
    {
        this.application = application ?? throw new ArgumentNullException(nameof(application));
        this.dictionaryLoader = dictionaryLoader ?? throw new ArgumentNullException(nameof(dictionaryLoader));
        this.dictionaryReplacer = dictionaryReplacer ?? ReplaceThemeDictionary;
        activeThemeDictionary = application.Resources.MergedDictionaries
            .OfType<ThemeResourceDictionary>()
            .SingleOrDefault();
        CurrentTheme = InferTheme(activeThemeDictionary?.Source);
    }

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public AppTheme CurrentTheme { get; private set; }

    public IReadOnlyList<ThemeDescriptor> AvailableThemes => Descriptors;

    public bool ApplyTheme(AppTheme theme)
    {
        if (!ThemeUris.ContainsKey(theme))
        {
            return false;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            return application.Dispatcher.Invoke(() => ApplyTheme(theme));
        }

        if (activeThemeDictionary is not null && CurrentTheme == theme)
        {
            return true;
        }

        ThemeResourceDictionary candidate;
        try
        {
            candidate = dictionaryLoader(theme);
            ValidateThemeDictionary(candidate);
        }
        catch (Exception exception)
        {
            LogThemeFailure(theme, "load", exception);
            return false;
        }

        IList<ResourceDictionary> merged = application.Resources.MergedDictionaries;
        ThemeResourceDictionary? previousDictionary = activeThemeDictionary;
        AppTheme previousTheme = CurrentTheme;
        int previousIndex = previousDictionary is null ? -1 : merged.IndexOf(previousDictionary);

        try
        {
            dictionaryReplacer(merged, previousIndex, candidate);

            activeThemeDictionary = candidate;
            CurrentTheme = theme;
        }
        catch (Exception exception)
        {
            RestorePreviousTheme(merged, candidate, previousDictionary, previousIndex, previousTheme);
            LogThemeFailure(theme, "replace", exception);
            return false;
        }

        if (previousTheme != theme)
        {
            RaiseThemeChanged(previousTheme, theme);
        }

        return true;
    }

    internal static ThemeResourceDictionary LoadThemeDictionary(AppTheme theme)
    {
        if (!ThemeUris.TryGetValue(theme, out Uri? uri))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unsupported application theme.");
        }

        return new ThemeResourceDictionary { Source = uri };
    }

    internal static IReadOnlyCollection<object> GetEffectiveResourceKeys(ResourceDictionary dictionary)
    {
        HashSet<object> keys = [];
        CollectResourceKeys(dictionary, keys);
        return keys;
    }

    private static void ValidateThemeDictionary(ResourceDictionary dictionary)
    {
        IReadOnlyCollection<object> keys = GetEffectiveResourceKeys(dictionary);
        foreach (string key in RequiredResourceKeys)
        {
            if (!keys.Contains(key))
            {
                throw new InvalidOperationException($"Theme resource '{key}' is missing.");
            }
        }
    }

    private static void CollectResourceKeys(ResourceDictionary dictionary, ISet<object> keys)
    {
        foreach (object key in dictionary.Keys)
        {
            keys.Add(key);
        }

        foreach (ResourceDictionary mergedDictionary in dictionary.MergedDictionaries)
        {
            CollectResourceKeys(mergedDictionary, keys);
        }
    }

    private static void ReplaceThemeDictionary(
        IList<ResourceDictionary> merged,
        int previousIndex,
        ThemeResourceDictionary candidate)
    {
        if (previousIndex >= 0)
        {
            merged[previousIndex] = candidate;
        }
        else
        {
            merged.Add(candidate);
        }
    }

    private void RaiseThemeChanged(AppTheme previousTheme, AppTheme currentTheme)
    {
        ThemeChangedEventArgs args = new(previousTheme, currentTheme);
        foreach (EventHandler<ThemeChangedEventArgs> handler in
                 ThemeChanged?.GetInvocationList().Cast<EventHandler<ThemeChangedEventArgs>>() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                AppLogger.LogError(
                    "ThemeChanged subscriber failed.",
                    exception,
                    $"theme-changed-subscriber:{exception.GetType().FullName}",
                    TimeSpan.FromMinutes(5));
            }
        }
    }

    private void RestorePreviousTheme(
        IList<ResourceDictionary> merged,
        ThemeResourceDictionary candidate,
        ThemeResourceDictionary? previousDictionary,
        int previousIndex,
        AppTheme previousTheme)
    {
        bool previousRestored = false;
        try
        {
            int candidateIndex = merged.IndexOf(candidate);
            if (previousDictionary is not null)
            {
                if (candidateIndex >= 0)
                {
                    merged[candidateIndex] = previousDictionary;
                }
                else if (!merged.Contains(previousDictionary))
                {
                    merged.Insert(Math.Clamp(previousIndex, 0, merged.Count), previousDictionary);
                }
            }
            else if (candidateIndex >= 0)
            {
                merged.RemoveAt(candidateIndex);
            }
            previousRestored = true;
        }
        catch (Exception restoreException)
        {
            AppLogger.LogError(
                "Theme rollback failed; the application will attempt to restore Classic.",
                restoreException,
                $"theme-rollback:{restoreException.GetType().FullName}",
                TimeSpan.FromMinutes(5));
            TryRestoreClassic(merged);
        }

        if (previousRestored)
        {
            activeThemeDictionary = previousDictionary;
            CurrentTheme = previousTheme;
        }
    }

    private void TryRestoreClassic(IList<ResourceDictionary> merged)
    {
        try
        {
            ThemeResourceDictionary classic = dictionaryLoader(AppTheme.Classic);
            ValidateThemeDictionary(classic);
            foreach (ThemeResourceDictionary existing in merged.OfType<ThemeResourceDictionary>().ToArray())
            {
                merged.Remove(existing);
            }
            merged.Add(classic);
            activeThemeDictionary = classic;
            CurrentTheme = AppTheme.Classic;
        }
        catch (Exception fallbackException)
        {
            AppLogger.LogError(
                "Classic theme fallback failed.",
                fallbackException,
                $"theme-classic-fallback:{fallbackException.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
    }

    private static AppTheme InferTheme(Uri? source) =>
        source?.OriginalString.Contains("/Tracework/", StringComparison.OrdinalIgnoreCase) == true
            ? AppTheme.Tracework
            : AppTheme.Classic;

    private static void LogThemeFailure(AppTheme theme, string operation, Exception exception)
    {
        AppLogger.LogError(
            $"Theme {operation} failed for {theme}.",
            exception,
            $"theme-{operation}:{theme}:{exception.GetType().FullName}",
            TimeSpan.FromMinutes(5));
    }
}
