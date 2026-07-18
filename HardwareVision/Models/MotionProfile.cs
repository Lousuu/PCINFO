namespace HardwareVision.Models;

public sealed record MotionProfile(
    MotionLevel RequestedLevel,
    MotionLevel EffectiveLevel,
    TimeSpan FastDuration,
    TimeSpan NormalDuration,
    TimeSpan SlowDuration,
    TimeSpan PageEnterDuration,
    double PageEnterOffset,
    double PageEnterStartOpacity,
    bool AllowsOpacity,
    bool AllowsSpatialMotion,
    bool AllowsScale,
    bool IsAnimationEnabled,
    string FallbackReason)
{
    public static MotionProfile Create(MotionLevel requestedLevel, MotionLevel effectiveLevel, string fallbackReason)
    {
        TimeSpan fast = TimeSpan.FromMilliseconds(80);
        TimeSpan normal = TimeSpan.FromMilliseconds(130);
        TimeSpan slow = TimeSpan.FromMilliseconds(180);

        return effectiveLevel switch
        {
            MotionLevel.Full => new(
                requestedLevel,
                effectiveLevel,
                fast,
                normal,
                slow,
                TimeSpan.FromMilliseconds(180),
                8d,
                0.68d,
                AllowsOpacity: true,
                AllowsSpatialMotion: true,
                AllowsScale: false,
                IsAnimationEnabled: true,
                fallbackReason),
            MotionLevel.Reduced => new(
                requestedLevel,
                effectiveLevel,
                fast,
                normal,
                slow,
                TimeSpan.FromMilliseconds(80),
                0d,
                0.88d,
                AllowsOpacity: true,
                AllowsSpatialMotion: false,
                AllowsScale: false,
                IsAnimationEnabled: true,
                fallbackReason),
            MotionLevel.Off => new(
                requestedLevel,
                effectiveLevel,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                0d,
                1d,
                AllowsOpacity: false,
                AllowsSpatialMotion: false,
                AllowsScale: false,
                IsAnimationEnabled: false,
                fallbackReason),
            _ => new(
                requestedLevel,
                MotionLevel.Standard,
                fast,
                normal,
                slow,
                TimeSpan.FromMilliseconds(130),
                4d,
                0.80d,
                AllowsOpacity: true,
                AllowsSpatialMotion: true,
                AllowsScale: false,
                IsAnimationEnabled: true,
                fallbackReason)
        };
    }
}
