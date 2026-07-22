using System.Text.Json;
using HardwareVision.Models;

namespace HardwareVision.Tests;

internal static class SessionReportPresentationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Report presentation 01 UI displays maximum FPS", UiDisplaysMaximumFps),
        ("Report presentation 02 old maximum labels are absent", OldMaximumLabelsAreAbsent),
        ("Report presentation 03 raw maximum is not user visible", RawMaximumIsNotUserVisible),
        ("Report presentation 04 frame diagnostics panel is absent", DiagnosticsPanelIsAbsent),
        ("Report presentation 05 module details wording is absent", ModuleDetailsWordingIsAbsent),
        ("Report presentation 06 elapsed field is not user visible", ElapsedFieldIsNotUserVisible),
        ("Report presentation 07 session internals are not user visible", SessionInternalsAreNotUserVisible),
        ("Report presentation 08 report headings use professional names", ReportHeadingsUseProfessionalNames),
        ("Report presentation 09 schema v4 sustained field remains readable", SchemaV4FieldRemainsReadable),
        ("Report presentation 10 user-visible resources contain no known mojibake", UserVisibleResourcesContainNoMojibake),
        ("Report presentation 11 navigation uses professional terminology", NavigationUsesProfessionalTerminology),
        ("Report presentation 12 network heading uses professional terminology", NetworkHeadingUsesProfessionalTerminology)
    ];

    private static string ReportXaml => Read("HardwareVision", "Views", "GameSessionReportView.xaml")
        + Read("HardwareVision", "Views", "GameSessionReport", "ClassicGameSessionReportLayout.xaml")
        + Read("HardwareVision", "Views", "GameSessionReport", "TraceworkGameSessionReportLayout.xaml");
    private static string ReportViewModel => Read("HardwareVision", "ViewModels", "GameSessionReportViewModel.cs");
    private static string UserDocumentation => Read("README.md") + Read("HANDOFF.md");

    private static void UiDisplaysMaximumFps() =>
        TestSupport.True(ReportViewModel.Contains("\"最大 FPS\"", StringComparison.Ordinal), "maximum FPS label");

    private static void OldMaximumLabelsAreAbsent()
    {
        string source = ReportXaml + ReportViewModel + UserDocumentation;
        TestSupport.False(source.Contains("稳健最大 FPS", StringComparison.OrdinalIgnoreCase), "old maximum label");
        TestSupport.False(source.Contains("稳健峰值 FPS", StringComparison.OrdinalIgnoreCase), "old peak label");
        TestSupport.False(source.Contains("Robust Maximum FPS", StringComparison.OrdinalIgnoreCase), "English robust label");
    }

    private static void RawMaximumIsNotUserVisible()
    {
        string source = ReportXaml + ReportViewModel + UserDocumentation;
        TestSupport.False(source.Contains("原始单帧最大 FPS", StringComparison.Ordinal), "raw maximum label");
    }

    private static void DiagnosticsPanelIsAbsent()
    {
        TestSupport.False(ReportXaml.Contains("逐帧质量诊断", StringComparison.Ordinal), "diagnostics title");
        TestSupport.False(ReportXaml.Contains("ValidationMetrics", StringComparison.Ordinal), "stale diagnostics binding");
        TestSupport.False(ReportViewModel.Contains("ValidationMetrics", StringComparison.Ordinal), "stale diagnostics collection");
    }

    private static void ModuleDetailsWordingIsAbsent()
    {
        string memory = Read("HardwareVision", "Views", "MemoryView.xaml")
            + Read("HardwareVision", "Views", "Memory", "ClassicMemoryLayout.xaml");
        TestSupport.False(memory.Contains("模块细节", StringComparison.Ordinal), "module details wording");
        TestSupport.True(memory.Contains("详细信息", StringComparison.Ordinal), "detail wording");
    }

    private static void ElapsedFieldIsNotUserVisible()
    {
        TestSupport.False(ReportXaml.Contains("ElapsedSeconds", StringComparison.Ordinal), "ElapsedSeconds XAML");
        TestSupport.True(ReportXaml.Contains("横轴为会话经过时间", StringComparison.Ordinal), "localized elapsed description");
    }

    private static void SessionInternalsAreNotUserVisible()
    {
        TestSupport.False(ReportXaml.Contains("SessionId", StringComparison.Ordinal), "SessionId XAML");
        TestSupport.False(ReportXaml.Contains("generation", StringComparison.OrdinalIgnoreCase), "generation XAML");
        TestSupport.False(ReportViewModel.Contains("SessionId：", StringComparison.Ordinal), "SessionId ViewModel text");
    }

    private static void ReportHeadingsUseProfessionalNames()
    {
        string source = ReportXaml;
        string[] headings = ["性能统计", "会话性能曲线", "CPU / GPU 性能限制统计", "性能限制事件", "会话硬件信息", "数据完整性"];
        foreach (string heading in headings)
            TestSupport.True(source.Contains(heading, StringComparison.Ordinal), $"missing heading {heading}");
    }

    private static void SchemaV4FieldRemainsReadable()
    {
        GameSessionSummary? summary = JsonSerializer.Deserialize<GameSessionSummary>(
            "{\"SessionSchemaVersion\":4,\"SustainedMaximumFps\":240}");
        TestSupport.Nearly(240d, summary?.SustainedMaximumFps, "schema v4 maximum field");
    }

    private static void UserVisibleResourcesContainNoMojibake()
    {
        string root = Environment.CurrentDirectory;
        string source = string.Concat(
            Directory.GetFiles(Path.Combine(root, "HardwareVision", "Views"), "*.xaml").Select(File.ReadAllText))
            + string.Concat(Directory.GetFiles(Path.Combine(root, "HardwareVision", "ViewModels"), "*.cs").Select(File.ReadAllText))
            + Read("README.md")
            + Read("HANDOFF.md");
        string[] fragments = ["�", "纭", "鏃", "锛", "閿"];
        foreach (string fragment in fragments)
            TestSupport.False(source.Contains(fragment, StringComparison.Ordinal), $"mojibake fragment {fragment}");
    }

    private static void NavigationUsesProfessionalTerminology()
    {
        string source = Read("HardwareVision", "ViewModels", "MainViewModel.cs");
        TestSupport.True(source.Contains("容量与内存模组", StringComparison.Ordinal), "memory navigation wording");
        TestSupport.True(source.Contains("网络适配器与流量", StringComparison.Ordinal), "network navigation wording");
        TestSupport.True(source.Contains("应用设置与诊断", StringComparison.Ordinal), "settings navigation wording");
        TestSupport.False(source.Contains("网卡与吞吐", StringComparison.Ordinal), "informal network wording");
    }

    private static void NetworkHeadingUsesProfessionalTerminology()
    {
        string source = Read("HardwareVision", "Views", "NetworkView.xaml")
            + Read("HardwareVision", "Views", "Network", "ClassicNetworkLayout.xaml");
        TestSupport.True(source.Contains("网络适配器信息", StringComparison.Ordinal), "network information heading");
        TestSupport.False(source.Contains("网络细节", StringComparison.Ordinal), "informal network detail heading");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Environment.CurrentDirectory, .. parts]));
}
