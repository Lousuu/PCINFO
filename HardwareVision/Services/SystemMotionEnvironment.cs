using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace HardwareVision.Services;

public sealed class SystemMotionEnvironment : IMotionEnvironment, IDisposable
{
    private bool areClientAnimationsEnabled;
    private bool isHighContrast;
    private bool isRemoteSession;
    private int renderTier;
    private bool isDisposed;

    public SystemMotionEnvironment()
    {
        RefreshCore(raiseChanged: false);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public bool AreClientAnimationsEnabled => areClientAnimationsEnabled;

    public bool IsHighContrast => isHighContrast;

    public bool IsRemoteSession => isRemoteSession;

    public int RenderTier => renderTier;

    public event EventHandler? EnvironmentChanged;

    public void Refresh()
    {
        if (isDisposed)
        {
            return;
        }

        RefreshCore(raiseChanged: true);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation)
            or nameof(SystemParameters.HighContrast))
        {
            Refresh();
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Refresh();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Refresh();

    private void RefreshCore(bool raiseChanged)
    {
        bool nextClientAnimations = SystemParameters.ClientAreaAnimation;
        bool nextHighContrast = SystemParameters.HighContrast;
        bool nextRemoteSession = System.Windows.Forms.SystemInformation.TerminalServerSession;
        int nextRenderTier = RenderCapability.Tier >> 16;

        bool changed = nextClientAnimations != areClientAnimationsEnabled
            || nextHighContrast != isHighContrast
            || nextRemoteSession != isRemoteSession
            || nextRenderTier != renderTier;

        areClientAnimationsEnabled = nextClientAnimations;
        isHighContrast = nextHighContrast;
        isRemoteSession = nextRemoteSession;
        renderTier = nextRenderTier;

        if (raiseChanged && changed)
        {
            EnvironmentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
