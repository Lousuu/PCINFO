using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionStorageSettingsTests
{
    public static IEnumerable<(string Name, Action Test)> GetTests()
    {
        yield return ("Session storage settings 01 compression is default", CompressionIsDefault);
        yield return ("Session storage settings 02 auto hardware refresh is default", AutoHardwareRefreshIsDefault);
        yield return ("Session storage settings 03 storage and hotplug settings round trip", TestSupport.Run(SettingsRoundTripAsync));
    }

    private static void CompressionIsDefault() => TestSupport.Equal(
        GameSessionFrameStorageMode.CompressedCsv,
        new AppSettings().GameSessionFrameStorageMode,
        "default storage mode");

    private static void AutoHardwareRefreshIsDefault() => TestSupport.True(
        new AppSettings().AutoRefreshHardwareOnDeviceChange,
        "default hotplug setting");

    private static Task SettingsRoundTripAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        SettingsService writer = new(directory);
        AppSettings settings = await writer.LoadAsync();
        settings.GameSessionFrameStorageMode = GameSessionFrameStorageMode.PlainCsv;
        settings.AutoRefreshHardwareOnDeviceChange = false;
        await writer.SaveAsync(settings);

        SettingsService reader = new(directory);
        AppSettings reloaded = await reader.LoadAsync();
        TestSupport.Equal(GameSessionFrameStorageMode.PlainCsv, reloaded.GameSessionFrameStorageMode, "storage mode");
        TestSupport.False(reloaded.AutoRefreshHardwareOnDeviceChange, "hotplug mode");
    });
}
