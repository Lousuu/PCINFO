using System;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;

namespace HardwareVision.Services;

public interface ISettingsService
{
	Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default(CancellationToken));

	Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<AppSettings> UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default(CancellationToken));
}
