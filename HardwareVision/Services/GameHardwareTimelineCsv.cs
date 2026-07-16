using System.Globalization;
using System.IO;
using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class GameHardwareTimelineCsv
{
    public const string Header = "SessionSchemaVersion,CaptureSessionId,CaptureGeneration,Timestamp,ElapsedSeconds,DeviceType,DeviceId,DeviceName,CpuAverageCoreClockMHz,CpuEffectiveClockMHz,CpuMaximumCoreClockMHz,CpuLoadPercent,CpuTemperatureCelsius,CpuPackagePowerWatts,CpuLimitActive,CpuLimitReasonCount,CpuLimitReasons,CpuLimitSupportStatus,GpuCoreClockMHz,GpuMemoryClockMHz,GpuLoadPercent,GpuTemperatureCelsius,GpuHotSpotTemperatureCelsius,GpuBoardPowerWatts,GpuLimitActive,GpuLimitReasonCount,GpuLimitReasons,GpuLimitSupportStatus,MemoryUsedBytes,MemoryLoadPercent";

    public static string GetPath(string frameCsvPath)
    {
        return GameSessionFileNaming.GetRelatedPath(frameCsvPath, ".hardware-timeline.csv");
    }

    public static string GetPartialPath(string finalPath) =>
        Path.ChangeExtension(finalPath, ".partial.csv");

    public static string Format(GameHardwareTimelineSample sample)
    {
        string[] values =
        [
            "2",
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
        return TryParse(
            line,
            SessionCsvColumnMap.Create(Header),
            expectedSessionId,
            expectedGeneration,
            out sample);
    }

    public static bool TryParse(
        string line,
        SessionCsvColumnMap columns,
        Guid? expectedSessionId,
        int? expectedGeneration,
        out GameHardwareTimelineSample? sample)
    {
        sample = null;
        try
        {
            IReadOnlyList<string> fields = PresentMonCsvParser.ParseColumns(line);
            Guid sessionId = Guid.Parse(columns.Get(fields, "CaptureSessionId"));
            int generation = int.Parse(columns.Get(fields, "CaptureGeneration"), NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (expectedSessionId.HasValue && expectedSessionId.Value != sessionId
                || expectedGeneration.HasValue && expectedGeneration.Value != generation)
            {
                return false;
            }

            sample = new GameHardwareTimelineSample
            {
                CaptureSessionId = sessionId,
                CaptureGeneration = generation,
                Timestamp = DateTimeOffset.Parse(columns.Get(fields, "Timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ElapsedSeconds = ParseRequired(columns.Get(fields, "ElapsedSeconds")),
                DeviceType = Enum.Parse<GameTimelineDeviceType>(columns.Get(fields, "DeviceType"), true),
                DeviceId = columns.Get(fields, "DeviceId", "DeviceKey"),
                DeviceName = columns.Get(fields, "DeviceName"),
                CpuAverageCoreClockMHz = ParseOptional(columns.Get(fields, "CpuAverageCoreClockMHz")),
                CpuEffectiveClockMHz = ParseOptional(columns.Get(fields, "CpuEffectiveClockMHz")),
                CpuMaximumCoreClockMHz = ParseOptional(columns.Get(fields, "CpuMaximumCoreClockMHz")),
                CpuLoadPercent = ParseOptional(columns.Get(fields, "CpuLoadPercent")),
                CpuTemperatureCelsius = ParseOptional(columns.Get(fields, "CpuTemperatureCelsius")),
                CpuPackagePowerWatts = ParseOptional(columns.Get(fields, "CpuPackagePowerWatts")),
                CpuLimitActive = ParseOptionalBool(columns.Get(fields, "CpuLimitActive")),
                CpuLimitReasonCount = ParseOptionalInt(columns.Get(fields, "CpuLimitReasonCount")),
                CpuLimitReasons = GamePerformanceLimitCsv.SplitValues(columns.Get(fields, "CpuLimitReasons")),
                CpuLimitSupportStatus = ParseOptionalEnum<PerformanceLimitSupportStatus>(columns.Get(fields, "CpuLimitSupportStatus")),
                GpuCoreClockMHz = ParseOptional(columns.Get(fields, "GpuCoreClockMHz")),
                GpuMemoryClockMHz = ParseOptional(columns.Get(fields, "GpuMemoryClockMHz")),
                GpuLoadPercent = ParseOptional(columns.Get(fields, "GpuLoadPercent")),
                GpuTemperatureCelsius = ParseOptional(columns.Get(fields, "GpuTemperatureCelsius")),
                GpuHotSpotTemperatureCelsius = ParseOptional(columns.Get(fields, "GpuHotSpotTemperatureCelsius")),
                GpuBoardPowerWatts = ParseOptional(columns.Get(fields, "GpuBoardPowerWatts")),
                GpuLimitActive = ParseOptionalBool(columns.Get(fields, "GpuLimitActive")),
                GpuLimitReasonCount = ParseOptionalInt(columns.Get(fields, "GpuLimitReasonCount")),
                GpuLimitReasons = GamePerformanceLimitCsv.SplitValues(columns.Get(fields, "GpuLimitReasons")),
                GpuLimitSupportStatus = ParseOptionalEnum<PerformanceLimitSupportStatus>(columns.Get(fields, "GpuLimitSupportStatus")),
                MemoryUsedBytes = ParseOptional(columns.Get(fields, "MemoryUsedBytes")),
                MemoryLoadPercent = ParseOptional(columns.Get(fields, "MemoryLoadPercent"))
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
