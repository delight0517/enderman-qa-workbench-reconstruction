using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Contracts;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Adapters;

internal sealed class MinecraftServerMacroAdapter : IGameMacroAdapter, IAsyncDisposable
{
	[CompilerGenerated]
	private GameToolDescriptor _003Cdescriptor_003EP;

	[CompilerGenerated]
	private GameAutomationTarget _003Ctarget_003EP;

	[CompilerGenerated]
	private GameTargetVerification _003Cverification_003EP;

	[CompilerGenerated]
	private IGameAdapter _003Cinner_003EP;

	private QaPlan? _plan;

	private bool _initialized;

	private string? _preparationSummary;

	public GameToolDescriptor Descriptor => _003Cdescriptor_003EP;

	public GameAutomationTarget Target => _003Ctarget_003EP;

	public GameTargetVerification TargetVerification => _003Cverification_003EP;

	public MinecraftServerMacroAdapter(GameToolDescriptor descriptor, GameAutomationTarget target, GameTargetVerification verification, IGameAdapter inner)
	{
		_003Cdescriptor_003EP = descriptor;
		_003Ctarget_003EP = target;
		_003Cverification_003EP = verification;
		_003Cinner_003EP = inner;
		
	}

	public async Task InitializeAsync(GameAutomationMacro macro, CancellationToken cancellationToken)
	{
		if (macro.Target != _003Ctarget_003EP || macro.AdapterId != _003Cdescriptor_003EP.AdapterId)
		{
			throw new InvalidOperationException("Minecraft server macro target or adapter changed before initialization.");
		}
		_initialized = false;
		_preparationSummary = null;
		PlannedStep[] steps = macro.Steps.Select((GameMacroStep step, int index) => new PlannedStep(index + 1, step.Id, step.Action, step.Arguments)).ToArray();
		string planHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(macro.ContentSha256 + _003Ctarget_003EP.TargetId)));
		_plan = new QaPlan(macro.Id, "saved-macro:" + macro.Id, macro.ContentSha256, _003Cinner_003EP.Capabilities.AdapterId, _003Cinner_003EP.Target.TargetId, DateTimeOffset.UtcNow, 180, steps, macro.Assertions.Select((GameMacroAssertion assertion) => $"{assertion.AfterStepId}:{assertion.ObservationKey}:{assertion.Operator}:{assertion.ExpectedValue}").ToArray(), Array.Empty<string>(), planHash, "macro-defined");
		_preparationSummary = await ((_003Cinner_003EP as ISuiteLifecycleAdapter) ?? throw new InvalidOperationException("Minecraft server macro adapter does not expose verified QA fixture preparation; no action was dispatched.")).PrepareSuiteAsync(cancellationToken);
		await _003Cinner_003EP.InitializeAsync("macro-" + macro.Id, _plan, cancellationToken);
		_initialized = true;
	}

	public async Task<GameMacroStepResult> ExecuteAsync(GameAutomationMacro macro, GameMacroStep step, long cancellationGeneration, CancellationToken cancellationToken)
	{
		GameMacroStep step2 = step;
		QaPlan qaPlan = _plan ?? throw new InvalidOperationException("Minecraft macro has not been initialized.");
		if (!_initialized || macro.Target != _003Ctarget_003EP || macro.AdapterId != _003Cdescriptor_003EP.AdapterId)
		{
			throw new InvalidOperationException("Minecraft macro session identity changed.");
		}
		PlannedStep step3 = qaPlan.Steps.SingleOrDefault((PlannedStep item) => item.StepId == step2.Id && item.Action == step2.Action) ?? throw new InvalidOperationException("Macro step is not in the pinned Minecraft plan.");
		AdapterActionResult adapterActionResult = await _003Cinner_003EP.ExecuteAsync(qaPlan, step3, cancellationGeneration, cancellationToken);
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["action.succeeded"] = (adapterActionResult.Succeeded ? "true" : "false"),
			["action.detail"] = adapterActionResult.Detail
		};
		EntitySnapshot entity = adapterActionResult.Entity;
		if ((object)entity != null)
		{
			dictionary["entity.uuid"] = entity.RunEntityId;
			dictionary["entity.type"] = entity.EntityType;
			dictionary["entity.lifeState"] = entity.LifeState.ToString().ToLowerInvariant();
			dictionary["entity.alive"] = (entity.LifeState == EntityLifeState.Alive).ToString().ToLowerInvariant();
			dictionary["entity.health"] = entity.Health?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
			dictionary["entity.maxHealth"] = entity.MaxHealth?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
			dictionary["entity.damageCause"] = entity.DamageCause ?? "unknown";
			dictionary["entity.gameTick"] = entity.GameTick?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
			dictionary["entity.provenance"] = entity.Provenance;
			dictionary["entity.damageEventCount"] = (entity.DamageEvents?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
			dictionary["entity.attackerUuid"] = entity.LastAttackerUuid ?? "unknown";
			dictionary["entity.positionTraceCount"] = (entity.PositionTrace?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
			WorldPositionSnapshot position = entity.Position;
			if ((object)position != null)
			{
				dictionary["entity.dimension"] = position.Dimension;
				dictionary["entity.x"] = position.X.ToString(CultureInfo.InvariantCulture);
				dictionary["entity.y"] = position.Y.ToString(CultureInfo.InvariantCulture);
				dictionary["entity.z"] = position.Z.ToString(CultureInfo.InvariantCulture);
			}
			int? frozenTicks = entity.FrozenTicks;
			if (frozenTicks.HasValue)
			{
				dictionary["entity.frozenTicks"] = frozenTicks.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
			}
			frozenTicks = entity.FireTicks;
			if (frozenTicks.HasValue)
			{
				dictionary["entity.fireTicks"] = frozenTicks.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
			}
		}
		return new GameMacroStepResult(Detail: dictionary["action.detail"] = ((_preparationSummary == null) ? adapterActionResult.Detail : ("QA fixture preparation confirmed: " + _preparationSummary + ". " + adapterActionResult.Detail)), StepId: step2.Id, Succeeded: adapterActionResult.Succeeded, Observations: dictionary, EvidenceReferences: Array.Empty<string>());
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
