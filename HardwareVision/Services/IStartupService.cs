using System.Threading;
using System.Threading.Tasks;

namespace HardwareVision.Services;

public interface IStartupService
{
	string StatusMessage { get; }

	bool IsAdministratorStartupAvailable { get; }

	bool IsUsingFallbackStartup { get; }

	bool IsEnabled();

	void Enable();

	void Disable();

	void SetEnabled(bool enabled);

	Task<bool> IsStartupEnabledAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SetStartupEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default(CancellationToken));
}
