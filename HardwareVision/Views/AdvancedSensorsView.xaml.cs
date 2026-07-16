namespace HardwareVision.Views;

public partial class AdvancedSensorsView : System.Windows.Controls.UserControl
{
    private bool hasLoggedFirstLoad;

    public AdvancedSensorsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (hasLoggedFirstLoad)
            {
                return;
            }

            hasLoggedFirstLoad = true;
            HardwareVision.Utilities.AppLogger.LogKeyEvent("StartupTiming | AdvancedSensorsView first loaded");
            HardwareVision.Utilities.AppLogger.LogMemoryCheckpoint("AdvancedSensorsView first loaded");
        };
    }
}
