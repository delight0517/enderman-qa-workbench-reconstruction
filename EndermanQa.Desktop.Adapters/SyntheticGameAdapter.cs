using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Adapters;

public sealed class SyntheticGameAdapter : IGameAdapter, IAsyncDisposable
{
	private readonly HashSet<string> _allowed = new HashSet<string>(StringComparer.Ordinal) { "fixture.create", "entity.spawn", "entity.apply_damage", "environment.configure", "time.advance", "observation.request", "capture.request" };

	private QaPlan? _plan;

	private string? _entityRunId;

	private string? _runId;

	private EntitySnapshot? _snapshot;

	private long _sequence;

	private long _generation;

	private int _nextStep;

	private bool _fixtureCreated;

	private bool _fallConfigured;

	private bool _stopped;

	public GameTarget Target { get; } = new GameTarget("synthetic-local", "synthetic-game", null, "1.0", "synthetic", "owned");


	public AdapterCapabilities Capabilities { get; } = new AdapterCapabilities("synthetic.adapter", "1.0", new HashSet<string>(StringComparer.Ordinal) { "fixture.create", "entity.spawn", "entity.apply_damage", "environment.configure", "time.advance", "observation.request", "capture.request" }, AuthoritativeEntityState: false, Screenshot: false, Video: false);


	public Task InitializeAsync(string runId, QaPlan plan, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (plan.TargetId != Target.TargetId || plan.AdapterId != Capabilities.AdapterId || plan.PlanHash.Length != 64 || plan.Steps.Count == 0)
		{
			throw new InvalidOperationException("Synthetic adapter rejected a mismatched or invalid plan.");
		}
		_runId = runId;
		_plan = plan;
		_generation = 0L;
		_nextStep = 0;
		_stopped = false;
		return Task.CompletedTask;
	}

