using HardwareVision.Models;

namespace HardwareVision.Controls;

internal sealed record MotionTransitionPlan(
    bool ShouldAnimate,
    bool AnimatesOpacity,
    bool AnimatesTranslation,
    TimeSpan Duration,
    double StartOpacity,
    double Offset,
    MotionTransitionDirection Direction,
    MotionLevel EffectiveLevel);
