using HardwareVision.Models;

namespace HardwareVision.Controls;

internal static class MotionTransitionPlanFactory
{
    public static MotionTransitionPlan Create(
        MotionProfile profile,
        bool isTransitionEnabled,
        bool isLoaded,
        bool isVisible,
        bool isWindowVisible,
        MotionTransitionDirection direction)
    {
        ArgumentNullException.ThrowIfNull(profile);
        bool shouldAnimate = isTransitionEnabled
            && isLoaded
            && isVisible
            && isWindowVisible
            && profile.IsAnimationEnabled
            && profile.PageEnterDuration > TimeSpan.Zero
            && profile.AllowsOpacity;

        bool spatial = shouldAnimate
            && profile.AllowsSpatialMotion
            && profile.PageEnterOffset > 0d;

        return new MotionTransitionPlan(
            shouldAnimate,
            shouldAnimate,
            spatial,
            shouldAnimate ? profile.PageEnterDuration : TimeSpan.Zero,
            shouldAnimate ? profile.PageEnterStartOpacity : 1d,
            spatial ? profile.PageEnterOffset : 0d,
            direction,
            profile.EffectiveLevel);
    }
}
