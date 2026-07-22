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
}
