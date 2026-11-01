using System;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Adapters;

public interface IGameAdapter : IAsyncDisposable
{
	GameTarget Target { get; }

	AdapterCapabilities Capabilities { get; }

	Task InitializeAsync(string runId, QaPlan plan, CancellationToken cancellationToken);

	Task<AdapterActionResult> ExecuteAsync(QaPlan plan, PlannedStep step, long cancellationGeneration, CancellationToken cancellationToken);

	Task StopAsync(long cancellationGeneration, CancellationToken cancellationToken = default(CancellationToken));
}
