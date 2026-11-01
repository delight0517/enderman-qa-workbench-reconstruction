using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Adapters;

public sealed class GameMacroAdapterRegistry
{
	private sealed record Registration(IGameMacroAdapterProvider Provider, GameToolDescriptor PinnedDescriptor);

	private sealed class PinnedAdapter : IGameMacroAdapter, IAsyncDisposable
	{
		[CompilerGenerated]
		private IGameMacroAdapterProvider _003Cprovider_003EP;

		[CompilerGenerated]
		private IGameMacroAdapter _003Cinner_003EP;

		[CompilerGenerated]
		private GameToolDescriptor _003CpinnedDescriptor_003EP;

		private readonly GameAutomationTarget _target;

		public GameToolDescriptor Descriptor => _003CpinnedDescriptor_003EP;

		public GameAutomationTarget Target => _target;

		public GameTargetVerification TargetVerification => _003Cinner_003EP.TargetVerification;

		public PinnedAdapter(IGameMacroAdapterProvider provider, IGameMacroAdapter inner, GameToolDescriptor pinnedDescriptor)
		{
			_003Cprovider_003EP = provider;
			_003Cinner_003EP = inner;
			_003CpinnedDescriptor_003EP = pinnedDescriptor;
			_target = _003Cinner_003EP.Target;
			
		}

		private void EnsurePinned()
		{
			if (!SameDescriptor(_003Cprovider_003EP.Descriptor, _003CpinnedDescriptor_003EP) || !SameDescriptor(_003Cinner_003EP.Descriptor, _003CpinnedDescriptor_003EP))
			{
				throw new InvalidOperationException("Adapter capabilities changed during a run; further actions are blocked.");
			}
			if (!GameMacroTargetSafety.IsLiveProcessStillBound(_003Cinner_003EP.TargetVerification, _003CpinnedDescriptor_003EP, _target))
			{
				throw new InvalidOperationException("The verified game process exited or its path/start-time identity changed; further actions are blocked.");
			}
		}

		public Task InitializeAsync(GameAutomationMacro macro, CancellationToken cancellationToken)
		{
			EnsurePinned();
			return _003Cinner_003EP.InitializeAsync(macro, cancellationToken);
		}

		public Task<GameMacroStepResult> ExecuteAsync(GameAutomationMacro macro, GameMacroStep step, long cancellationGeneration, CancellationToken cancellationToken)
		{
			EnsurePinned();
			return _003Cinner_003EP.ExecuteAsync(macro, step, cancellationGeneration, cancellationToken);
		}

		public Task StopAsync(long cancellationGeneration, CancellationToken cancellationToken = default(CancellationToken))
		{
			return _003Cinner_003EP.StopAsync(cancellationGeneration, cancellationToken);
		}

		public ValueTask DisposeAsync()
		{
			return _003Cinner_003EP.DisposeAsync();
		}
	}

	private readonly Dictionary<string, Registration> _providers = new Dictionary<string, Registration>(StringComparer.Ordinal);

	private static readonly Regex Identifier = new Regex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex CapabilityIdentifier = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly HashSet<string> AvailabilityValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ready", "unavailable", "requires-setup", "disabled" };

	public void Register(IGameMacroAdapterProvider provider)
	{
		ArgumentNullException.ThrowIfNull(provider, "provider");
		GameToolDescriptor gameToolDescriptor = provider.Descriptor ?? throw new InvalidOperationException("Adapter provider did not supply a descriptor.");
		ValidateDescriptor(gameToolDescriptor);
		if (_providers.ContainsKey(gameToolDescriptor.AdapterId))
		{
			throw new InvalidOperationException("Adapter '" + gameToolDescriptor.AdapterId + "' is already registered.");
		}
		GameToolDescriptor pinnedDescriptor = gameToolDescriptor with
		{
			Actions = gameToolDescriptor.Actions.ToFrozenSet<string>(StringComparer.Ordinal),
			ObservationKeys = gameToolDescriptor.ObservationKeys.ToFrozenSet<string>(StringComparer.Ordinal)
		};
		_providers.Add(gameToolDescriptor.AdapterId, new Registration(provider, pinnedDescriptor));
	}

	public IReadOnlyList<GameToolDescriptor> ListForGame(string gameId)
	{
		string gameId2 = gameId;
		return (from x in _providers.Values
			select x.PinnedDescriptor into x
			where x.GameId.Equals(gameId2, StringComparison.Ordinal)
			select x).OrderBy<GameToolDescriptor, string>((GameToolDescriptor x) => x.DisplayName, StringComparer.Ordinal).ToArray();
	}

