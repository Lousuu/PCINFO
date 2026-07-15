using System.Text.Json;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionSchemaCompatibilityTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Session schema 01 missing JSON version means v1", MissingJsonVersionMeansV1),
        ("Session schema 02 v2 JSON version round trips", V2JsonVersionRoundTrips),
        ("Session schema 03 v1 limit CSV remains readable", TestSupport.Run(V1LimitCsvRemainsReadableAsync)),
        ("Session schema 04 reordered v2 columns remain readable", TestSupport.Run(ReorderedV2ColumnsRemainReadableAsync)),
        ("Session schema 05 missing optional columns use defaults", TestSupport.Run(MissingOptionalColumnsUseDefaultsAsync)),
        ("Session schema 06 future CSV version warns", TestSupport.Run(FutureCsvVersionWarnsAsync)),
        ("Session schema 07 v1 timeline row remains readable", V1TimelineRowRemainsReadable)
    ];

    private static void MissingJsonVersionMeansV1()
    {
        GameSessionSummary summary = TestSupport.NotNull(
            JsonSerializer.Deserialize<GameSessionSummary>("{\"CsvFileName\":\"old.csv\"}"),
            "legacy summary did not deserialize");
        TestSupport.Equal(1, summary.SessionSchemaVersion, "legacy schema version");
    }

    private static void V2JsonVersionRoundTrips()
    {
        GameSessionSummary summary = new() { SessionSchemaVersion = 2, CsvFileName = "new.csv" };
        GameSessionSummary roundTrip = TestSupport.NotNull(
            JsonSerializer.Deserialize<GameSessionSummary>(JsonSerializer.Serialize(summary)),
            "v2 summary did not deserialize");
        TestSupport.Equal(2, roundTrip.SessionSchemaVersion, "v2 schema version");
    }

    private static Task V1LimitCsvRemainsReadableAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid id = Guid.NewGuid();
        string path = Path.Combine(directory, "v1.performance-limits.csv");
        await File.WriteAllLinesAsync(path,
        [
            "CaptureSessionId,CaptureGeneration,EventId,ProcessorType,StartedAt,DurationSeconds,Reasons,TriggerCount,WasMerged,IsActiveFinalState",
            $"{id:N},1,7,Gpu,{DateTimeOffset.UtcNow:O},2.5,Power Limit,3,true,false"
        ]);
        PerformanceLimitCsvReadResult result = await GamePerformanceLimitCsv.ReadAsync(path, id, 1, CancellationToken.None);
        TestSupport.Equal(1, result.Events.Count, "legacy event count");
        TestSupport.Equal(3, result.Events[0].TriggerCount, "legacy trigger count");
    });

    private static Task ReorderedV2ColumnsRemainReadableAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid id = Guid.NewGuid();
        string path = Path.Combine(directory, "reordered.performance-limits.csv");
        await File.WriteAllLinesAsync(path,
        [
            "Reasons,DurationSeconds,StartedAt,ProcessorType,EventId,CaptureGeneration,CaptureSessionId,SessionSchemaVersion,DeviceId",
            $"Thermal,1.25,{DateTimeOffset.UtcNow:O},Gpu,9,2,{id:N},2,pci:0000:01:00.0"
        ]);
        PerformanceLimitCsvReadResult result = await GamePerformanceLimitCsv.ReadAsync(path, id, 2, CancellationToken.None);
        TestSupport.Equal(1, result.Events.Count, "reordered event count");
        TestSupport.Equal("pci:0000:01:00.0", result.Events[0].DeviceId, "reordered device ID");
    });

    private static Task MissingOptionalColumnsUseDefaultsAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid id = Guid.NewGuid();
        string path = Path.Combine(directory, "minimal.performance-limits.csv");
        await File.WriteAllLinesAsync(path,
        [
            "CaptureSessionId,CaptureGeneration,EventId,ProcessorType,StartedAt,DurationSeconds",
            $"{id:N},1,1,Cpu,{DateTimeOffset.UtcNow:O},0.5"
        ]);
        PerformanceLimitCsvReadResult result = await GamePerformanceLimitCsv.ReadAsync(path, id, 1, CancellationToken.None);
        TestSupport.Equal(1, result.Events[0].TriggerCount, "missing trigger-count default");
        TestSupport.True(result.Events[0].DeviceId is null, "missing device ID default");
    });

    private static Task FutureCsvVersionWarnsAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid id = Guid.NewGuid();
        string path = Path.Combine(directory, "future.performance-limits.csv");
        await File.WriteAllLinesAsync(path,
        [
            "SessionSchemaVersion,CaptureSessionId,CaptureGeneration,EventId,ProcessorType,StartedAt,DurationSeconds",
            $"99,{id:N},1,1,Gpu,{DateTimeOffset.UtcNow:O},1"
        ]);
        PerformanceLimitCsvReadResult result = await GamePerformanceLimitCsv.ReadAsync(path, id, 1, CancellationToken.None);
        TestSupport.Equal(1, result.Events.Count, "known future-version fields were not read");
        TestSupport.True(result.Warnings.Count > 0, "future-version warning missing");
    });

    private static void V1TimelineRowRemainsReadable()
    {
        Guid id = Guid.NewGuid();
        const string header = "CaptureSessionId,CaptureGeneration,Timestamp,ElapsedSeconds,DeviceType,DeviceId,DeviceName,GpuCoreClockMHz";
        string row = $"{id:N},1,{DateTimeOffset.UtcNow:O},3.5,Gpu,/gpu/0,RTX,2100";
        bool parsed = GameHardwareTimelineCsv.TryParse(
            row,
            SessionCsvColumnMap.Create(header),
            id,
            1,
            out GameHardwareTimelineSample? sample);
        TestSupport.True(parsed, "legacy timeline row rejected");
        TestSupport.Nearly(2100d, sample?.GpuCoreClockMHz, "legacy GPU clock");
    }
}
