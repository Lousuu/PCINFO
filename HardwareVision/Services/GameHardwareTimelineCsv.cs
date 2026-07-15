using System.Globalization;
using System.IO;
using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class GameHardwareTimelineCsv
{
    public const string Header = "CaptureSessionId,CaptureGeneration,Timestamp,ElapsedSeconds,DeviceType,DeviceId,DeviceName,CpuAverageCoreClockMHz,CpuEffectiveClockMHz,CpuMaximumCoreClockMHz,CpuLoadPercent,CpuTemperatureCelsius,CpuPackagePowerWatts,CpuLimitActive,CpuLimitReasonCount,CpuLimitReasons,CpuLimitSupportStatus,GpuCoreClockMHz,GpuMemoryClockMHz,GpuLoadPercent,GpuTemperatureCelsius,GpuHotSpotTemperatureCelsius,GpuBoardPowerWatts,GpuLimitActive,GpuLimitReasonCount,GpuLimitReasons,GpuLimitSupportStatus,MemoryUsedBytes,MemoryLoadPercent";

    public static string GetPath(string frameCsvPath)
    {
        string directory = Path.GetDirectoryName(frameCsvPath)!;
        string baseName = Path.GetFileNameWithoutExtension(frameCsvPath);
        return Path.Combine(directory, baseName + ".hardware-timeline.csv");
    }

    public static string GetPartialPath(string finalPath) =>
        Path.ChangeExtension(finalPath, ".partial.csv");

    public static string Format(GameHardwareTimelineSample sample)
    {
        string[] values =
        [
            sample.CaptureSessionId.ToString("N", CultureInfo.InvariantCulture),
            sample.CaptureGeneration.ToString(CultureInfo.InvariantCulture),
            sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            Number(sample.ElapsedSeconds),
            sample.DeviceType.ToString(),
            sample.DeviceId,
            sample.DeviceName,
            Number(sample.CpuAverageCoreClockMHz),
            Number(sample.CpuEffectiveClockMHz),
            Number(sample.CpuMaximumCoreClockMHz),
            Number(sample.CpuLoadPercent),
            Number(sample.CpuTemperatureCelsius),
            Number(sample.CpuPackagePowerWatts),
            Boolean(sample.CpuLimitActive),
            Integer(sample.CpuLimitReasonCount),
            GamePerformanceLimitCsv.JoinValues(sample.CpuLimitReasons),
            sample.CpuLimitSupportStatus?.ToString() ?? string.Empty,
            Number(sample.GpuCoreClockMHz),
            Number(sample.GpuMemoryClockMHz),
            Number(sample.GpuLoadPercent),
            Number(sample.GpuTemperatureCelsius),
            Number(sample.GpuHotSpotTemperatureCelsius),
            Number(sample.GpuBoardPowerWatts),
            Boolean(sample.GpuLimitActive),
            Integer(sample.GpuLimitReasonCount),
            GamePerformanceLimitCsv.JoinValues(sample.GpuLimitReasons),
            sample.GpuLimitSupportStatus?.ToString() ?? string.Empty,
            Number(sample.MemoryUsedBytes),
            Number(sample.MemoryLoadPercent)
        ];
        return string.Join(',', values.Select(GameCsvFormatting.Escape));
    }

    public static bool TryParse(
        string line,
        Guid? expectedSessionId,
        int? expectedGeneration,
        out GameHardwareTimelineSample? sample)
    {
        sample = null;
        try
        {
            IReadOnlyList<string> fields = PresentMonCsvParser.ParseColumns(line);
            if (fields.Count < 29) return false;
            Guid sessionId = Guid.Parse(fields[0]);
            int generation = int.Parse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (expectedSessionId.HasValue && expectedSessionId.Value != sessionId
                || expectedGeneration.HasValue && expectedGeneration.Value != generation)
            {
                return false;
            }

            sample = new GameHardwareTimelineSample
            {
                CaptureSessionId = sessionId,
                CaptureGeneration = generation,
                Timestamp = DateTimeOffset.Parse(fields[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ElapsedSeconds = ParseRequired(fields[3]),
                DeviceType = Enum.Parse<GameTimelineDeviceType>(fields[4], true),
                DeviceId = fields[5],
                DeviceName = fields[6],
                CpuAverageCoreClockMHz = ParseOptional(fields[7]),
                CpuEffectiveClockMHz = ParseOptional(fields[8]),
                CpuMaximumCoreClockMHz = ParseOptional(fields[9]),
                CpuLoadPercent = ParseOptional(fields[10]),
                CpuTemperatureCelsius = ParseOptional(fields[11]),
                CpuPackagePowerWatts = ParseOptional(fields[12]),
                CpuLimitActive = ParseOptionalBool(fields[13]),
                CpuLimitReasonCount = ParseOptionalInt(fields[14]),
                CpuLimitReasons = GamePerformanceLimitCsv.SplitValues(fields[15]),
                CpuLimitSupportStatus = ParseOptionalEnum<PerformanceLimitSupportStatus>(fields[16]),
                GpuCoreClockMHz = ParseOptional(fields[17]),
                GpuMemoryClockMHz = ParseOptional(fields[18]),
                GpuLoadPercent = ParseOptional(fields[19]),
                GpuTemperatureCelsius = ParseOptional(fields[20]),
                GpuHotSpotTemperatureCelsius = ParseOptional(fields[21]),
                GpuBoardPowerWatts = ParseOptional(fields[22]),
                GpuLimitActive = ParseOptionalBool(fields[23]),
                GpuLimitReasonCount = ParseOptionalInt(fields[24]),
                GpuLimitReasons = GamePerformanceLimitCsv.SplitValues(fields[25]),
                GpuLimitSupportStatus = ParseOptionalEnum<PerformanceLimitSupportStatus>(fields[26]),
                MemoryUsedBytes = ParseOptional(fields[27]),
                MemoryLoadPercent = ParseOptional(fields[28])
            };
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private static string Number(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
        : string.Empty;

    private static string Boolean(bool? value) => value.HasValue ? (value.Value ? "true" : "false") : string.Empty;
    private static string Integer(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static double ParseRequired(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static double? ParseOptional(string value) => string.IsNullOrWhiteSpace(value) ? null : ParseRequired(value);
    private static int? ParseOptionalInt(string value) => string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static bool? ParseOptionalBool(string value) => string.IsNullOrWhiteSpace(value) ? null : bool.Parse(value);
    private static T? ParseOptionalEnum<T>(string value) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<T>(value, true);
}
