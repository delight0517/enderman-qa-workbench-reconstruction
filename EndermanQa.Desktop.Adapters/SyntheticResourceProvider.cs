using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Adapters;

internal sealed class SyntheticResourceProvider : IGameMacroAdapterProvider
{
	private sealed class SyntheticResourceAdapter : IGameMacroAdapter, IAsyncDisposable
	{
		[CompilerGenerated]
		private GameToolDescriptor _003Cdescriptor_003EP;

		[CompilerGenerated]
		private GameAutomationTarget _003Ctarget_003EP;

		private int _resourceCount;

		public GameToolDescriptor Descriptor => _003Cdescriptor_003EP;

		public GameAutomationTarget Target => _003Ctarget_003EP;

		public GameTargetVerification TargetVerification => new GameTargetVerification(IsVerified: true, _003Ctarget_003EP.GameId, _003Ctarget_003EP.TargetId, "synthetic-fixture", DateTimeOffset.UtcNow, IsTaskOwned: true, IsUnmirrored: false, null, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-fixture:" + _003Ctarget_003EP.TargetId))));

		public SyntheticResourceAdapter(GameToolDescriptor descriptor, GameAutomationTarget target)
		{
			_003Cdescriptor_003EP = descriptor;
			_003Ctarget_003EP = target;
			
		}

		public Task InitializeAsync(GameAutomationMacro macro, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public Task<GameMacroStepResult> ExecuteAsync(GameAutomationMacro macro, GameMacroStep step, long cancellationGeneration, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (step.Action == "demo.add-resource")
			{
				_resourceCount += int.Parse(step.Arguments.GetValueOrDefault("amount", "1"), CultureInfo.InvariantCulture);
			}
			Dictionary<string, string> observations = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["runState"] = "running",
				["resourceCount"] = _resourceCount.ToString(CultureInfo.InvariantCulture)
			};
			return Task.FromResult(new GameMacroStepResult(step.Id, Succeeded: true, "synthetic action", observations, Array.Empty<string>()));
		}

		public Task StopAsync(long cancellationGeneration, CancellationToken cancellationToken = default(CancellationToken))
		{
			return Task.CompletedTask;
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}

	public GameToolDescriptor Descriptor { get; } = new GameToolDescriptor("sample.resource-click", 1, "sample.resource-click", "합성 자원 클릭 예제 (실게임 아님)", "fixture-only", "ready", RequiresOwnedProcess: false, RequiresUnmirroredDisplay: false, new HashSet<string>(new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "demo.begin-run", "demo.add-resource" }), StringComparer.Ordinal), new HashSet<string>(new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "resourceCount", "runState" }), StringComparer.Ordinal));


	public bool Supports(GameAutomationTarget target)
	{
		if (target.GameId == Descriptor.GameId)
		{
			return target.RuntimeKind == "synthetic";
		}
		return false;
	}

	public Task<IGameMacroAdapter> ConnectAsync(GameAutomationTarget target, CancellationToken cancellationToken)
	{
		return Task.FromResult((IGameMacroAdapter)new SyntheticResourceAdapter(Descriptor, target));
	}
}
