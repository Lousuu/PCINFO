using System;

namespace HardwareVision.Models;

public sealed class HardwareMetric
{
	public string Id { get; set; } = string.Empty;


	public string HardwareId { get; set; } = string.Empty;


	public HardwareMetricCategory Category { get; set; } = HardwareMetricCategory.Unknown;


	public string DisplayName { get; set; } = string.Empty;


	public string TechnicalName { get; set; } = string.Empty;


	public string Value { get; set; } = "--";


	public string Unit { get; set; } = string.Empty;


	public string Source { get; set; } = string.Empty;


	public MetricAvailability Availability { get; set; } = MetricAvailability.NotReported;


	public string Description { get; set; } = string.Empty;


	public bool IsImportant { get; set; }

	public bool IsVisible { get; set; } = true;

	public bool ShowWhenUnavailable { get; set; }


	public int DisplayOrder { get; set; }

	public string GroupName { get; set; } = string.Empty;


	public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;

}
