using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IHardwareInfoService
{
	void InvalidateCaches()
	{
	}

	Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<HardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default(CancellationToken));
}
