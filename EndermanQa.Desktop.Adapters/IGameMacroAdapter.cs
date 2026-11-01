using System;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Adapters;

public interface IGameMacroAdapter : IAsyncDisposable
{
	GameToolDescriptor Descriptor { get; }

	GameAutomationTarget Target { get; }

	GameTargetVerification TargetVerification { get; }

	Task InitializeAsync(GameAutomationMacro macro, CancellationToken cancellationToken);

	Task<GameMacroStepResult> ExecuteAsync(GameAutomationMacro macro, GameMacroStep step, long cancellationGeneration, CancellationToken cancellationToken);

	Task StopAsync(long cancellationGeneration, CancellationToken cancellationToken = default(CancellationToken));
}
