using HardwareVision.Controls;

namespace HardwareVision.Views.AdvancedSensors;

public partial class TraceworkAdvancedSensorsLayout : System.Windows.Controls.UserControl
{
    public TraceworkAdvancedSensorsLayout()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyResponsiveDataGridHeight(ActualWidth);
        Loaded += (_, _) => ApplyResponsiveDataGridHeight(ActualWidth);
    }

    internal static (double Minimum, double Height, double Maximum) ResolveDataGridHeight(double width) =>
        TraceworkResponsiveGrid.ResolveMode(width) switch
        {
            TraceworkResponsiveMode.Wide => (520d, 640d, 760d),
            TraceworkResponsiveMode.Standard => (460d, 560d, 680d),
            TraceworkResponsiveMode.Compact => (420d, 500d, 600d),
            _ => (380d, 460d, 540d)
        };

    private void ApplyResponsiveDataGridHeight(double width)
    {
        (double minimum, double height, double maximum) = ResolveDataGridHeight(width);
        AdvancedSensorDataGrid.MinHeight = minimum;
        AdvancedSensorDataGrid.Height = height;
        AdvancedSensorDataGrid.MaxHeight = maximum;
    }
}
