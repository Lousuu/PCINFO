using System.Windows;

namespace HardwareVision.Controls;

public enum NavigationMotionRole
{
    None,
    Primary,
    Secondary
}

public static class NavigationMotion
{
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role",
        typeof(NavigationMotionRole),
        typeof(NavigationMotion),
        new FrameworkPropertyMetadata(NavigationMotionRole.None));

    public static void SetRole(DependencyObject element, NavigationMotionRole value) =>
        element.SetValue(RoleProperty, value);

    public static NavigationMotionRole GetRole(DependencyObject element) =>
        (NavigationMotionRole)element.GetValue(RoleProperty);
}
