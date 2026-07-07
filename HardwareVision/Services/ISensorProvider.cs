using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;

namespace HardwareVision.Services;

public interface ISensorProvider
{
	string Name { get; }

	bool IsAvailable { get; }

	int Priority { get; }

	Task InitializeAsync(CancellationToken cancellationToken = default);

	Task<IReadOnlyList<SensorReading>> GetReadingsAsync(CancellationToken cancellationToken = default);
}