	public async Task<AdapterActionResult> ExecuteAsync(QaPlan plan, PlannedStep step, long cancellationGeneration, CancellationToken cancellationToken)
	{
		EnsureAuthorized(plan, step, cancellationGeneration, cancellationToken);
		await Task.Delay(80, cancellationToken);
		EnsureAuthorized(plan, step, cancellationGeneration, cancellationToken);
		_nextStep++;
		List<QaEvent> list = new List<QaEvent>();
		EntitySnapshot entity = null;
		bool visualCaptureAvailable = false;
		string detail;
		switch (step.Action)
		{
		case "fixture.create":
			_fixtureCreated = true;
			detail = "Created in-memory disposable fixture; no game world was changed.";
			break;
		case "entity.spawn":
			if (!_fixtureCreated)
			{
				throw new InvalidOperationException("Cannot spawn before the synthetic fixture is created.");
			}
			_entityRunId = "entity-" + Guid.NewGuid().ToString("N");
			_snapshot = new EntitySnapshot(_entityRunId, "synthetic-native-1", "example:enderman", EntityLifeState.Alive, 40.0, 40.0, null, 100L, DateTimeOffset.UtcNow, "synthetic-adapter");
			entity = _snapshot;
			detail = "Created synthetic entity with a stable run-scoped ID; no Minecraft entity was spawned.";
			list.Add(Event("entity.observed", "Synthetic entity initialized alive with 40/40 health."));
			break;
		case "environment.configure":
		{
			if ((object)_snapshot == null)
			{
				throw new InvalidOperationException("Cannot configure a hazard before the entity exists.");
			}
			_fallConfigured = step.Arguments.TryGetValue("hazard", out string value2) && value2 == "controlled-fall";
			detail = (_fallConfigured ? "Configured an in-memory controlled-fall condition." : "Configured an in-memory synthetic environment.");
			break;
		}
		case "entity.apply_damage":
		{
			if ((object)_snapshot == null)
			{
				throw new InvalidOperationException("Cannot apply damage before the entity exists.");
			}
			string value;
			double result;
			double num = ((step.Arguments.TryGetValue("amount", out value) && double.TryParse(value, out result)) ? result : 1.0);
			double num2 = Math.Max(0.0, _snapshot.Health.GetValueOrDefault(40.0) - Math.Max(0.0, num));
			_snapshot = _snapshot with
			{
				Health = num2,
				LifeState = ((num2 == 0.0) ? EntityLifeState.Dead : EntityLifeState.Alive),
				DamageCause = "synthetic-damage",
				GameTick = _snapshot.GameTick.GetValueOrDefault() + 1,
				ObservedAt = DateTimeOffset.UtcNow
			};
			entity = _snapshot;
			detail = $"Applied {num:0.##} synthetic damage.";
			list.Add(Event("entity.damage_received", $"health={num2:0.##}"));
			if (num2 == 0.0)
			{
				list.Add(Event("entity.died", "Synthetic death transition; not a Minecraft observation."));
			}
			break;
		}
		case "time.advance":
			if ((object)_snapshot != null)
			{
				_snapshot = _snapshot with
				{
					GameTick = _snapshot.GameTick.GetValueOrDefault() + 20,
					ObservedAt = DateTimeOffset.UtcNow
				};
			}
			detail = "Advanced the synthetic tick counter by 20.";
			entity = _snapshot;
			break;
		case "observation.request":
			if ((object)_snapshot != null && _fallConfigured && _snapshot.LifeState == EntityLifeState.Alive && step.Arguments.GetValueOrDefault("until") == "dead-or-timeout")
			{
				_snapshot = _snapshot with
				{
					LifeState = EntityLifeState.Dead,
					Health = 0.0,
					DamageCause = "controlled-fall",
					GameTick = _snapshot.GameTick.GetValueOrDefault() + 40,
					ObservedAt = DateTimeOffset.UtcNow
				};
				list.Add(Event("entity.died", "Synthetic fixture confirmed the expected state transition; no game was run."));
			}
			entity = _snapshot;
			detail = (((object)_snapshot == null) ? "No entity has been observed." : $"Observed {_snapshot.LifeState} at synthetic tick {_snapshot.GameTick}.");
			if ((object)_snapshot != null)
			{
				list.Add(Event("entity.observed", detail));
			}
			break;
		case "capture.request":
			detail = "No screenshot/video backend in the synthetic adapter; capture unavailable is recorded explicitly.";
			list.Add(Event("adapter.warning", detail));
			break;
		default:
			throw new InvalidOperationException("Synthetic adapter does not implement action '" + step.Action + "'.");
		}
		return new AdapterActionResult(step.StepId, step.Action, Succeeded: true, detail, entity, list, visualCaptureAvailable);
	}

	public Task StopAsync(long cancellationGeneration, CancellationToken cancellationToken = default(CancellationToken))
	{
		_generation = Math.Max(_generation + 1, cancellationGeneration);
		_stopped = true;
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		_stopped = true;
		_plan = null;
		return ValueTask.CompletedTask;
	}

	private QaEvent Event(string kind, string detail)
	{
		return new QaEvent(_runId ?? "", ++_sequence, kind, _entityRunId, DateTimeOffset.UtcNow, _snapshot?.GameTick, detail, "synthetic-adapter");
	}

	private void EnsureAuthorized(QaPlan plan, PlannedStep step, long generation, CancellationToken cancellationToken)
	{
		PlannedStep step2 = step;
		cancellationToken.ThrowIfCancellationRequested();
		if (_stopped || generation != _generation)
		{
			throw new OperationCanceledException("Adapter run was stopped.", cancellationToken);
		}
		if ((object)_plan == null || plan.PlanId != _plan.PlanId || plan.PlanHash != _plan.PlanHash || plan.TargetId != Target.TargetId)
		{
			throw new InvalidOperationException("Action does not belong to this adapter's authorized target/plan.");
		}
		if (!_allowed.Contains(step2.Action) || step2.Order != _nextStep + 1 || !_plan.Steps.Any((PlannedStep x) => x.Order == step2.Order && x.StepId == step2.StepId && x.Action == step2.Action))
		{
			throw new InvalidOperationException("Action is not in the validated plan or is out of order.");
		}
	}
}
