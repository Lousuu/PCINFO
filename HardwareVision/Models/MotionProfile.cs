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
                TimeSpan.FromMilliseconds(220),
                8d,
                0.52d,
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
                TimeSpan.FromMilliseconds(105),
                0d,
                0.84d,
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
                TimeSpan.FromMilliseconds(175),
                5d,
                0.66d,
                AllowsOpacity: true,
                AllowsSpatialMotion: true,
                AllowsScale: false,
                IsAnimationEnabled: true,
                fallbackReason)
        };
    }
}
