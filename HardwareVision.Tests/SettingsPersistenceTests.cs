using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class SettingsPersistenceTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Settings persistence 01 default is 0.5 seconds", TestSupport.Run(DefaultIsHalfSecondAsync)),
        ("Settings persistence 02 two seconds round trips", TestSupport.Run(TwoSecondsRoundTripsAsync)),
        ("Settings persistence 03 view model preserves loaded interval", ViewModelPreservesLoadedInterval),
        ("Settings persistence 04 polling receives loaded interval", PollingReceivesLoadedInterval),
        ("Settings persistence 05 NaN falls back", () => Normalize(double.NaN, 0.5d)),
        ("Settings persistence 06 infinity falls back", () => Normalize(double.PositiveInfinity, 0.5d)),
        ("Settings persistence 07 lower bound clamps", () => Normalize(0.1d, 0.5d)),
        ("Settings persistence 08 upper bound clamps", () => Normalize(31d, 30d)),
        ("Settings persistence 09 half-second step rounds", () => Normalize(2.24d, 2d)),
        ("Settings persistence 10 background interval stays independent", BackgroundIntervalStaysIndependent)
    ];

    private static Task DefaultIsHalfSecondAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        SettingsService service = new(directory);
        AppSettings settings = await service.LoadAsync();
        TestSupport.Nearly(0.5d, settings.RefreshIntervalSeconds, "default foreground interval");
    });

    private static Task TwoSecondsRoundTripsAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        SettingsService writer = new(directory);
        AppSettings settings = await writer.LoadAsync();
        settings.RefreshIntervalSeconds = 2d;
        await writer.SaveAsync(settings);

        SettingsService reader = new(directory);
        AppSettings reloaded = await reader.LoadAsync();
        TestSupport.Nearly(2d, reloaded.RefreshIntervalSeconds, "persisted foreground interval");
    });

    private static void ViewModelPreservesLoadedInterval()
    {
        TestSupport.InTemporaryDirectory(directory =>
        {
            AppSettings settings = new() { RefreshIntervalSeconds = 2d, BackgroundRefreshIntervalSeconds = 17 };
            SettingsService settingsService = new(Path.Combine(directory, "settings"));
            CountingSensorService sensors = new();
            using PollingService polling = new(sensors, settings);
            using CsvGameSessionRecorder recorder = new(Path.Combine(directory, "sessions"), 8);
            using SettingsViewModel viewModel = new(
                settings,
                settingsService,
                new NoopStartupService(),
                polling,
                new SensorDiagnosticService(),
                Dispatcher.CurrentDispatcher,
                () => { },
                recorder);
            TestSupport.Nearly(2d, viewModel.RefreshIntervalSeconds, "view model interval");
            TestSupport.Nearly(2d, settings.RefreshIntervalSeconds, "model interval");
        });
    }

    private static void PollingReceivesLoadedInterval()
    {
        AppSettings settings = new() { RefreshIntervalSeconds = 2d };
        using PollingService polling = new(new CountingSensorService(), settings);
        TestSupport.Equal(TimeSpan.FromSeconds(2d), polling.ForegroundIntervalForDiagnostics, "polling interval");
    }

    private static void Normalize(double input, double expected) =>
        TestSupport.Nearly(
            expected,
            SettingsService.NormalizeForegroundRefreshInterval(input),
            $"normalized value for {input}");

    private static void BackgroundIntervalStaysIndependent()
    {
        AppSettings normalized = SettingsService.Normalize(new AppSettings
        {
            RefreshIntervalSeconds = 2d,
            BackgroundRefreshIntervalSeconds = 23
        });
        TestSupport.Nearly(2d, normalized.RefreshIntervalSeconds, "foreground remains two seconds");
        TestSupport.Equal(23, normalized.BackgroundRefreshIntervalSeconds, "background remains independent");
    }
}