	private static void ValidateDescriptor(GameToolDescriptor descriptor)
	{
		bool flag = !Identifier.IsMatch(descriptor.AdapterId ?? string.Empty) || !Identifier.IsMatch(descriptor.GameId ?? string.Empty);
		if (!flag)
		{
			int contractVersion = descriptor.ContractVersion;
			bool flag2 = ((contractVersion < 1 || contractVersion > 100) ? true : false);
			flag = flag2;
		}
		if (flag || string.IsNullOrWhiteSpace(descriptor.DisplayName) || descriptor.DisplayName.Length > 120 || descriptor.DisplayName.Any(char.IsControl) || string.IsNullOrWhiteSpace(descriptor.Engine) || descriptor.Engine.Length > 80 || descriptor.Engine.Any(char.IsControl) || !AvailabilityValues.Contains(descriptor.Availability ?? string.Empty) || descriptor.Actions == null || descriptor.ObservationKeys == null || descriptor.Actions.Count > 256 || descriptor.ObservationKeys.Count > 256 || (string.Equals(descriptor.Availability, "ready", StringComparison.OrdinalIgnoreCase) && (descriptor.Actions.Count == 0 || descriptor.ObservationKeys.Count == 0)) || descriptor.Actions.Any((string value) => string.IsNullOrWhiteSpace(value) || !CapabilityIdentifier.IsMatch(value)) || descriptor.ObservationKeys.Any((string value) => string.IsNullOrWhiteSpace(value) || !CapabilityIdentifier.IsMatch(value)))
		{
			throw new InvalidOperationException("Adapter descriptor is malformed or contains an invalid capability declaration.");
		}
	}

	private static bool SameDescriptor(GameToolDescriptor current, GameToolDescriptor pinned)
	{
		if (current.AdapterId == pinned.AdapterId && current.ContractVersion == pinned.ContractVersion && current.GameId == pinned.GameId && current.DisplayName == pinned.DisplayName && current.Engine == pinned.Engine && current.Availability == pinned.Availability && current.RequiresOwnedProcess == pinned.RequiresOwnedProcess && current.RequiresUnmirroredDisplay == pinned.RequiresUnmirroredDisplay)
		{
			return current.CapabilitySha256 == pinned.CapabilitySha256;
		}
		return false;
	}

	public async Task<IGameMacroAdapter> ConnectAsync(string adapterId, GameAutomationTarget target, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!_providers.TryGetValue(adapterId, out Registration value))
		{
			throw new KeyNotFoundException("Game adapter '" + adapterId + "' is not registered.");
		}
		IGameMacroAdapterProvider provider = value.Provider;
		GameToolDescriptor descriptor = value.PinnedDescriptor;
		if (!SameDescriptor(provider.Descriptor, descriptor))
		{
			throw new InvalidOperationException("Registered adapter descriptor changed after its capabilities were pinned; reconnect requires re-registration.");
		}
		if (descriptor.Availability != "ready")
		{
			throw new InvalidOperationException("Game adapter is not ready: " + descriptor.Availability);
		}
		if (!provider.Supports(target) || !descriptor.GameId.Equals(target.GameId, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Selected adapter does not support this game target.");
		}
		IGameMacroAdapter gameMacroAdapter = await provider.ConnectAsync(target, cancellationToken);
		if (gameMacroAdapter == null)
		{
			throw new InvalidOperationException("Game adapter provider returned no adapter.");
		}
		if (gameMacroAdapter.Descriptor.AdapterId != descriptor.AdapterId || gameMacroAdapter.Descriptor.ContractVersion != descriptor.ContractVersion || gameMacroAdapter.Descriptor.CapabilitySha256 != descriptor.CapabilitySha256 || gameMacroAdapter.Target != target)
		{
			await StopAndDisposeRejectedAsync(gameMacroAdapter);
			throw new InvalidOperationException("Connected adapter identity differs from the registered provider or requested target.");
		}
		if (!GameMacroTargetSafety.IsVerified(gameMacroAdapter.TargetVerification, descriptor, target))
		{
			await StopAndDisposeRejectedAsync(gameMacroAdapter);
			throw new InvalidOperationException("Connected adapter did not prove the selected game target/session is task-owned and valid.");
		}
		return new PinnedAdapter(provider, gameMacroAdapter, descriptor);
	}

	private static async Task StopAndDisposeRejectedAsync(IGameMacroAdapter adapter)
	{
		Exception failure = null;
		try
		{
			await adapter.StopAsync(-1L, CancellationToken.None);
		}
		catch (Exception ex)
		{
			failure = ex;
		}
		try
		{
			await adapter.DisposeAsync();
		}
		catch (Exception ex2)
		{
			if (failure == null)
			{
				failure = ex2;
			}
		}
		if (failure != null)
		{
			throw new InvalidOperationException("Rejected adapter cleanup failed.", failure);
		}
	}
}
