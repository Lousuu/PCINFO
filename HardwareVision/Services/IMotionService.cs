using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IMotionService
{
    MotionLevel RequestedLevel { get; }

    MotionLevel EffectiveLevel { get; }

    MotionProfile CurrentProfile { get; }

    IReadOnlyList<MotionLevel> AvailableLevels { get; }

    event EventHandler<MotionChangedEventArgs>? MotionChanged;

    bool SetRequestedLevel(MotionLevel level);

    void RefreshEnvironment();
}
