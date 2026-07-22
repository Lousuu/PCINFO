using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class MotionChangedEventArgs : EventArgs
{
    public MotionChangedEventArgs(
        MotionLevel previousRequestedLevel,
        MotionLevel currentRequestedLevel,
        MotionLevel previousEffectiveLevel,
        MotionLevel currentEffectiveLevel,
        MotionProfile previousProfile,
        MotionProfile currentProfile,
        MotionChangeReason changeReason)
    {
        PreviousRequestedLevel = previousRequestedLevel;
        CurrentRequestedLevel = currentRequestedLevel;
        PreviousEffectiveLevel = previousEffectiveLevel;
        CurrentEffectiveLevel = currentEffectiveLevel;
        PreviousProfile = previousProfile;
        CurrentProfile = currentProfile;
        ChangeReason = changeReason;
    }

    public MotionLevel PreviousRequestedLevel { get; }

    public MotionLevel CurrentRequestedLevel { get; }

    public MotionLevel PreviousEffectiveLevel { get; }

    public MotionLevel CurrentEffectiveLevel { get; }

    public MotionProfile PreviousProfile { get; }

    public MotionProfile CurrentProfile { get; }

    public MotionChangeReason ChangeReason { get; }
}
