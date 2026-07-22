namespace HardwareVision.Views.Shell;

public partial class TraceworkTelemetrySpine : System.Windows.Controls.UserControl
{
    public TraceworkTelemetrySpine()
    {
        InitializeComponent();
    }

    public void CancelTransition() => TitleTransitionHost.CancelTransition();
}
