using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

public interface ISuiteLifecycleAdapter
{
	Task<string> PrepareSuiteAsync(CancellationToken cancellationToken);
}
