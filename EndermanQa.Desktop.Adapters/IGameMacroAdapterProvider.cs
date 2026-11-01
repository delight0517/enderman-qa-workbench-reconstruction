using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Adapters;

public interface IGameMacroAdapterProvider
{
	GameToolDescriptor Descriptor { get; }

	bool Supports(GameAutomationTarget target);

	Task<IGameMacroAdapter> ConnectAsync(GameAutomationTarget target, CancellationToken cancellationToken);
}
