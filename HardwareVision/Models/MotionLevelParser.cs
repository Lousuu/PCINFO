namespace HardwareVision.Models;

public static class MotionLevelParser
{
    public static MotionLevel Parse(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "FULL" => MotionLevel.Full,
            "STANDARD" => MotionLevel.Standard,
            "REDUCED" => MotionLevel.Reduced,
            "OFF" => MotionLevel.Off,
            _ => MotionLevel.Standard
        };
    }

    public static string ToStorageValue(MotionLevel level)
    {
        return level switch
        {
            MotionLevel.Full => "Full",
            MotionLevel.Standard => "Standard",
            MotionLevel.Reduced => "Reduced",
            MotionLevel.Off => "Off",
            _ => "Standard"
        };
    }

    public static string NormalizeStorageValue(string? value) => ToStorageValue(Parse(value));
}
