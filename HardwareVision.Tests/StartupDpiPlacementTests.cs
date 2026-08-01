using HardwareVision.Interop;

namespace HardwareVision.Tests;

internal static class StartupDpiPlacementTests
{
    private const double WidthDip = 1120d;
    private const double HeightDip = 720d;

    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            tests.Add(($"DPI placement 100 percent {iteration:00}/20", Verify100Percent));
            tests.Add(($"DPI placement 125 percent {iteration:00}/20", Verify125Percent));
            tests.Add(($"DPI placement 150 percent {iteration:00}/20", Verify150Percent));
            tests.Add(($"DPI placement 175 percent {iteration:00}/20", Verify175Percent));
            tests.Add(($"DPI placement 200 percent {iteration:00}/20", Verify200Percent));
            tests.Add(($"DPI placement negative-left monitor {iteration:00}/20", VerifyNegativeLeftMonitor));
            tests.Add(($"DPI placement right-side monitor {iteration:00}/20", VerifyRightSideMonitor));
            tests.Add(($"DPI placement lower monitor {iteration:00}/20", VerifyLowerMonitor));
            tests.Add(($"DPI placement mixed-monitor scaling {iteration:00}/20", VerifyMixedMonitorScaling));
            tests.Add(($"DPI placement captured monitor survives cursor move {iteration:00}/20", VerifyCapturedMonitorIsStable));
        }
        return tests;
    }

    private static void Verify100Percent() =>
        AssertBounds(
            new PhysicalPixelRect(0, 0, 1920, 1080),
            96,
            400,
            180,
            1120,
            720);

    private static void Verify125Percent() =>
        AssertBounds(
            new PhysicalPixelRect(0, 0, 2560, 1440),
            120,
            580,
            270,
            1400,
            900);

    private static void Verify150Percent() =>
        AssertBounds(
            new PhysicalPixelRect(0, 0, 2560, 1440),
            144,
            440,
            180,
            1680,
            1080);

    private static void Verify175Percent() =>
        AssertBounds(
            new PhysicalPixelRect(0, 0, 3440, 1440),
            168,
            740,
            90,
            1960,
            1260);

    private static void Verify200Percent() =>
        AssertBounds(
            new PhysicalPixelRect(0, 0, 3840, 2160),
            192,
            800,
            360,
            2240,
            1440);

    private static void VerifyNegativeLeftMonitor() =>
        AssertBounds(
            new PhysicalPixelRect(-2560, 0, 0, 1440),
            144,
            -2120,
            180,
            1680,
            1080);

    private static void VerifyRightSideMonitor() =>
        AssertBounds(
            new PhysicalPixelRect(1920, 0, 4480, 1440),
            144,
            2360,
            180,
            1680,
            1080);

    private static void VerifyLowerMonitor() =>
        AssertBounds(
            new PhysicalPixelRect(0, 1080, 1920, 3240),
            96,
            400,
            1800,
            1120,
            720);

    private static void VerifyMixedMonitorScaling()
    {
        FirstFramePhysicalPlacement primary = WindowPlacementInterop.CreateCenteredPlacement(
            (nint)1,
            new PhysicalPixelRect(0, 0, 1920, 1080),
            96,
            96,
            WidthDip,
            HeightDip);
        FirstFramePhysicalPlacement scaled = WindowPlacementInterop.CreateCenteredPlacement(
            (nint)2,
            new PhysicalPixelRect(1920, 0, 4480, 1440),
            144,
            144,
            WidthDip,
            HeightDip);

        TestSupport.Equal(1120, primary.Bounds.Width, "primary physical width");
        TestSupport.Equal(1680, scaled.Bounds.Width, "scaled physical width");
        TestSupport.Equal(720, primary.Bounds.Height, "primary physical height");
        TestSupport.Equal(1080, scaled.Bounds.Height, "scaled physical height");
        AssertCentered(primary);
        AssertCentered(scaled);
    }

    private static void VerifyCapturedMonitorIsStable()
    {
        FirstFramePhysicalPlacement captured = WindowPlacementInterop.CreateCenteredPlacement(
            (nint)11,
            new PhysicalPixelRect(-2560, 0, 0, 1440),
            144,
            144,
            WidthDip,
            HeightDip);
        FirstFramePhysicalPlacement cursorAfterCapture = WindowPlacementInterop.CreateCenteredPlacement(
            (nint)22,
            new PhysicalPixelRect(1920, 0, 4480, 1440),
            120,
            120,
            WidthDip,
            HeightDip);

        TestSupport.Equal((nint)11, captured.Monitor, "captured monitor handle");
        TestSupport.Equal(-2120, captured.Bounds.Left, "captured monitor position");
        TestSupport.Equal(144u, captured.DpiX, "captured monitor DPI");
        TestSupport.False(captured.Bounds == cursorAfterCapture.Bounds, "later cursor monitor cannot alter captured bounds");
        AssertCentered(captured);
    }

    private static void AssertBounds(
        PhysicalPixelRect workArea,
        uint dpi,
        int expectedLeft,
        int expectedTop,
        int expectedWidth,
        int expectedHeight)
    {
        FirstFramePhysicalPlacement placement = WindowPlacementInterop.CreateCenteredPlacement(
            (nint)1,
            workArea,
            dpi,
            dpi,
            WidthDip,
            HeightDip);
        TestSupport.True(placement.IsCaptured, "placement captured");
        NearlyEqual(expectedLeft, placement.Bounds.Left, "physical left");
        NearlyEqual(expectedTop, placement.Bounds.Top, "physical top");
        NearlyEqual(expectedWidth, placement.Bounds.Width, "physical width");
        NearlyEqual(expectedHeight, placement.Bounds.Height, "physical height");
        AssertCentered(placement);
    }

    private static void AssertCentered(FirstFramePhysicalPlacement placement)
    {
        double windowCenterX = placement.Bounds.Left + placement.Bounds.Width / 2d;
        double windowCenterY = placement.Bounds.Top + placement.Bounds.Height / 2d;
        double workCenterX = placement.WorkArea.Left + placement.WorkArea.Width / 2d;
        double workCenterY = placement.WorkArea.Top + placement.WorkArea.Height / 2d;
        TestSupport.True(Math.Abs(windowCenterX - workCenterX) <= 2d, "horizontal center within two physical pixels");
        TestSupport.True(Math.Abs(windowCenterY - workCenterY) <= 2d, "vertical center within two physical pixels");
    }

    private static void NearlyEqual(int expected, int actual, string message) =>
        TestSupport.True(Math.Abs(expected - actual) <= 2, $"{message}: expected {expected}, got {actual}");
}
