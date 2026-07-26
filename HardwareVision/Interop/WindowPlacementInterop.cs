using System.Runtime.InteropServices;

namespace HardwareVision.Interop;

internal static class WindowPlacementInterop
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoZOrder = 0x0004;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;

    internal static FirstFramePhysicalPlacement CreateCenteredPlacement(
        nint monitor,
        PhysicalPixelRect workArea,
        uint dpiX,
        uint dpiY,
        double widthDip,
        double heightDip) =>
        new(
            monitor,
            workArea,
            NormalizeDpi(dpiX),
            NormalizeDpi(dpiY),
            ResolveCenteredBounds(workArea, widthDip, heightDip, dpiX, dpiY));

    internal static PhysicalPixelBounds ResolveCenteredBounds(
        PhysicalPixelRect workArea,
        double widthDip,
        double heightDip,
        uint dpiX,
        uint dpiY)
    {
        uint normalizedDpiX = NormalizeDpi(dpiX);
        uint normalizedDpiY = NormalizeDpi(dpiY);
        int width = ScaleDipToPhysical(widthDip, normalizedDpiX);
        int height = ScaleDipToPhysical(heightDip, normalizedDpiY);
        int left = workArea.Left
            + RoundPhysical(Math.Max(0d, (workArea.Width - width) / 2d));
        int top = workArea.Top
            + RoundPhysical(Math.Max(0d, (workArea.Height - height) / 2d));
        return new(left, top, width, height);
    }

    internal static PhysicalPixelBounds ResolveOffscreenBounds(
        int virtualLeft,
        int virtualTop,
        int expectedWidth,
        int expectedHeight) =>
        new(
            virtualLeft - Math.Max(expectedWidth, 640) - 128,
            virtualTop - Math.Max(expectedHeight, 480) - 128,
            expectedWidth,
            expectedHeight);

    internal static bool TryCaptureCursorCenteredPlacement(
        double widthDip,
        double heightDip,
        out FirstFramePhysicalPlacement placement)
    {
        placement = default;
        try
        {
            if (!GetCursorPos(out NativePoint cursor))
            {
                return false;
            }

            nint monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
            return TryCreateMonitorPlacement(
                monitor,
                widthDip,
                heightDip,
                out placement);
        }
        catch (Exception exception) when (IsInteropUnavailable(exception))
        {
            return false;
        }
    }

    internal static bool TryCaptureOwnerCenteredPlacement(
        nint ownerHandle,
        double widthDip,
        double heightDip,
        out FirstFramePhysicalPlacement placement)
    {
        placement = default;
        if (ownerHandle == nint.Zero)
        {
            return false;
        }

        try
        {
            nint monitor = MonitorFromWindow(ownerHandle, MonitorDefaultToNearest);
            if (!TryGetMonitorMetrics(monitor, out PhysicalPixelRect workArea, out uint dpiX, out uint dpiY)
                || !GetWindowRect(ownerHandle, out NativeRect ownerRect))
            {
                return false;
            }

            PhysicalPixelRect ownerArea = new(
                ownerRect.Left,
                ownerRect.Top,
                ownerRect.Right,
                ownerRect.Bottom);
            placement = new(
                monitor,
                workArea,
                dpiX,
                dpiY,
                ResolveCenteredBounds(ownerArea, widthDip, heightDip, dpiX, dpiY));
            return true;
        }
        catch (Exception exception) when (IsInteropUnavailable(exception))
        {
            return false;
        }
    }

    internal static bool TryCaptureCurrentPlacement(
        nint handle,
        out FirstFramePhysicalPlacement placement)
    {
        placement = default;
        if (handle == nint.Zero)
        {
            return false;
        }

        try
        {
            nint monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (!TryGetMonitorMetrics(monitor, out PhysicalPixelRect workArea, out uint dpiX, out uint dpiY)
                || !GetWindowRect(handle, out NativeRect windowRect))
            {
                return false;
            }

            placement = new(
                monitor,
                workArea,
                dpiX,
                dpiY,
                new PhysicalPixelBounds(
                    windowRect.Left,
                    windowRect.Top,
                    windowRect.Right - windowRect.Left,
                    windowRect.Bottom - windowRect.Top));
            return placement.Bounds.IsValid;
        }
        catch (Exception exception) when (IsInteropUnavailable(exception))
        {
            return false;
        }
    }

    internal static bool TryStageWindowOffscreen(
        nint handle,
        PhysicalPixelBounds expectedBounds)
    {
        if (handle == nint.Zero || !expectedBounds.IsValid)
        {
            return false;
        }

        try
        {
            PhysicalPixelBounds staging = ResolveOffscreenBounds(
                GetSystemMetrics(SmXVirtualScreen),
                GetSystemMetrics(SmYVirtualScreen),
                expectedBounds.Width,
                expectedBounds.Height);
            return ApplyWindowBounds(handle, staging);
        }
        catch (Exception exception) when (IsInteropUnavailable(exception))
        {
            return false;
        }
    }

    internal static bool TryApplyWindowBounds(
        nint handle,
        PhysicalPixelBounds bounds)
    {
        if (handle == nint.Zero || !bounds.IsValid)
        {
            return false;
        }

        try
        {
            return ApplyWindowBounds(handle, bounds);
        }
        catch (Exception exception) when (IsInteropUnavailable(exception))
        {
            return false;
        }
    }

    private static bool TryCreateMonitorPlacement(
        nint monitor,
        double widthDip,
        double heightDip,
        out FirstFramePhysicalPlacement placement)
    {
        placement = default;
        if (!TryGetMonitorMetrics(monitor, out PhysicalPixelRect workArea, out uint dpiX, out uint dpiY))
        {
            return false;
        }

        placement = CreateCenteredPlacement(
            monitor,
            workArea,
            dpiX,
            dpiY,
            widthDip,
            heightDip);
        return placement.Bounds.IsValid;
    }

    private static bool TryGetMonitorMetrics(
        nint monitor,
        out PhysicalPixelRect workArea,
        out uint dpiX,
        out uint dpiY)
    {
        workArea = default;
        dpiX = 96;
        dpiY = 96;
        if (monitor == nint.Zero)
        {
            return false;
        }

        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        workArea = new(
            info.Work.Left,
            info.Work.Top,
            info.Work.Right,
            info.Work.Bottom);
        try
        {
            if (GetDpiForMonitor(monitor, MonitorDpiType.Effective, out uint capturedDpiX, out uint capturedDpiY) == 0)
            {
                dpiX = NormalizeDpi(capturedDpiX);
                dpiY = NormalizeDpi(capturedDpiY);
            }
        }
        catch (Exception exception) when (IsInteropUnavailable(exception))
        {
        }
        return workArea.IsValid;
    }

    private static bool ApplyWindowBounds(nint handle, PhysicalPixelBounds bounds) =>
        SetWindowPos(
            handle,
            nint.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            SwpNoActivate | SwpNoOwnerZOrder | SwpNoZOrder);

    private static int ScaleDipToPhysical(double dip, uint dpi) =>
        Math.Max(1, RoundPhysical(dip * dpi / 96d));

    private static int RoundPhysical(double value) =>
        checked((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private static uint NormalizeDpi(uint dpi) => dpi == 0 ? 96u : dpi;

    private static bool IsInteropUnavailable(Exception exception) =>
        exception is DllNotFoundException
            or EntryPointNotFoundException
            or ExternalException
            or OverflowException;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint handle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        nint monitor,
        MonitorDpiType dpiType,
        out uint dpiX,
        out uint dpiY);

    private enum MonitorDpiType
    {
        Effective
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}

internal readonly record struct FirstFramePhysicalPlacement(
    nint Monitor,
    PhysicalPixelRect WorkArea,
    uint DpiX,
    uint DpiY,
    PhysicalPixelBounds Bounds)
{
    public bool IsCaptured => Monitor != nint.Zero && WorkArea.IsValid && Bounds.IsValid;
}

internal readonly record struct PhysicalPixelRect(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsValid => Width > 0 && Height > 0;
}

internal readonly record struct PhysicalPixelBounds(
    int Left,
    int Top,
    int Width,
    int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}
