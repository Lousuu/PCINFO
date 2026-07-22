namespace HardwareVision.Models;

public sealed record NavigationRouteDescriptor(
    string PageKey,
    string Code,
    string Title,
    string Subtitle,
    NavigationGroup Group,
    int GroupIndex,
    bool IsReport = false)
{
    public static bool TryCreate(
        string pageKey,
        string code,
        string title,
        string subtitle,
        out NavigationRouteDescriptor? descriptor)
    {
        descriptor = pageKey switch
        {
            "Dashboard" => new(pageKey, code, title, subtitle, NavigationGroup.System, 0),
            "Cpu" => new(pageKey, code, title, subtitle, NavigationGroup.System, 1),
            "Gpu" => new(pageKey, code, title, subtitle, NavigationGroup.System, 2),
            "Memory" => new(pageKey, code, title, subtitle, NavigationGroup.System, 3),
            "Disk" => new(pageKey, code, title, subtitle, NavigationGroup.System, 4),
            "Network" => new(pageKey, code, title, subtitle, NavigationGroup.System, 5),
            "Motherboard" => new(pageKey, code, title, subtitle, NavigationGroup.System, 6),
            "AdvancedSensors" => new(pageKey, code, title, subtitle, NavigationGroup.System, 7),
            "GamePerformance" => new(pageKey, code, title, subtitle, NavigationGroup.Session, 0),
            "GameSessionReport" => new(pageKey, code, title, subtitle, NavigationGroup.Session, 1, true),
            "Settings" => new(pageKey, code, title, subtitle, NavigationGroup.Control, 0),
            "MetricVisibility" => new(pageKey, code, title, subtitle, NavigationGroup.Control, 1),
            _ => null
        };

        return descriptor is not null;
    }

    public static NavigationTransitionDirection ResolveDirection(
        NavigationRouteDescriptor? origin,
        NavigationRouteDescriptor? target)
    {
        if (origin is null || target is null || origin.PageKey == target.PageKey)
        {
            return NavigationTransitionDirection.None;
        }

        if (origin.IsReport || target.IsReport)
        {
            return origin.PageKey == "GamePerformance" && target.PageKey == "GameSessionReport"
                ? NavigationTransitionDirection.FromRight
                : origin.PageKey == "GameSessionReport" && target.PageKey == "GamePerformance"
                    ? NavigationTransitionDirection.FromLeft
                    : NavigationTransitionDirection.None;
        }

        if (origin.Group == target.Group)
        {
            return target.GroupIndex > origin.GroupIndex
                ? NavigationTransitionDirection.FromBottom
                : NavigationTransitionDirection.FromTop;
        }

        return target.Group > origin.Group
            ? NavigationTransitionDirection.FromRight
            : NavigationTransitionDirection.FromLeft;
    }
}
