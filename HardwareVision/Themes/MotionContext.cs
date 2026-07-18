using System.Windows;
using HardwareVision.Models;

namespace HardwareVision.Themes;

public static class MotionContext
{
    public static readonly DependencyProperty RequestedLevelProperty =
        DependencyProperty.RegisterAttached(
            "RequestedLevel",
            typeof(MotionLevel),
            typeof(MotionContext),
            new FrameworkPropertyMetadata(
                MotionLevel.Standard,
                FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty EffectiveLevelProperty =
        DependencyProperty.RegisterAttached(
            "EffectiveLevel",
            typeof(MotionLevel),
            typeof(MotionContext),
            new FrameworkPropertyMetadata(
                MotionLevel.Standard,
                FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty CurrentProfileProperty =
        DependencyProperty.RegisterAttached(
            "CurrentProfile",
            typeof(MotionProfile),
            typeof(MotionContext),
            new FrameworkPropertyMetadata(
                MotionProfile.Create(MotionLevel.Standard, MotionLevel.Standard, string.Empty),
                FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty IsAnimationEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsAnimationEnabled",
            typeof(bool),
            typeof(MotionContext),
            new FrameworkPropertyMetadata(
                true,
                FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty AllowsSpatialMotionProperty =
        DependencyProperty.RegisterAttached(
            "AllowsSpatialMotion",
            typeof(bool),
            typeof(MotionContext),
            new FrameworkPropertyMetadata(
                true,
                FrameworkPropertyMetadataOptions.Inherits));

    public static MotionLevel GetRequestedLevel(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (MotionLevel)element.GetValue(RequestedLevelProperty);
    }

    public static void SetRequestedLevel(DependencyObject element, MotionLevel value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RequestedLevelProperty, value);
    }

    public static MotionLevel GetEffectiveLevel(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (MotionLevel)element.GetValue(EffectiveLevelProperty);
    }

    public static void SetEffectiveLevel(DependencyObject element, MotionLevel value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EffectiveLevelProperty, value);
    }

    public static MotionProfile GetCurrentProfile(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (MotionProfile)element.GetValue(CurrentProfileProperty);
    }

    public static void SetCurrentProfile(DependencyObject element, MotionProfile value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CurrentProfileProperty, value);
    }

    public static bool GetIsAnimationEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsAnimationEnabledProperty);
    }

    public static void SetIsAnimationEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsAnimationEnabledProperty, value);
    }

    public static bool GetAllowsSpatialMotion(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(AllowsSpatialMotionProperty);
    }

    public static void SetAllowsSpatialMotion(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AllowsSpatialMotionProperty, value);
    }
}
