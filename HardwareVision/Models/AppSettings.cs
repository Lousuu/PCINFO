using System.Collections.Generic;

namespace HardwareVision.Models;

public sealed class AppSettings
{
	public bool AutoStartEnabled { get; set; }

	public bool StartMinimizedToTray { get; set; }

	public bool CloseToTray { get; set; } = true;


	public double RefreshIntervalSeconds { get; set; } = 0.5d;


	public int BackgroundRefreshIntervalSeconds { get; set; } = 10;


	public string Theme { get; set; } = "Dark";


	public string LastSelectedPage { get; set; } = "Dashboard";


	public string? PreferredGpuId { get; set; }

	public string? PreferredDiskId { get; set; }

	public string? PreferredNetworkAdapterId { get; set; }

	public bool ShowVirtualNetworkAdapters { get; set; }

	public Dictionary<string, bool> MetricVisibility { get; set; } = new Dictionary<string, bool>();


	public Dictionary<string, int> MetricDisplayOrder { get; set; } = new Dictionary<string, int>();

}
