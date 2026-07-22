namespace HardwareVision.Views.Shell;

public partial class TraceworkShellChrome : System.Windows.Controls.UserControl
{
    public TraceworkShellChrome()
    {
        InitializeComponent();
    }

    public void CancelFlowRelayVisuals()
    {
        SignalRail.CancelTransition();
        TelemetrySpine.CancelTransition();
    }

    internal System.Windows.FrameworkElement StartupSignalRailTarget => SignalRail;

    internal System.Windows.FrameworkElement StartupTelemetryTarget => TelemetrySpine;

    internal System.Windows.FrameworkElement StartupTimeRibbonTarget => TimeRibbon;
}
