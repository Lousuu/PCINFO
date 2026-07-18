using System.Windows.Threading;
using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class MotionService : IMotionService, IDisposable
{
    private static readonly IReadOnlyList<MotionLevel> Levels =
    [
        MotionLevel.Full,
        MotionLevel.Standard,
        MotionLevel.Reduced,
        MotionLevel.Off
    ];

    private readonly IMotionEnvironment environment;
    private readonly Dispatcher dispatcher;
    private MotionProfile currentProfile;
    private bool isDisposed;

    public MotionService(IMotionEnvironment environment, MotionLevel requestedLevel, Dispatcher? dispatcher = null)
    {
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        this.dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        currentProfile = CreateProfile(requestedLevel);
        environment.EnvironmentChanged += OnEnvironmentChanged;
    }

    public MotionLevel RequestedLevel => currentProfile.RequestedLevel;

    public MotionLevel EffectiveLevel => currentProfile.EffectiveLevel;

    public MotionProfile CurrentProfile => currentProfile;

    public IReadOnlyList<MotionLevel> AvailableLevels => Levels;

    public event EventHandler<MotionChangedEventArgs>? MotionChanged;

    public bool SetRequestedLevel(MotionLevel level)
    {
        if (isDisposed || RequestedLevel == level)
        {
            return false;
        }

        ApplyProfile(CreateProfile(level), MotionChangeReason.RequestedLevelChanged);
        return true;
    }

    public void RefreshEnvironment()
    {
        if (isDisposed)
        {
            return;
        }

        environment.Refresh();
        ApplyProfile(CreateProfile(RequestedLevel), MotionChangeReason.EnvironmentChanged);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        environment.EnvironmentChanged -= OnEnvironmentChanged;
    }

    private void OnEnvironmentChanged(object? sender, EventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        ApplyProfile(CreateProfile(RequestedLevel), MotionChangeReason.EnvironmentChanged);
    }

    private MotionProfile CreateProfile(MotionLevel requestedLevel)
    {
        (MotionLevel effectiveLevel, string fallbackReason) = ResolveEffectiveLevel(requestedLevel);
        return MotionProfile.Create(requestedLevel, effectiveLevel, fallbackReason);
    }

    private (MotionLevel EffectiveLevel, string FallbackReason) ResolveEffectiveLevel(MotionLevel requestedLevel)
    {
        if (requestedLevel == MotionLevel.Off)
        {
            return (MotionLevel.Off, "Requested Off");
        }

        if (!environment.AreClientAnimationsEnabled)
        {
            return (MotionLevel.Off, "Windows animations disabled");
        }

        if (environment.RenderTier <= 0)
        {
            return (MotionLevel.Off, "Render tier 0");
        }

        if (environment.IsHighContrast)
        {
            return (CapAtReduced(requestedLevel), "High contrast");
        }

        if (environment.IsRemoteSession)
        {
            return (CapAtReduced(requestedLevel), "Remote session");
        }

        if (environment.RenderTier == 1)
        {
            return (CapAtReduced(requestedLevel), "Render tier 1");
        }

        return (requestedLevel, string.Empty);
    }

    private static MotionLevel CapAtReduced(MotionLevel requestedLevel) =>
        requestedLevel is MotionLevel.Full or MotionLevel.Standard
            ? MotionLevel.Reduced
            : requestedLevel;

    private void ApplyProfile(MotionProfile nextProfile, MotionChangeReason reason)
    {
        MotionProfile previousProfile = currentProfile;
        if (previousProfile.RequestedLevel == nextProfile.RequestedLevel
            && previousProfile.EffectiveLevel == nextProfile.EffectiveLevel)
        {
            return;
        }

        currentProfile = nextProfile;
        MotionChangedEventArgs args = new(
            previousProfile.RequestedLevel,
            nextProfile.RequestedLevel,
            previousProfile.EffectiveLevel,
            nextProfile.EffectiveLevel,
            previousProfile,
            nextProfile,
            reason);

        RaiseMotionChanged(args);
    }

    private void RaiseMotionChanged(MotionChangedEventArgs args)
    {
        if (isDisposed)
        {
            return;
        }

        void Raise()
        {
            if (!isDisposed)
            {
                MotionChanged?.Invoke(this, args);
            }
        }

        if (dispatcher.CheckAccess())
        {
            Raise();
        }
        else
        {
            dispatcher.BeginInvoke(Raise, DispatcherPriority.DataBind);
        }
    }
}
