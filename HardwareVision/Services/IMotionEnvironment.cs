namespace HardwareVision.Services;

public interface IMotionEnvironment
{
    bool AreClientAnimationsEnabled { get; }

    bool IsHighContrast { get; }

    bool IsRemoteSession { get; }

    int RenderTier { get; }

    event EventHandler? EnvironmentChanged;

    void Refresh();
}
