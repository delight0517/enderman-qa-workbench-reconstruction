using System;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

public interface IGameObserver : IDisposable
{
	Task<FabricObservationSnapshot> ObserveAsync(CancellationToken cancellationToken = default(CancellationToken));
}
