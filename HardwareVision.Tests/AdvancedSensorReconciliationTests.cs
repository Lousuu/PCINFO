using System.Collections.Specialized;
using HardwareVision.Collections;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class AdvancedSensorReconciliationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Advanced reconcile 01 initial 500 uses one reset", InitialReset),
        ("Advanced reconcile 02 identical refresh has no collection change", IdenticalNoChange),
        ("Advanced reconcile 03 value update reuses object", ValueUpdateReusesObject),
        ("Advanced reconcile 04 value update avoids row rebuild", ValueUpdateAvoidsRebuild),
        ("Advanced reconcile 05 deletion produces final count", DeleteRow),
        ("Advanced reconcile 06 addition produces final count", AddRow),
        ("Advanced reconcile 07 reorder preserves requested order", ReorderRows),
        ("Advanced reconcile 08 dictionary path replaces linear lookup", DictionaryPath),
        ("Advanced reconcile 09 inactive page blocks apply", InactiveGuard),
        ("Advanced reconcile 10 dispose blocks apply", DisposeGuard),
        ("Advanced reconcile 11 latest owner wins", LatestOwnerGuard),
        ("Advanced reconcile 12 DataGrid virtualization contract", VirtualizationContract),
        ("Advanced reconcile 13 page owns one outer ScrollViewer", OneOuterScrollViewer),
        ("Advanced reconcile 14 refresh avoids notification storm", NoNotificationStorm),
        ("Advanced reconcile 15 status reflects applied result", StatusContract)
    ];

    private static DetailSensorRowSnapshot Snapshot(int id, string? readout = null) =>
        new($"sensor-{id}", $"Sensor {id}", "Temperature", $"Sensor {id}", "Temperature", readout ?? id.ToString(), readout ?? id.ToString(), "℃", "test", "可用", true, null, null, null, null);

    private static DetailSensorRowSnapshot[] Snapshots(int count) => Enumerable.Range(0, count).Select(index => Snapshot(index)).ToArray();

    private static void InitialReset()
    {
        BulkObservableCollection<DetailSensorRowViewModel> rows = new();
        int notifications = 0;
        rows.CollectionChanged += (_, _) => notifications++;
        SensorRowReconciliationResult result = AdvancedSensorRowReconciler.Apply(rows, Snapshots(500));
        TestSupport.Equal(500, rows.Count, "row count");
        TestSupport.Equal(1, notifications, "reset count");
        TestSupport.True(result.CollectionReset, "reset result");
    }

    private static void IdenticalNoChange()
    {
        BulkObservableCollection<DetailSensorRowViewModel> rows = new();
        DetailSensorRowSnapshot[] snapshots = Snapshots(500);
        AdvancedSensorRowReconciler.Apply(rows, snapshots);
        int notifications = 0;
        rows.CollectionChanged += (_, _) => notifications++;
        SensorRowReconciliationResult result = AdvancedSensorRowReconciler.Apply(rows, snapshots);
        TestSupport.Equal(0, notifications, "notifications");
        TestSupport.False(result.CollectionReset, "no reset");
        TestSupport.Equal(500, result.ReusedCount, "reused");
    }

    private static void ValueUpdateReusesObject()
    {
        BulkObservableCollection<DetailSensorRowViewModel> rows = new();
        AdvancedSensorRowReconciler.Apply(rows, [Snapshot(1, "10")]);
        DetailSensorRowViewModel original = rows[0];
        SensorRowReconciliationResult result = AdvancedSensorRowReconciler.Apply(rows, [Snapshot(1, "11")]);
        TestSupport.True(ReferenceEquals(original, rows[0]), "same row reference");
        TestSupport.Equal("11", rows[0].Readout, "updated value");
        TestSupport.Equal(1, result.UpdatedCount, "updated count");
    }

    private static void ValueUpdateAvoidsRebuild()
    {
        BulkObservableCollection<DetailSensorRowViewModel> rows = new();
        DetailSensorRowSnapshot[] snapshots = Snapshots(500);
        AdvancedSensorRowReconciler.Apply(rows, snapshots);
        DetailSensorRowViewModel[] references = rows.ToArray();
        snapshots[250] = Snapshot(250, "changed");
        SensorRowReconciliationResult result = AdvancedSensorRowReconciler.Apply(rows, snapshots);
        TestSupport.True(references.Select((row, index) => ReferenceEquals(row, rows[index])).All(value => value), "all references reused");
        TestSupport.Equal(0, result.CreatedCount, "created count");
    }

    private static void DeleteRow()
    {
        BulkObservableCollection<DetailSensorRowViewModel> rows = new();
        AdvancedSensorRowReconciler.Apply(rows, Snapshots(3));
        AdvancedSensorRowReconciler.Apply(rows, [Snapshot(0), Snapshot(2)]);
        TestSupport.Equal(2, rows.Count, "delete count");
        TestSupport.Equal("sensor-2", rows[1].Id, "delete order");
    }

    private static void AddRow()
    {
        BulkObservableCollection<DetailSensorRowViewModel> rows = new();
        AdvancedSensorRowReconciler.Apply(rows, [Snapshot(0)]);
        AdvancedSensorRowReconciler.Apply(rows, [Snapshot(0), Snapshot(1)]);
        TestSupport.Equal(2, rows.Count, "add count");
    }

    private static void ReorderRows()
    {
        BulkObservableCollection<DetailSensorRowViewModel> rows = new();
        AdvancedSensorRowReconciler.Apply(rows, Snapshots(3));
        DetailSensorRowViewModel originalLast = rows[2];
        AdvancedSensorRowReconciler.Apply(rows, [Snapshot(2), Snapshot(0), Snapshot(1)]);
        TestSupport.Equal("sensor-2", rows[0].Id, "reordered ID");
        TestSupport.True(ReferenceEquals(originalLast, rows[0]), "reordered reuse");
    }

    private static void DictionaryPath()
    {
        string source = Read("HardwareVision", "ViewModels", "ViewModelHelpers.cs") + Read("HardwareVision", "ViewModels", "AdvancedSensorsViewModel.cs");
        TestSupport.True(source.Contains("Dictionary<string, DetailSensorRowViewModel>", StringComparison.Ordinal), "dictionary");
        TestSupport.False(source.Contains("FindSensorRowIndex", StringComparison.Ordinal), "linear sensor lookup removed");
    }

    private static void InactiveGuard() { string source = ReadVm(); TestSupport.True(source.Contains("&& isActive", StringComparison.Ordinal), "inactive event gate"); TestSupport.True(source.Contains("&& isActive", StringComparison.Ordinal) || source.Contains("!isActive", StringComparison.Ordinal), "inactive apply guard"); }
    private static void DisposeGuard() => TestSupport.True(ReadVm().Contains("!isDisposed", StringComparison.Ordinal), "dispose guard");
    private static void LatestOwnerGuard() => TestSupport.True(ReadVm().Contains("ReferenceEquals(Volatile.Read(ref refreshCancellation), owner)", StringComparison.Ordinal), "latest owner");

    private static void VirtualizationContract()
    {
        string xaml = Read("HardwareVision", "Views", "AdvancedSensors", "TraceworkAdvancedSensorsLayout.xaml");
        foreach (string value in new[] { "EnableRowVirtualization=\"True\"", "EnableColumnVirtualization=\"True\"", "VirtualizationMode=\"Recycling\"", "ScrollUnit=\"Item\"", "RowHeight=\"34\"" })
            TestSupport.True(xaml.Contains(value, StringComparison.Ordinal), value);
    }

    private static void OneOuterScrollViewer()
    {
        string xaml = Read("HardwareVision", "Views", "AdvancedSensors", "TraceworkAdvancedSensorsLayout.xaml");
        TestSupport.Equal(1, TraceworkPilotSource.Count(xaml, "<ScrollViewer"), "outer scroll viewers");
        TestSupport.True(xaml.Contains("x:Name=\"AdvancedSensorsPageScrollViewer\"", StringComparison.Ordinal), "named page scroll owner");
        TestSupport.True(xaml.Contains("NestedScrollViewerBehavior.ForwardAtBoundary=\"True\"", StringComparison.Ordinal), "DataGrid boundary forwarding");
    }

    private static void NoNotificationStorm()
    {
        BulkObservableCollection<DetailSensorRowViewModel> rows = new();
        int notifications = 0;
        rows.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) notifications++; };
        AdvancedSensorRowReconciler.Apply(rows, Snapshots(500));
        AdvancedSensorRowReconciler.Apply(rows, Snapshots(500));
        TestSupport.Equal(1, notifications, "total resets");
    }

    private static void StatusContract()
    {
        string source = ReadVm();
        TestSupport.True(source.Contains("BuildStatusText(readings.Length, rows.Length)", StringComparison.Ordinal), "applied counts");
        TestSupport.True(source.Contains("MaxVisibleRows = 500", StringComparison.Ordinal), "maximum rows");
        TestSupport.True(source.Contains("TimeSpan.FromSeconds(3)", StringComparison.Ordinal), "throttle");
    }

    private static string ReadVm() => Read("HardwareVision", "ViewModels", "AdvancedSensorsViewModel.cs");
    private static string Read(params string[] parts) => TraceworkPilotSource.Read(parts);
}
