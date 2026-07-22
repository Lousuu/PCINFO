namespace HardwareVision.Tests;

internal static class FlowRelayLifecycleTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Flow lifecycle 01 hidden window completes latest", HiddenWindowCompletesLatest),
        ("Flow lifecycle 02 minimized window completes latest", MinimizedWindowCompletesLatest),
        ("Flow lifecycle 03 closing cancels without new animation", ClosingCancelsWithoutAnimation),
        ("Flow lifecycle 04 shell unload unsubscribes", ShellUnloadUnsubscribes),
        ("Flow lifecycle 05 visual controls restore final state", VisualControlsRestoreFinalState),
        ("Flow lifecycle 06 focus stays outside overlays", FocusStaysOutsideOverlays),
        ("Flow lifecycle 07 no layout properties are animated", NoLayoutPropertiesAreAnimated),
        ("Flow lifecycle 08 no page copy or screenshot", NoPageCopyOrScreenshot),
        ("Flow lifecycle 09 tasks are observed", TasksAreObserved),
        ("Flow lifecycle 10 settings takeover commits pending target", SettingsTakeoverCommitsPendingTarget)
    ];

    private static string Root => FindRoot();
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static void HiddenWindowCompletesLatest()
    {
        string main = Read("HardwareVision", "ViewModels", "MainViewModel.cs");
        TestSupport.True(Slice(main, "public void SetWindowVisible", "public void SetWindowMinimized").Contains("CompletePendingNavigationImmediately", StringComparison.Ordinal), "hidden completion");
    }

    private static void MinimizedWindowCompletesLatest()
    {
        string main = Read("HardwareVision", "ViewModels", "MainViewModel.cs");
        TestSupport.True(Slice(main, "public void SetWindowMinimized", "public void SetWindowClosing").Contains("CompletePendingNavigationImmediately", StringComparison.Ordinal), "minimized completion");
        TestSupport.True(Read("HardwareVision", "MainWindow.xaml.cs").Contains("WindowState == WindowState.Minimized", StringComparison.Ordinal), "window state hook");
    }

    private static void ClosingCancelsWithoutAnimation()
    {
        string method = Slice(Read("HardwareVision", "ViewModels", "MainViewModel.cs"), "public void SetWindowClosing", "public void NotifyShellUnloaded");
        TestSupport.True(method.Contains("navigationTransitionService.Cancel()", StringComparison.Ordinal), "closing cancel");
        TestSupport.False(method.Contains("CommitAsync", StringComparison.Ordinal), "closing commit");
    }

    private static void ShellUnloadUnsubscribes()
    {
        string shell = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml.cs");
        TestSupport.True(shell.Contains("viewModel.PropertyChanged -= OnViewModelPropertyChanged", StringComparison.Ordinal), "shell unsubscribe");
        TestSupport.True(shell.Contains("viewModel.NotifyShellUnloaded()", StringComparison.Ordinal), "shell lifecycle notification");
    }

    private static void VisualControlsRestoreFinalState()
    {
        foreach (string file in new[] { "RelayBandOverlay.cs", "TelemetryTitleTransitionHost.cs", "MotionTransitionHost.cs" })
            TestSupport.True(Read("HardwareVision", "Controls", file).Contains("RestoreFinalState", StringComparison.Ordinal), file);
    }

    private static void FocusStaysOutsideOverlays()
    {
        string relay = Read("HardwareVision", "Controls", "RelayBandOverlay.cs");
        string rewire = Read("HardwareVision", "Controls", "SystemRewireOverlay.cs");
        TestSupport.True(relay.Contains("Focusable = false", StringComparison.Ordinal), "relay focus");
        TestSupport.True(rewire.Contains("Focusable = false", StringComparison.Ordinal), "rewire focus");
    }

    private static void NoLayoutPropertiesAreAnimated()
    {
        string source = string.Join('\n', new[] { "RelayBandOverlay.cs", "TelemetryTitleTransitionHost.cs", "MotionTransitionHost.cs" }.Select(file => Read("HardwareVision", "Controls", file)));
        foreach (string forbidden in new[] { "WidthProperty", "HeightProperty", "MarginProperty", "PaddingProperty", "LayoutTransform" })
            TestSupport.False(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    private static void NoPageCopyOrScreenshot()
    {
        string source = string.Join('\n', Directory.EnumerateFiles(Path.Combine(Root, "HardwareVision"), "*.cs", SearchOption.AllDirectories).Where(path => path.Contains("Navigation", StringComparison.OrdinalIgnoreCase) || path.Contains("Relay", StringComparison.OrdinalIgnoreCase)).Select(File.ReadAllText));
        foreach (string forbidden in new[] { "RenderTargetBitmap", "VisualBrush", "BitmapSource", "Screenshot" })
            TestSupport.False(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    private static void TasksAreObserved()
    {
        string main = Read("HardwareVision", "ViewModels", "MainViewModel.cs");
        TestSupport.True(main.Contains("ObserveNavigationTask", StringComparison.Ordinal), "task observer");
        TestSupport.True(main.Contains("catch (OperationCanceledException)", StringComparison.Ordinal), "cancellation observer");
    }

    private static void SettingsTakeoverCommitsPendingTarget()
    {
        string settings = Read("HardwareVision", "ViewModels", "SettingsViewModel.cs");
        string main = Read("HardwareVision", "ViewModels", "MainViewModel.cs");
        TestSupport.True(settings.Contains("await prepareThemeTransitionAsync()", StringComparison.Ordinal), "settings takeover callback");
        TestSupport.True(Slice(main, "private async Task PrepareForThemeTransitionAsync", "private void CompletePendingNavigationImmediately").Contains("pending.CommitAsync", StringComparison.Ordinal), "pending commit");
    }

    private static string Slice(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        int last = source.IndexOf(end, first, StringComparison.Ordinal);
        TestSupport.True(first >= 0 && last > first, start);
        return source[first..last];
    }

    private static string FindRoot()
    {
        foreach (string origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(origin);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HardwareVision", "MainWindow.xaml")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
