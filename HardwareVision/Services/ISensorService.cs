using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;

namespace HardwareVision.Services;

public interface ISensorService : IDisposable
{
	Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken = default(CancellationToken));
}
