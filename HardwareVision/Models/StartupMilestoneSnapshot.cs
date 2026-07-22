namespace HardwareVision.Models;

public sealed record StartupMilestoneSnapshot(
    StartupMilestoneId Id,
    string Name,
    StartupMilestoneState State,
    string StatusText,
    string Detail)
{
    public static StartupMilestoneSnapshot Waiting(StartupMilestoneId id) =>
        new(id, GetName(id), StartupMilestoneState.Wait, "WAIT", string.Empty);

    public static string GetName(StartupMilestoneId id) => id switch
    {
        StartupMilestoneId.ThemeResources => "THEME RESOURCES",
        StartupMilestoneId.ServiceGraph => "SERVICE GRAPH",
        StartupMilestoneId.PageRouter => "PAGE ROUTER",
        StartupMilestoneId.SensorBus => "SENSOR BUS",
        StartupMilestoneId.HistoryBuffer => "HISTORY BUFFER",
        StartupMilestoneId.ShellSurface => "SHELL SURFACE",
        _ => id.ToString().ToUpperInvariant()
    };

    public static string GetStatusText(StartupMilestoneState state) => state switch
    {
        StartupMilestoneState.Pending => "PENDING",
        StartupMilestoneState.Ready => "READY",
        StartupMilestoneState.Partial => "PARTIAL",
        StartupMilestoneState.Failed => "FAILED",
        _ => "WAIT"
    };
}
