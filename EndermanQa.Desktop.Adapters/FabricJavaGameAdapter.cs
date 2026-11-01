using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Adapters;

public sealed class FabricJavaGameAdapter : IGameAdapter, IAsyncDisposable, ISuiteLifecycleAdapter
{
	private readonly FabricBridgeClient _bridge;

	private readonly FabricBridgeStatus _identity;

	private QaPlan? _plan;

	private string? _testEntityUuid;

	private string? _managedVehicleUuid;

	private EntitySnapshot? _lastEntity;

	private readonly List<string> _managedEntityUuids = new List<string>();

	private HashSet<string> _artifactBaselineUuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private string? _runId;

	private int _nextStep;

	private bool _stopped;

	public static IReadOnlySet<string> SupportedActions { get; } = new HashSet<string>(StringComparer.Ordinal)
	{
		"entity.spawn", "entity.apply_damage", "entity.player_attack", "entity.player_attack.profile.v1", "entity.sweep_attack", "entity.sweep_attack.v1", "environment.configure", "projectile.throw", "projectile.throw_volley", "observation.request",
		"time.advance"
	};


	public GameTarget Target { get; }

	public AdapterCapabilities Capabilities { get; } = new AdapterCapabilities("minecraft-java.fabric-owned", "1.0", SupportedActions, AuthoritativeEntityState: true, Screenshot: false, Video: false);


	private FabricJavaGameAdapter(FabricBridgeClient bridge, FabricBridgeStatus identity)
	{
		_bridge = bridge;
		_identity = identity;
		Target = new GameTarget(identity.TargetId, "minecraft-java", "Java", identity.GameVersion, identity.RuntimeKind, "explicit-disposable");
	}

	public static async Task<FabricJavaGameAdapter> ConnectOwnedAsync(JavaServerWorkspace workspace, Process process, string expectedJavaExecutable, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (process.HasExited)
		{
			throw new InvalidOperationException("App-owned Java server has exited.");
		}
		if (!Path.GetFullPath(process.StartInfo.FileName).Equals(Path.GetFullPath(expectedJavaExecutable), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Java process executable does not match the selected task runtime.");
		}
		string descriptorPath = Path.Combine(Path.GetFullPath(workspace.RootPath), "endermanqa-bridge.json");
		FabricBridgeClient bridge = FabricBridgeClient.FromDescriptor(descriptorPath);
		try
		{
			FabricObservationSnapshot fabricObservationSnapshot = await bridge.ObserveAsync(cancellationToken);
			if (process.HasExited || fabricObservationSnapshot.Status.ServerPid != process.Id || fabricObservationSnapshot.Status.InstanceId != workspace.InstanceId || fabricObservationSnapshot.Status.TargetId.Length < 1 || !fabricObservationSnapshot.Status.WorldMutable)
			{
				throw new InvalidOperationException("Fabric bridge identity does not match the exact app-owned process and disposable instance.");
			}
			return new FabricJavaGameAdapter(bridge, fabricObservationSnapshot.Status);
		}
		catch
		{
			bridge.Dispose();
			throw;
		}
	}

	public Task InitializeAsync(string runId, QaPlan plan, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (plan.AdapterId != Capabilities.AdapterId || plan.TargetId != Target.TargetId || plan.PlanHash.Length != 64)
		{
			throw new InvalidOperationException("Java adapter rejected a plan for another process, target or adapter.");
		}
		_plan = plan;
		_runId = runId;
		_nextStep = 0;
		_stopped = false;
		_testEntityUuid = null;
		_managedVehicleUuid = null;
		_lastEntity = null;
		_managedEntityUuids.Clear();
		_artifactBaselineUuids.Clear();
		return Task.CompletedTask;
	}

	public async Task<string> PrepareSuiteAsync(CancellationToken cancellationToken)
	{
		FabricActionResponse fabricActionResponse = await _bridge.ExecuteAllowlistedActionAsync("prepare_qa_suite", null, null, null, 1, null, cancellationToken);
		if (!fabricActionResponse.NormalizedQaCage.GetValueOrDefault() || !fabricActionResponse.WeatherCleared.GetValueOrDefault() || !fabricActionResponse.SpawnMobsDisabled.GetValueOrDefault())
		{
			throw new InvalidOperationException("Java bridge did not confirm QA cage normalization, baseline weather cleanup and temporary natural mob-spawn suppression.");
		}
		return $"removed stale tagged fixtures={fabricActionResponse.RemovedTestEntities.GetValueOrDefault()}; reserved 3x3 cage normalized; baseline weather cleared; natural mob spawning temporarily disabled and scheduled for restoration";
	}

	public async Task<AdapterActionResult> ExecuteAsync(QaPlan plan, PlannedStep step, long cancellationGeneration, CancellationToken cancellationToken)
	{
		PlannedStep step2 = step;
		if (_stopped || (object)_plan == null || _plan.PlanHash != plan.PlanHash || _nextStep + 1 != step2.Order || !_plan.Steps.Any((PlannedStep x) => x.Order == step2.Order && x.StepId == step2.StepId && x.Action == step2.Action))
		{
			throw new InvalidOperationException("Java adapter rejected an unplanned, out-of-order or cancelled action.");
		}
		cancellationToken.ThrowIfCancellationRequested();
		_nextStep++;
		FabricActionResponse response;
		if (step2.Action == "entity.spawn")
		{
			step2.Arguments.TryGetValue("hazard", out string value2);
			response = await _bridge.ExecuteAllowlistedActionAsync("spawn_test_enderman", null, value2, null, 1, null, cancellationToken);
			_testEntityUuid = response.EntityUuid;
			_managedVehicleUuid = null;
			if (!string.IsNullOrWhiteSpace(_testEntityUuid))
			{
				_managedEntityUuids.Add(_testEntityUuid);
			}
			EntitySnapshot entity2;
			try
			{
				entity2 = await WaitForEntityAsync(_testEntityUuid, "alive", cancellationToken);
			}
			catch (TimeoutException innerException)
			{
				throw new TimeoutException($"Spawn was accepted but not observed; server says added={response.EntityAdded}, tickingChunk={response.TickingChunk}, endermanType={response.EndermanTypeMatches}, entityType={response.EntityType ?? "unknown"}, loadedEntityCount={response.EntityCount}.", innerException);
			}
			return Result(step2, entity2, $"Spawned one bridge-managed Enderman in this disposable QA world; cleared {response.ClearedQaArtifacts.GetValueOrDefault()} pre-existing item/XP entities in the owned 8-block QA fixture radius.");
		}
		if (step2.Action == "entity.apply_damage")
		{
			if (_testEntityUuid == null)
			{
				throw new InvalidOperationException("The plan must spawn a managed Enderman before applying test damage.");
			}
			await _bridge.ExecuteAllowlistedActionAsync("kill_test_enderman", _testEntityUuid, null, null, 1, null, cancellationToken);
			EntitySnapshot entity3 = await WaitForEntityAsync(_testEntityUuid, "dead", cancellationToken);
			return Result(step2, entity3, "Applied out-of-world test damage to the bridge-managed Enderman and observed server death state.");
		}
		bool completeReadback = default(bool);
		if (step2.Action == "entity.sweep_attack")
		{
			if (_testEntityUuid == null)
			{
				throw new InvalidOperationException("The plan must spawn a managed primary Enderman before a sweep attack.");
			}
			response = await _bridge.ExecuteAllowlistedActionAsync("sweep_attack_test_endermen", _testEntityUuid, null, "iron_sword", 1, "sweeping1", cancellationToken, teleportAfterHit: false, null, disableTargetAi: true);
			IReadOnlyList<FabricSweepTargetObservation> sweepTargets = response.SweepTargets ?? Array.Empty<FabricSweepTargetObservation>();
			foreach (string item in (from target in sweepTargets
				where target.Uuid != _testEntityUuid
				select target.Uuid).Distinct<string>(StringComparer.Ordinal))
			{
				if (!_managedEntityUuids.Contains<string>(item, StringComparer.Ordinal))
				{
					_managedEntityUuids.Add(item);
				}
			}
			HashSet<string> hashSet = sweepTargets.Select((FabricSweepTargetObservation target) => target.Role).ToHashSet<string>(StringComparer.Ordinal);
			completeReadback = response.SweepEnchantmentLevel.GetValueOrDefault() == 1 && response.TargetAiDisabled.GetValueOrDefault() && sweepTargets.Count == 3 && sweepTargets.Select((FabricSweepTargetObservation target) => target.Uuid).Distinct<string>(StringComparer.Ordinal).Count() == 3 && hashSet.SetEquals(new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "primary", "eligible-neighbor", "outside-control" }));
			FabricObservationSnapshot fabricObservationSnapshot = await _bridge.ObserveAsync(cancellationToken);
			EntitySnapshot entitySnapshot = Map(fabricObservationSnapshot.Endermen.FirstOrDefault((FabricEndermanObservation item) => item.Uuid == _testEntityUuid), fabricObservationSnapshot.Deaths);
			string detail = $"sweepOracle={response.SweepOracle?.ToString().ToLowerInvariant() ?? "unknown"}; profile=sweeping1; enchantmentLevel={response.SweepEnchantmentLevel?.ToString() ?? "unknown"}; attackerUuid={response.AttackerUuid ?? "unknown"}; serverTick={response.ServerTick}; exactUuidReadback={completeReadback}; " + string.Join(" | ", sweepTargets.Select((FabricSweepTargetObservation target) => $"role={target.Role};uuid={target.Uuid};hp={target.BeforeHealth:0.##}->{target.AfterHealth:0.##};allDamageDelta={target.DamageEventDelta};playerDamageDelta={target.PlayerDamageEventDelta};attackerMatches={target.AttackerMatches};damageTick={target.GameTick};attackTick={target.AttackTick};distance={target.Distance:0.##};alive={target.Alive}"));
			QaEvent[] events = sweepTargets.Select((FabricSweepTargetObservation target, int index) => new QaEvent(_runId ?? "", (long)step2.Order * 10L + index, "sweep-target", target.Uuid, DateTimeOffset.UtcNow, target.GameTick, $"role={target.Role};uuid={target.Uuid};beforeHealth={target.BeforeHealth.ToString("0.##", CultureInfo.InvariantCulture)};afterHealth={target.AfterHealth.ToString("0.##", CultureInfo.InvariantCulture)};damageEventDelta={target.DamageEventDelta};playerDamageEventDelta={target.PlayerDamageEventDelta};attackerUuid={target.AttackerUuid};attackerMatches={target.AttackerMatches};gameTick={target.GameTick};attackTick={target.AttackTick};distance={target.Distance.ToString("0.##", CultureInfo.InvariantCulture)};alive={target.Alive}", "minecraft-java-fabric-authoritative")).ToArray();
			if ((object)entitySnapshot != null)
			{
				_lastEntity = entitySnapshot;
			}
			return new AdapterActionResult(step2.StepId, step2.Action, completeReadback, detail, entitySnapshot, events, VisualCaptureAvailable: false);
		}
		bool flag;
		if (step2.Action == "entity.player_attack")
		{
			EntityLifeState? entityLifeState = _lastEntity?.LifeState;
			if (entityLifeState.HasValue)
			{
				EntityLifeState valueOrDefault = entityLifeState.GetValueOrDefault();
				if ((uint)(valueOrDefault - 1) <= 1u)
				{
					flag = true;
					goto IL_0a3c;
				}
			}
			flag = false;
			goto IL_0a3c;
		}
		if (step2.Action == "projectile.throw_volley")
		{
			return await ExecuteProjectileVolleyAsync(step2, cancellationToken);
		}
		FabricActionResponse launched;
		string[] projectileIds;
		Dictionary<string, FabricProjectileObservation> observations;
		EntitySnapshot entitySnapshot2;
		FabricEndermanObservation fabricEndermanObservation;
		FabricDamageFact[] array2;
		int num;
		string attackProfile2 = default(string);
		bool disableTargetAi2 = default(bool);
		if (step2.Action == "projectile.throw")
		{
			flag = _testEntityUuid == null || !step2.Arguments.TryGetValue("projectileType", out attackProfile2);
			if (!flag)
			{
				bool flag2;
				switch (attackProfile2)
				{
				case "splash_water_inside":
				case "splash_water_potion":
				case "splash_harming_potion":
				case "splash_healing_potion":
				case "snowball":
				case "egg":
				case "arrow":
				case "flaming_arrow":
				case "crossbow_arrow":
				case "firework_crossbow":
				case "trident":
				case "splash_water_outside":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = !flag2;
			}
			if (flag)
			{
				throw new InvalidOperationException("Projectile test requires a managed Enderman and an allowlisted projectile type.");
			}
			string valueOrDefault2 = step2.Arguments.GetValueOrDefault("secondProjectileType");
			flag = ((valueOrDefault2 == null || valueOrDefault2 == "splash_harming_potion") ? true : false);
			if (!flag || (valueOrDefault2 != null && attackProfile2 != "splash_water_potion"))
			{
				throw new InvalidOperationException("Paired projectile test is limited to water followed by Harming splash potions.");
			}
			string value3;
			bool result = default(bool);
			bool flag3 = step2.Arguments.TryGetValue("teleportTargetAfterLaunch", out value3) && bool.TryParse(value3, out result) && result;
			bool result2 = default(bool);
			disableTargetAi2 = step2.Arguments.TryGetValue("secondTargetAfterTeleport", out string value4) && bool.TryParse(value4, out result2) && result2;
			completeReadback = valueOrDefault2 != null;
			if (disableTargetAi2 && (attackProfile2 != "arrow" || !flag3 || completeReadback))
			{
				throw new InvalidOperationException("TC #55 requires one vanilla arrow and the fixed same-action teleport fixture.");
			}
			string action = (completeReadback ? "throw_test_potion_pair" : (disableTargetAi2 ? "throw_test_projectile_second_target" : "throw_test_projectile"));
			launched = await _bridge.ExecuteAllowlistedActionAsync(action, _testEntityUuid, null, attackProfile2, 1, null, cancellationToken, teleportAfterHit: false, null, disableTargetAi: false, flag3, valueOrDefault2, disableTargetAi2);
			if (!string.IsNullOrWhiteSpace(launched.SecondTargetUuid) && !_managedEntityUuids.Contains<string>(launched.SecondTargetUuid, StringComparer.Ordinal))
			{
				_managedEntityUuids.Add(launched.SecondTargetUuid);
			}
			if (disableTargetAi2 && (!launched.SecondTargetPlacementVerified.GetValueOrDefault() || launched.SecondTargetUuid == null || !launched.BeforeHealth.HasValue || !launched.SecondTargetHealthBefore.HasValue || !launched.SecondTargetDamageEventsBefore.HasValue || !launched.FirstTargetDamageEventsBefore.HasValue || (object)launched.SecondTargetPositionBefore == null || !launched.TeleportSucceeded.GetValueOrDefault() || !launched.TargetAiDisabled.GetValueOrDefault()))
			{
				return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: exact first/second target identity, baselines, AI isolation or post-launch teleport was not confirmed; no gameplay verdict.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
			}
			projectileIds = ((!completeReadback) ? (string.IsNullOrWhiteSpace(launched.ProjectileUuid) ? Array.Empty<string>() : new string[1] { launched.ProjectileUuid }) : (launched.ProjectileUuids?.ToArray() ?? Array.Empty<string>()));
			if (!launched.EntityAdded || projectileIds.Length != ((!completeReadback) ? 1 : 2))
			{
				return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: Minecraft did not confirm the managed projectile was added.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
			}
			observations = new Dictionary<string, FabricProjectileObservation>(StringComparer.Ordinal);
			DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(7.0);
			while (DateTimeOffset.UtcNow < deadline)
			{
				cancellationToken.ThrowIfCancellationRequested();
				IReadOnlyList<FabricProjectileObservation> source = await _bridge.GetProjectilesAsync(cancellationToken);
				string[] array = projectileIds;
				foreach (string projectileId in array)
				{
					FabricProjectileObservation fabricProjectileObservation = source.FirstOrDefault((FabricProjectileObservation item) => item.ProjectileUuid == projectileId);
					if ((object)fabricProjectileObservation != null)
					{
						observations[projectileId] = fabricProjectileObservation;
					}
				}
				if (projectileIds.All((string id) => observations.TryGetValue(id, out FabricProjectileObservation value34) && value34.Outcome != "pending"))
				{
					break;
				}
				await Task.Delay(100, cancellationToken);
			}
			FabricObservationSnapshot fabricObservationSnapshot2 = await _bridge.ObserveAsync(cancellationToken);
			entitySnapshot2 = Map(fabricObservationSnapshot2.Endermen.FirstOrDefault((FabricEndermanObservation item) => item.Uuid == _testEntityUuid), fabricObservationSnapshot2.Deaths);
			if (disableTargetAi2)
			{
				fabricEndermanObservation = fabricObservationSnapshot2.Endermen.FirstOrDefault((FabricEndermanObservation item) => item.Uuid == launched.SecondTargetUuid);
				array2 = (from item in fabricEndermanObservation?.DamageEvents?.Skip(launched.SecondTargetDamageEventsBefore.Value)
					where string.Equals(item.Cause, "arrow", StringComparison.OrdinalIgnoreCase) && item.DamageTaken > 0f
					select item).ToArray() ?? Array.Empty<FabricDamageFact>();
				if ((object)fabricEndermanObservation != null)
				{
					FabricPositionCoordinates secondTargetPositionBefore = launched.SecondTargetPositionBefore;
					if ((object)secondTargetPositionBefore != null)
					{
						num = ((Math.Sqrt(Math.Pow(fabricEndermanObservation.X - secondTargetPositionBefore.X, 2.0) + Math.Pow(fabricEndermanObservation.Y - secondTargetPositionBefore.Y, 2.0) + Math.Pow(fabricEndermanObservation.Z - secondTargetPositionBefore.Z, 2.0)) >= 4.0) ? 1 : 0);
						goto IL_22b7;
					}
				}
				num = 0;
				goto IL_22b7;
			}
			if (completeReadback)
			{
				FabricProjectileObservation fabricProjectileObservation2 = observations.Values.FirstOrDefault((FabricProjectileObservation item) => item.ProjectileType == "splash_water_potion");
				FabricProjectileObservation fabricProjectileObservation3 = observations.Values.FirstOrDefault((FabricProjectileObservation item) => item.ProjectileType == "splash_harming_potion");
				bool value5 = fabricProjectileObservation2?.Outcome == "entity-hit" && fabricProjectileObservation2.ImpactEntityUuid == _testEntityUuid;
				bool value6 = fabricProjectileObservation3?.Outcome == "entity-hit" && fabricProjectileObservation3.ImpactEntityUuid == _testEntityUuid;
				bool value7 = (object)fabricProjectileObservation2 != null && (object)fabricProjectileObservation3 != null && fabricProjectileObservation2.Outcome == "entity-hit" && fabricProjectileObservation3.Outcome == "entity-hit" && fabricProjectileObservation2.GameTick == fabricProjectileObservation3.GameTick;
				IReadOnlyList<DamageEventSnapshot> readOnlyList = entitySnapshot2?.DamageEvents;
				string value8 = ((readOnlyList == null) ? "unknown" : string.Join(",", readOnlyList.Select((DamageEventSnapshot item) => item.Cause)));
				string detail2 = $"potionPairTypes=splash_water_potion,splash_harming_potion; waterProjectileUuid={fabricProjectileObservation2?.ProjectileUuid ?? "unknown"}; harmingProjectileUuid={fabricProjectileObservation3?.ProjectileUuid ?? "unknown"}; waterEffect={fabricProjectileObservation2?.PotionEffects ?? "unknown"}; harmingEffect={fabricProjectileObservation3?.PotionEffects ?? "unknown"}; waterProjectileHit={value5}; harmingProjectileHit={value6}; sameImpactTick={value7}; waterImpactTick={fabricProjectileObservation2?.GameTick.ToString() ?? "unknown"}; harmingImpactTick={fabricProjectileObservation3?.GameTick.ToString() ?? "unknown"}; damageEventCount={readOnlyList?.Count.ToString() ?? "unknown"}; damageEventSources={value8}; healthAfter={entitySnapshot2?.Health?.ToString("0.##") ?? "unknown"}; lifeState={entitySnapshot2?.LifeState.ToString() ?? "unknown"}";
				return Result(step2, entitySnapshot2, detail2);
			}
			FabricProjectileObservation valueOrDefault3 = observations.GetValueOrDefault(projectileIds[0]);
			bool value9 = valueOrDefault3?.Outcome == "entity-hit" && valueOrDefault3.ImpactEntityUuid == _testEntityUuid;
			string detail3 = $"projectileType={attackProfile2}; potionEffects={valueOrDefault3?.PotionEffects ?? "unknown"}; projectileUuid={launched.ProjectileUuid}; outcome={valueOrDefault3?.Outcome ?? "unobserved"}; impactEntityUuid={valueOrDefault3?.ImpactEntityUuid ?? "none"}; targetUuid={_testEntityUuid}; vehicleUuid={_managedVehicleUuid ?? "none"}; targetHit={value9}; projectileHealth={valueOrDefault3?.HealthBefore?.ToString("0.##") ?? "?"}->{valueOrDefault3?.HealthAfter?.ToString("0.##") ?? "?"}; lastPosition=({valueOrDefault3?.X.ToString("0.##") ?? "?"},{valueOrDefault3?.Y.ToString("0.##") ?? "?"},{valueOrDefault3?.Z.ToString("0.##") ?? "?"}); lastVelocity=({valueOrDefault3?.VelocityX.ToString("0.##") ?? "?"},{valueOrDefault3?.VelocityY.ToString("0.##") ?? "?"},{valueOrDefault3?.VelocityZ.ToString("0.##") ?? "?"}); teleportRequested={launched.TeleportRequested}; teleportSucceeded={launched.TeleportSucceeded}; {launched.TeleportDiagnostics}";
			return Result(step2, entitySnapshot2, detail3);
		}
		int hitsApplied;
		if (step2.Action == "environment.configure")
		{
			if (_testEntityUuid == null || !step2.Arguments.TryGetValue("hazard", out attackProfile2) || !FabricBridgeContract.SupportsHazard(attackProfile2))
			{
				throw new InvalidOperationException("Environmental test requires a managed Enderman and an allowlisted hazard.");
			}
			switch (attackProfile2)
			{
			case "water-pulse":
			case "water-source-pulse":
			case "water-flowing-pulse":
			case "water-shallow-pulse":
			case "water-deep-pulse":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			completeReadback = flag;
			string text = attackProfile2 switch
			{
				"water-source-pulse" => "water-source", 
				"water-flowing-pulse" => "water-flowing", 
				"water-shallow-pulse" => "water-shallow", 
				"water-deep-pulse" => "water-deep", 
				"water-pulse" => "water", 
				_ => attackProfile2, 
			};
			response = await _bridge.ExecuteAllowlistedActionAsync("configure_hazard", _testEntityUuid, completeReadback ? text : attackProfile2, null, 1, null, cancellationToken);
			if (attackProfile2 == "vehicle-boat")
			{
				string text2 = (from value in response.FixtureDiagnostics?.Split(';')
					select value.Trim()).FirstOrDefault((string value) => value.StartsWith("vehicleUuid=", StringComparison.Ordinal));
				FabricJavaGameAdapter fabricJavaGameAdapter = this;
				object managedVehicleUuid;
				if (text2 != null)
				{
					string text3 = text2;
					int i = "vehicleUuid=".Length;
					managedVehicleUuid = text3.Substring(i, text3.Length - i);
				}
				else
				{
					managedVehicleUuid = null;
				}
				fabricJavaGameAdapter._managedVehicleUuid = (string?)managedVehicleUuid;
				if (string.IsNullOrWhiteSpace(_managedVehicleUuid) || !response.FixtureDiagnostics.Contains("mounted=true", StringComparison.Ordinal))
				{
					return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: exact run-owned boat and mounted-passenger state were not confirmed.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
				}
				FabricObservationSnapshot fabricObservationSnapshot3 = await _bridge.ObserveAsync(cancellationToken);
				EntitySnapshot entity4 = Map(fabricObservationSnapshot3.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == _testEntityUuid), fabricObservationSnapshot3.Deaths);
				return Result(step2, entity4, "Created one run-owned oak boat and verified the managed Enderman is riding it. vehicleUuid=" + _managedVehicleUuid + "; fixture=" + response.FixtureDiagnostics);
			}
			if (completeReadback)
			{
				long serverTick = (await _bridge.GetStatusAsync(cancellationToken)).ServerTick;
				if (!(await WaitForServerTickAdvanceAsync(serverTick, 1, TimeSpan.FromSeconds(3.0), cancellationToken)))
				{
					return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: the water contact did not remain active for one authoritative server tick.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
				}
				FabricActionResponse waterCleared = await _bridge.ExecuteAllowlistedActionAsync("configure_hazard", _testEntityUuid, "water-clear", null, 1, null, cancellationToken);
				FabricObservationSnapshot fabricObservationSnapshot4 = await _bridge.ObserveAsync(cancellationToken);
				EntitySnapshot entitySnapshot3 = Map(fabricObservationSnapshot4.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == _testEntityUuid), fabricObservationSnapshot4.Deaths);
				if ((object)entitySnapshot3 != null)
				{
					int? fireTicksAfterAction = waterCleared.FireTicksAfterAction;
					if (fireTicksAfterAction.HasValue)
					{
						int valueOrDefault4 = fireTicksAfterAction.GetValueOrDefault();
						entitySnapshot3 = entitySnapshot3 with
						{
							FireTicks = valueOrDefault4
						};
					}
				}
				return Result(step2, entitySnapshot3, $"Applied {attackProfile2} at the managed Enderman for one confirmed server tick, removed the water, and recorded post-contact state. HealthAfter={entitySnapshot3?.Health?.ToString("0.##") ?? "unknown"}; Fixture diagnostics: {response.FixtureDiagnostics ?? "no fluid state diagnostics"}; teleport destination is observed but is not a deterministic pass condition.");
			}
			if (attackProfile2 == "cactus-pulse")
			{
				for (hitsApplied = 0; hitsApplied < 40; hitsApplied++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					FabricObservationSnapshot fabricObservationSnapshot5 = await _bridge.ObserveAsync(cancellationToken);
					EntitySnapshot entitySnapshot4 = Map(fabricObservationSnapshot5.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == _testEntityUuid), fabricObservationSnapshot5.Deaths);
					if ((object)entitySnapshot4 != null && (entitySnapshot4.DamageEvents?.Any((DamageEventSnapshot x) => x.Cause == "cactus")).GetValueOrDefault())
					{
						return Result(step2, entitySnapshot4, "Applied one-shot cactus contact; the bridge restored its managed cactus block after the first authoritative cactus damage event.");
					}
					await Task.Delay(100, cancellationToken);
				}
				return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: no authoritative cactus contact event arrived within four seconds.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
			}
			switch (attackProfile2)
			{
			case "rain":
			case "rain-shelter":
			case "rain-clear":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			string text4 = (flag ? $" WeatherRaining={response.WeatherRaining}; RainAtEntity={response.WeatherAtEntity}; {response.RainDiagnostics}." : "");
			if (attackProfile2 == "rain" && !response.WeatherAtEntity.GetValueOrDefault())
			{
				return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Rain precondition failed: weather is not reaching the Enderman's actual block position; this is setup-inconclusive, not a game behavior result." + text4, null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
			}
			if (attackProfile2 == "fire-clear")
			{
				FabricObservationSnapshot fabricObservationSnapshot6 = await _bridge.ObserveAsync(cancellationToken);
				EntitySnapshot entitySnapshot5 = Map(fabricObservationSnapshot6.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == _testEntityUuid), fabricObservationSnapshot6.Deaths);
				if ((object)entitySnapshot5 != null)
				{
					int? fireTicksAfterAction = response.FireTicksAfterAction;
					if (fireTicksAfterAction.HasValue)
					{
						int valueOrDefault5 = fireTicksAfterAction.GetValueOrDefault();
						entitySnapshot5 = entitySnapshot5 with
						{
							FireTicks = valueOrDefault5
						};
					}
				}
				return Result(step2, entitySnapshot5, "Removed only managed fire blocks and extinguished the managed entity; server fireTicks=" + (response.FireTicksAfterAction?.ToString() ?? "unknown") + ".");
			}
			switch (attackProfile2)
			{
			case "health-6":
			case "lava":
			case "lava-escape":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				FabricObservationSnapshot fabricObservationSnapshot7 = await _bridge.ObserveAsync(cancellationToken);
				EntitySnapshot entity5 = Map(fabricObservationSnapshot7.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == _testEntityUuid), fabricObservationSnapshot7.Deaths);
				string value10 = ((attackProfile2 == "health-6") ? " RequestedHealth=6." : "");
				string text3 = attackProfile2;
				flag = ((text3 == "lava" || text3 == "lava-escape") ? true : false);
				string value11 = (flag ? (" Fixture diagnostics: " + (response.FixtureDiagnostics ?? "missing") + ".") : "");
				return Result(step2, entity5, $"Configured {attackProfile2} on the managed Enderman; authoritative state captured immediately.{value10}{value11}");
			}
			switch (attackProfile2)
			{
			case "fire-stationary":
			case "projectile-wall":
			case "rain-shelter":
			case "vehicle-boat":
			case "water-source":
			case "water-flowing":
			case "water-shallow":
			case "fire-lava":
			case "ice-slope":
			case "disable-target-ai":
			case "fire-lethal-pulse":
			case "lava-lethal-pulse":
			case "water-deep":
			case "water-lethal-pulse":
			case "lingering-water-cloud":
			case "lingering-water-cloud-long":
			case "move-target-outside-cloud":
			case "water-blocked-teleport-attempt":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			string value12 = (flag ? (" Fixture diagnostics: " + (response.FixtureDiagnostics ?? "missing") + ".") : "");
			if (attackProfile2 == "move-target-outside-cloud")
			{
				FabricObservationSnapshot fabricObservationSnapshot8 = await _bridge.ObserveAsync(cancellationToken);
				EntitySnapshot entity6 = Map(fabricObservationSnapshot8.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == _testEntityUuid), fabricObservationSnapshot8.Deaths);
				return Result(step2, entity6, "Moved managed Enderman to a verified pad outside the water cloud. Fixture diagnostics: " + (response.FixtureDiagnostics ?? "missing") + ".");
			}
			return Result(step2, null, $"Applied the allowlisted {attackProfile2} fixture to the run-scoped Enderman.{text4}{value12}");
		}
		if (step2.Action == "time.advance")
		{
			double seconds = default(double);
			flag = !step2.Arguments.TryGetValue("seconds", out string value13) || !double.TryParse(value13, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds);
			if (!flag)
			{
				bool flag2 = ((seconds < 0.1 || seconds > 60.0) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				throw new InvalidOperationException("time.advance requires seconds between 0.1 and 60.");
			}
			await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
			return Result(step2, null, $"Observed {seconds:0.##} seconds of live Minecraft server ticks.");
		}
		if (step2.Action == "observation.request")
		{
			EntitySnapshot entity;
			if (_testEntityUuid != null && step2.Arguments.TryGetValue("until", out string value14) && value14 == "dead-or-timeout")
			{
				string value15;
				int result3;
				int num2 = ((step2.Arguments.TryGetValue("timeoutSeconds", out value15) && int.TryParse(value15, out result3)) ? Math.Clamp(result3, 1, 45) : 20);
				entity = await WaitForOutcomeAsync(_testEntityUuid, TimeSpan.FromSeconds(num2), cancellationToken);
			}
			else
			{
				FabricObservationSnapshot fabricObservationSnapshot9 = await _bridge.ObserveAsync(cancellationToken);
				entity = ((_testEntityUuid == null) ? null : Map(fabricObservationSnapshot9.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == _testEntityUuid), fabricObservationSnapshot9.Deaths));
			}
			attackProfile2 = (((object)entity == null) ? "No run-scoped Enderman observation exists." : $"Observed run-scoped entity: {entity.LifeState}.");
			if (_testEntityUuid != null)
			{
				FabricObservationSnapshot fabricObservationSnapshot10 = await _bridge.ObserveAsync(cancellationToken);
				bool value16 = fabricObservationSnapshot10.Status.ServerPid == _identity.ServerPid && fabricObservationSnapshot10.Status.InstanceId == _identity.InstanceId && fabricObservationSnapshot10.Status.TargetId == _identity.TargetId;
				int value17 = fabricObservationSnapshot10.Deaths.Count((FabricDeathFact item) => item.EntityUuid.Equals(_testEntityUuid, StringComparison.OrdinalIgnoreCase));
				attackProfile2 += $" serverAlive={value16}; deathFactsForTarget={value17}; serverTick={fabricObservationSnapshot10.Status.ServerTick}.";
			}
			if (_testEntityUuid != null && step2.Arguments.TryGetValue("captureDeathArtifacts", out string value18) && bool.TryParse(value18, out completeReadback) && completeReadback)
			{
				IReadOnlyList<FabricDeathArtifactObservation> readOnlyList2 = await _bridge.GetDeathArtifactsAsync(_testEntityUuid, cancellationToken);
				if (step2.Arguments.TryGetValue("artifactBaseline", out string value19) && bool.TryParse(value19, out disableTargetAi2) && disableTargetAi2)
				{
					_artifactBaselineUuids = readOnlyList2.Select((FabricDeathArtifactObservation item) => item.ArtifactUuid).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
					attackProfile2 += $" Death artifact baseline: count={readOnlyList2.Count}; UUID set captured.";
				}
				else
				{
					FabricDeathArtifactObservation[] array3 = readOnlyList2.Where((FabricDeathArtifactObservation item) => !_artifactBaselineUuids.Contains(item.ArtifactUuid)).ToArray();
					bool value20 = array3.Select((FabricDeathArtifactObservation item) => item.ArtifactUuid).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != array3.Length;
					bool value21 = array3.All((FabricDeathArtifactObservation item) => item.NearestDeathEntityUuid.Equals(_testEntityUuid, StringComparison.OrdinalIgnoreCase));
					int value22 = array3.Where((FabricDeathArtifactObservation item) => item.ArtifactType == "item").Sum((FabricDeathArtifactObservation item) => item.ItemCount);
					int value23 = array3.Where((FabricDeathArtifactObservation item) => item.ArtifactType == "item" && item.ItemId == "minecraft:ender_pearl").Sum((FabricDeathArtifactObservation item) => item.ItemCount);
					attackProfile2 += $" Death artifact delta: baseline={_artifactBaselineUuids.Count}; observed={readOnlyList2.Count}; new={array3.Length}; newXp={array3.Sum((FabricDeathArtifactObservation item) => item.ExperienceValue)}; newItemEntities={array3.Count((FabricDeathArtifactObservation item) => item.ArtifactType == "item")}; newItemCount={value22}; newPearls={value23}; allTargetUuidMatches={value21}; duplicateArtifactUuid={value20}; correlation={array3.FirstOrDefault()?.CorrelationMethod ?? "no-new-artifacts"}.";
				}
			}
			return Result(step2, entity, attackProfile2);
		}
		throw new InvalidOperationException("Java adapter action is not implemented.");
		IL_22b7:
		bool value24 = (byte)num != 0;
		int? num3 = entitySnapshot2?.DamageEvents?.Count;
		int? num4 = fabricEndermanObservation?.DamageEvents?.Count;
		int num5;
		if (num3.HasValue)
		{
			int valueOrDefault6 = num3.GetValueOrDefault();
			num5 = Math.Max(0, valueOrDefault6 - launched.FirstTargetDamageEventsBefore.Value);
		}
		else
		{
			num5 = -1;
		}
		int value25 = num5;
		int num6;
		if (num4.HasValue)
		{
			int valueOrDefault7 = num4.GetValueOrDefault();
			num6 = Math.Max(0, valueOrDefault7 - launched.SecondTargetDamageEventsBefore.Value);
		}
		else
		{
			num6 = -1;
		}
		int value26 = num6;
		FabricProjectileObservation valueOrDefault8 = observations.GetValueOrDefault(projectileIds[0]);
		float valueOrDefault9 = launched.SecondTargetHealthBefore.GetValueOrDefault();
		string detail4 = $"projectileType=arrow; projectileUuid={launched.ProjectileUuid}; outcome={valueOrDefault8?.Outcome ?? "unobserved"}; impactEntityUuid={valueOrDefault8?.ImpactEntityUuid ?? "none"}; firstTargetUuid={_testEntityUuid}; secondTargetUuid={launched.SecondTargetUuid}; firstHealthBefore={launched.BeforeHealth?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"}; firstHealthAfter={entitySnapshot2?.Health?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"}; firstDamageEventDelta={value25}; firstAlive={(object)entitySnapshot2 != null && entitySnapshot2.LifeState == EntityLifeState.Alive}; secondHealthBefore={valueOrDefault9.ToString("0.##", CultureInfo.InvariantCulture)}; secondHealthAfter={(((object)fabricEndermanObservation == null) ? "unknown" : fabricEndermanObservation.Health.ToString("0.##", CultureInfo.InvariantCulture))}; secondDamageEventDelta={value26}; secondArrowDamageEventDelta={array2.Length}; secondArrowDamageTick={array2.LastOrDefault()?.GameTick.ToString() ?? "unknown"}; secondAlive={fabricEndermanObservation?.LifeState == "alive"}; secondMovedAfterImpact={value24}; secondPositionAfter={(((object)fabricEndermanObservation == null) ? "unknown" : $"({fabricEndermanObservation.X:0.##},{fabricEndermanObservation.Y:0.##},{fabricEndermanObservation.Z:0.##})")}; projectileGameTick={valueOrDefault8?.GameTick.ToString() ?? "unknown"}; targetAiDisabled={launched.TargetAiDisabled}; teleportSucceeded={launched.TeleportSucceeded}; placementVerified={launched.SecondTargetPlacementVerified}; limitation=server-side controlled fixture; no player bow input or autonomous Enderman AI";
		List<QaEvent> list = new List<QaEvent>();
		if ((object)entitySnapshot2 != null)
		{
			list.Add(new QaEvent(_runId ?? "", step2.Order, "projectile-target", entitySnapshot2.RunEntityId, DateTimeOffset.UtcNow, entitySnapshot2.GameTick, $"role=first;uuid={_testEntityUuid};healthBefore={launched.BeforeHealth?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"};healthAfter={entitySnapshot2.Health?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"};damageEventDelta={value25};alive={entitySnapshot2.LifeState == EntityLifeState.Alive}", "minecraft-java-fabric-authoritative"));
		}
		if ((object)fabricEndermanObservation != null)
		{
			list.Add(new QaEvent(_runId ?? "", step2.Order + 1, "projectile-target", fabricEndermanObservation.Uuid, DateTimeOffset.UtcNow, fabricEndermanObservation.GameTick, $"role=second;uuid={fabricEndermanObservation.Uuid};healthBefore={valueOrDefault9.ToString("0.##", CultureInfo.InvariantCulture)};healthAfter={fabricEndermanObservation.Health.ToString("0.##", CultureInfo.InvariantCulture)};damageEventDelta={value26};arrowDamageEventDelta={array2.Length};arrowDamageTick={array2.LastOrDefault()?.GameTick.ToString() ?? "unknown"};alive={fabricEndermanObservation.LifeState == "alive"}", "minecraft-java-fabric-authoritative"));
		}
		if ((object)valueOrDefault8 != null)
		{
			list.Add(new QaEvent(_runId ?? "", step2.Order + 2, "projectile-impact", valueOrDefault8.ProjectileUuid, DateTimeOffset.UtcNow, valueOrDefault8.GameTick, $"outcome={valueOrDefault8.Outcome};impactEntityUuid={valueOrDefault8.ImpactEntityUuid};targetUuid={valueOrDefault8.TargetUuid};projectileType={valueOrDefault8.ProjectileType}", "minecraft-java-fabric-authoritative"));
		}
		return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: true, detail4, entitySnapshot2, list, VisualCaptureAvailable: false);
		IL_0a3c:
		if (flag)
		{
			return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: true, "Skipped attack because the managed target was already terminal; no Java bridge action was sent.", _lastEntity, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
		}
		if (_testEntityUuid == null)
		{
			throw new InvalidOperationException("The plan must spawn a managed Enderman before a simulated player attack.");
		}
		if (!step2.Arguments.TryGetValue("itemId", out string itemId))
		{
			itemId = "bare_hand";
		}
		step2.Arguments.TryGetValue("attackProfile", out attackProfile2);
		string value27;
		int result4;
		int hitCount = ((!step2.Arguments.TryGetValue("hitCount", out value27) || !int.TryParse(value27, out result4)) ? 1 : result4);
		if ((hitCount < 1 || hitCount > 64) ? true : false)
		{
			throw new InvalidOperationException("Player attack repeat count is outside 1–64.");
		}
		bool result5 = default(bool);
		completeReadback = step2.Arguments.TryGetValue("teleportAfterHit", out string value28) && bool.TryParse(value28, out result5) && result5;
		bool result6 = default(bool);
		disableTargetAi2 = step2.Arguments.TryGetValue("disableTargetAi", out string value29) && bool.TryParse(value29, out result6) && result6;
		if (completeReadback && hitCount != 1)
		{
			throw new InvalidOperationException("The controlled teleport transition is bounded to exactly one attack.");
		}
		response = null;
		float? firstHealth = null;
		hitsApplied = 0;
		for (int hit = 0; hit < hitCount; hit++)
		{
			flag = (object)response != null;
			if (flag)
			{
				flag = !(await WaitForServerTickAdvanceAsync(response.ServerTick, 20, TimeSpan.FromSeconds(3.0), cancellationToken));
			}
			if (flag)
			{
				FabricObservationSnapshot fabricObservationSnapshot11 = await _bridge.ObserveAsync(cancellationToken);
				EntitySnapshot entity7 = Map(fabricObservationSnapshot11.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == _testEntityUuid), fabricObservationSnapshot11.Deaths);
				return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, $"Setup-inconclusive: server tick did not advance by 20 within 3 seconds after {hitsApplied} applied hit(s); stopped before hit {hit + 1}.", entity7, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
			}
			FabricActionResponse fabricActionResponse = response;
			if ((object)fabricActionResponse != null && fabricActionResponse.TargetAlive == false)
			{
				break;
			}
			step2.Arguments.TryGetValue("environmentDamageSource", out string value30);
			response = await _bridge.ExecuteAllowlistedActionAsync("player_attack_test_enderman", _testEntityUuid, null, itemId, 1, attackProfile2, cancellationToken, completeReadback, value30, disableTargetAi2);
			float? num7 = firstHealth;
			if (!num7.HasValue)
			{
				firstHealth = response.BeforeHealth;
			}
			num7 = response.AfterHealth;
			if (num7.HasValue)
			{
				float valueOrDefault10 = num7.GetValueOrDefault();
				num7 = response.BeforeHealth;
				if (num7.HasValue)
				{
					float valueOrDefault11 = num7.GetValueOrDefault();
					if (valueOrDefault10 < valueOrDefault11)
					{
						hitsApplied++;
					}
				}
			}
			if (response.TargetAlive == false)
			{
				break;
			}
		}
		if ((object)response == null)
		{
			throw new InvalidOperationException("Player attack loop produced no action result.");
		}
		if (attackProfile2 == "sharpness1" && response.ProfileEnchantmentLevel.GetValueOrDefault() != 1)
		{
			return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: Sharpness I was requested but the actually held weapon reports level " + (response.ProfileEnchantmentLevel?.ToString() ?? "unknown") + ".", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
		}
		if (attackProfile2 == "looting1" && response.ProfileEnchantmentLevel.GetValueOrDefault() != 1)
		{
			return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: Looting I was requested but the actually held weapon reports level " + (response.ProfileEnchantmentLevel?.ToString() ?? "unknown") + ".", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
		}
		if (attackProfile2 == "customName1" && !response.ProfileComponentPresent.GetValueOrDefault())
		{
			return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: CUSTOM_NAME item component was requested but not read back on the held weapon.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
		}
		string expected = (response.TargetAlive.GetValueOrDefault() ? "alive" : "dead");
		EntitySnapshot entity8 = await WaitForEntityAsync(_testEntityUuid, expected, cancellationToken);
		string value31 = ((attackProfile2 == null && response.AttackProfile == null) ? "" : $" profile={response.AttackProfile ?? attackProfile2}; applied={response.ProfileApplied?.ToString() ?? "unknown"}; profileEnchantmentLevel={response.ProfileEnchantmentLevel?.ToString() ?? "unknown"}; customComponentPresent={response.ProfileComponentPresent?.ToString() ?? "unknown"}; onGround={response.AttackerOnGround?.ToString() ?? "unknown"}; sprinting={response.AttackerSprinting?.ToString() ?? "unknown"}; crouching={response.AttackerCrouching?.ToString() ?? "unknown"}; criticalEligible={response.CriticalEligible?.ToString() ?? "unknown"}; fireTicksAfterHit={response.FireTicksAfterHit?.ToString() ?? "unknown"}; weaponDurabilityAfter={response.WeaponDurabilityAfter?.ToString() ?? "unknown"}.");
		string value32 = (completeReadback ? $" teleportRequested={response.TeleportRequested}; teleportSucceeded={response.TeleportSucceeded}; {response.TeleportDiagnostics}." : "");
		string environmentDamageSource = response.EnvironmentDamageSource;
		string value33 = ((environmentDamageSource != null) ? $" pairedEnvironment={environmentDamageSource}; applied={response.EnvironmentDamageApplied}; HP={response.EnvironmentHealthBefore?.ToString("0.##") ?? "?"}→{response.EnvironmentHealthAfter?.ToString("0.##") ?? "?"}." : "");
		return Result(step2, entity8, $"Minecraft server Player.attack used {response.ItemId ?? itemId} (held {response.HeldItemId ?? "?"}): {hitsApplied}/{hitCount} hits applied; multi-hit pacing waits for server tick +20 to clear hurt immunity (not full attack-cooldown priming); attackStrengthScale={response.AttackStrengthScale?.ToString("0.##") ?? "?"}, baseDamage={response.BaseAttackDamage?.ToString("0.##") ?? "?"}, itemBonus={response.ItemAttackBonus?.ToString("0.##") ?? "?"}; health {firstHealth?.ToString("0.##") ?? "?"} → {response.AfterHealth?.ToString("0.##") ?? "?"}; attacker {response.AttackerUuid ?? "unknown"}; targetAiDisabled={response.TargetAiDisabled.GetValueOrDefault()}.{value31}{value32}{value33}");
	}

	private async Task<AdapterActionResult> ExecuteProjectileVolleyAsync(PlannedStep step, CancellationToken cancellationToken)
	{
		PlannedStep step2 = step;
		string value;
		int count = default(int);
		bool flag = _testEntityUuid == null || !step2.Arguments.TryGetValue("projectileCount", out value) || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count);
		if (!flag)
		{
			bool flag2 = ((count < 2 || count > 8) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("Projectile volley requires a managed Enderman and a fixed count from 2–8.");
		}
		FabricObservationSnapshot fabricObservationSnapshot = await _bridge.ObserveAsync(cancellationToken);
		FabricEndermanObservation beforeObservation = fabricObservationSnapshot.Endermen.FirstOrDefault((FabricEndermanObservation item) => item.Uuid == _testEntityUuid);
		if ((object)beforeObservation == null || beforeObservation.LifeState != "alive" || fabricObservationSnapshot.Status.ServerPid != _identity.ServerPid)
		{
			return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: the live managed Enderman and owned server were not confirmed before volley.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
		}
		FabricProjectileVolleyReceipt launch = await _bridge.ThrowProjectileVolleyAsync(_testEntityUuid, count, cancellationToken);
		if (Math.Abs(launch.TargetHealthAtLaunch - beforeObservation.Health) > 0.01f)
		{
			return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: target health changed between the preflight snapshot and volley launch; receipt retained for reconciliation.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
		}
		Dictionary<string, FabricProjectileObservation> projectiles = new Dictionary<string, FabricProjectileObservation>(StringComparer.OrdinalIgnoreCase);
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(8.0);
		while (DateTimeOffset.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<FabricProjectileObservation> source = await _bridge.GetProjectilesAsync(cancellationToken);
			foreach (string id2 in launch.ProjectileUuids)
			{
				FabricProjectileObservation fabricProjectileObservation = source.FirstOrDefault((FabricProjectileObservation item) => item.ProjectileUuid.Equals(id2, StringComparison.OrdinalIgnoreCase));
				if ((object)fabricProjectileObservation != null)
				{
					projectiles[id2] = fabricProjectileObservation;
				}
			}
			if (launch.ProjectileUuids.All((string id) => projectiles.TryGetValue(id, out FabricProjectileObservation value8) && value8.Outcome != "pending"))
			{
				break;
			}
			await Task.Delay(100, cancellationToken);
		}
		FabricObservationSnapshot fabricObservationSnapshot2 = await _bridge.ObserveAsync(cancellationToken);
		if (fabricObservationSnapshot2.Status.ServerPid != _identity.ServerPid || fabricObservationSnapshot2.Status.InstanceId != _identity.InstanceId || fabricObservationSnapshot2.Status.TargetId != _identity.TargetId)
		{
			return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: false, "Setup-inconclusive: server identity changed while reconciling volley projectiles.", null, Array.Empty<QaEvent>(), VisualCaptureAvailable: false);
		}
		EntitySnapshot entitySnapshot = Map(fabricObservationSnapshot2.Endermen.FirstOrDefault((FabricEndermanObservation item) => item.Uuid == _testEntityUuid), fabricObservationSnapshot2.Deaths);
		int value2 = projectiles.Values.Count((FabricProjectileObservation item) => item.Outcome == "entity-hit" && string.Equals(item.ImpactEntityUuid, _testEntityUuid, StringComparison.OrdinalIgnoreCase));
		int value3 = projectiles.Values.Count((FabricProjectileObservation item) => item.Outcome != "pending");
		int value4 = fabricObservationSnapshot2.Deaths.Count((FabricDeathFact item) => item.EntityUuid.Equals(_testEntityUuid, StringComparison.OrdinalIgnoreCase));
		int num = beforeObservation.DamageEvents?.Count ?? 0;
		int valueOrDefault = (fabricObservationSnapshot2.Endermen.FirstOrDefault((FabricEndermanObservation item) => item.Uuid == _testEntityUuid)?.DamageEvents?.Count).GetValueOrDefault(num);
		WorldPositionSnapshot worldPositionSnapshot = entitySnapshot?.Position;
		double num2 = (((object)worldPositionSnapshot != null) ? Math.Sqrt(Math.Pow(worldPositionSnapshot.X - beforeObservation.X, 2.0) + Math.Pow(worldPositionSnapshot.Z - beforeObservation.Z, 2.0)) : double.NaN);
		WorldPositionSnapshot worldPositionSnapshot2 = entitySnapshot?.Position;
		double num3 = (((object)worldPositionSnapshot2 != null) ? (worldPositionSnapshot2.Y - beforeObservation.Y) : double.NaN);
		string value5 = string.Join(",", launch.ProjectileUuids.Select(delegate(string id)
		{
			FabricProjectileObservation valueOrDefault3 = projectiles.GetValueOrDefault(id);
			long? num4 = valueOrDefault3?.ImpactGameTick;
			object obj;
			if (num4.HasValue)
			{
				long valueOrDefault4 = num4.GetValueOrDefault();
				obj = $"impactTick={valueOrDefault4}:hp={valueOrDefault3.ImpactHealthBefore?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"}->{valueOrDefault3.ImpactHealthAfter?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"}:damageEvents={valueOrDefault3.ImpactDamageEventsBefore?.ToString() ?? "unknown"}->{valueOrDefault3.ImpactDamageEventsAfter?.ToString() ?? "unknown"}:pos=({valueOrDefault3.ImpactXBefore?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"},{valueOrDefault3.ImpactYBefore?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"},{valueOrDefault3.ImpactZBefore?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"})->({valueOrDefault3.ImpactXAfter?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"},{valueOrDefault3.ImpactYAfter?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"},{valueOrDefault3.ImpactZAfter?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"})";
			}
			else
			{
				obj = "impactTelemetry=unavailable";
			}
			string value6 = (string)obj;
			int? num5 = valueOrDefault3?.ImpactTeleportAttemptCount;
			object obj4;
			if (num5.HasValue)
			{
				int valueOrDefault5 = num5.GetValueOrDefault();
				string obj2 = $"arrowTeleportAttempts={valueOrDefault5}:arrowTeleportSuccesses={valueOrDefault3.ImpactTeleportSuccessCount.GetValueOrDefault()}";
				num4 = valueOrDefault3.ImpactTeleportGameTick;
				object obj3;
				if (num4.HasValue)
				{
					long valueOrDefault6 = num4.GetValueOrDefault();
					obj3 = $":teleportTick={valueOrDefault6}:teleportFrom=({valueOrDefault3.ImpactTeleportFromX?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"},{valueOrDefault3.ImpactTeleportFromY?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"},{valueOrDefault3.ImpactTeleportFromZ?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"}):teleportTo=({valueOrDefault3.ImpactTeleportToX?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"},{valueOrDefault3.ImpactTeleportToY?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"},{valueOrDefault3.ImpactTeleportToZ?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"})";
				}
				else
				{
					obj3 = string.Empty;
				}
				obj4 = obj2 + (string?)obj3;
			}
			else
			{
				obj4 = "teleportTelemetry=unavailable";
			}
			string value7 = (string)obj4;
			return $"{id}:{valueOrDefault3?.Outcome ?? "unobserved"}:{valueOrDefault3?.ImpactEntityUuid ?? "none"}:{value6}:{value7}";
		}));
		string detail = $"requestId={launch.RequestId}; projectileCount={launch.ProjectileCount}; projectileUuids={string.Join(",", launch.ProjectileUuids)}; terminalProjectiles={value3}; targetHits={value2}; outcomes={value5}; targetUuid={_testEntityUuid}; hp={beforeObservation.Health:0.##}->{entitySnapshot?.Health?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"}; lifeState={entitySnapshot?.LifeState.ToString() ?? "unknown"}; deathCause={entitySnapshot?.DamageCause ?? "none"}; horizontalDisplacement={num2.ToString("0.##", CultureInfo.InvariantCulture)}; verticalDisplacement={num3.ToString("0.##", CultureInfo.InvariantCulture)}; targetDamageEvents={Math.Max(0, valueOrDefault - num)}; deathFactsForTarget={value4}; latencyCoverage={launch.LatencyCoverage}; serverAlive=true; serverTick={fabricObservationSnapshot2.Status.ServerTick}; fixtureContract={launch.FixtureContract}";
		QaEvent[] events = launch.ProjectileUuids.Select(delegate(string id, int index)
		{
			FabricProjectileObservation valueOrDefault2 = projectiles.GetValueOrDefault(id);
			return new QaEvent(_runId ?? "", step2.Order + index, "projectile-volley-member", id, DateTimeOffset.UtcNow, valueOrDefault2?.GameTick ?? launch.ServerTick, $"requestId={launch.RequestId}; outcome={valueOrDefault2?.Outcome ?? "unobserved"}; impactEntityUuid={valueOrDefault2?.ImpactEntityUuid ?? "none"}; impactGameTick={valueOrDefault2?.ImpactGameTick?.ToString() ?? "unknown"}; impactHealth={valueOrDefault2?.ImpactHealthBefore?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"}->{valueOrDefault2?.ImpactHealthAfter?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown"}; impactDamageEvents={valueOrDefault2?.ImpactDamageEventsBefore?.ToString() ?? "unknown"}->{valueOrDefault2?.ImpactDamageEventsAfter?.ToString() ?? "unknown"}; teleportAttempts={valueOrDefault2?.ImpactTeleportAttemptCount?.ToString() ?? "unknown"}; teleportSuccesses={valueOrDefault2?.ImpactTeleportSuccessCount?.ToString() ?? "unknown"}; teleportGameTick={valueOrDefault2?.ImpactTeleportGameTick?.ToString() ?? "none"}; targetUuid={_testEntityUuid}; type={valueOrDefault2?.ProjectileType ?? "unknown"}", "minecraft-java-fabric-authoritative");
		}).ToArray();
		return new AdapterActionResult(step2.StepId, step2.Action, Succeeded: true, detail, entitySnapshot, events, VisualCaptureAvailable: false);
	}

	private async Task<EntitySnapshot?> WaitForOutcomeAsync(string uuid, TimeSpan timeout, CancellationToken cancellationToken)
	{
		string uuid2 = uuid;
		DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
		EntitySnapshot last = null;
		while (DateTimeOffset.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			FabricObservationSnapshot fabricObservationSnapshot = await _bridge.ObserveAsync(cancellationToken);
			last = Map(fabricObservationSnapshot.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == uuid2), fabricObservationSnapshot.Deaths) ?? last;
			EntityLifeState? entityLifeState = last?.LifeState;
			bool flag;
			if (entityLifeState.HasValue)
			{
				EntityLifeState valueOrDefault = entityLifeState.GetValueOrDefault();
				if ((uint)(valueOrDefault - 1) <= 1u)
				{
					flag = true;
					goto IL_0168;
				}
			}
			flag = false;
			goto IL_0168;
			IL_0168:
			if (flag)
			{
				return last;
			}
			await Task.Delay(200, cancellationToken);
		}
		return last;
	}

	private async Task<bool> WaitForServerTickAdvanceAsync(long startingTick, int minimumTicks, TimeSpan timeout, CancellationToken cancellationToken)
	{
		if ((minimumTicks < 1 || minimumTicks > 200) ? true : false)
		{
			throw new ArgumentOutOfRangeException("minimumTicks");
		}
		long requiredTick = checked(startingTick + minimumTicks);
		using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(timeout);
		Stopwatch timer = Stopwatch.StartNew();
		try
		{
			while (timer.Elapsed < timeout)
			{
				timeoutSource.Token.ThrowIfCancellationRequested();
				FabricBridgeStatus fabricBridgeStatus = await _bridge.GetStatusAsync(timeoutSource.Token);
				if (fabricBridgeStatus.ServerPid != _identity.ServerPid || fabricBridgeStatus.InstanceId != _identity.InstanceId || fabricBridgeStatus.TargetId != _identity.TargetId)
				{
					throw new InvalidOperationException("Bridge identity changed while waiting for server tick pacing.");
				}
				if (fabricBridgeStatus.ServerTick >= requiredTick)
				{
					return true;
				}
				await Task.Delay(TimeSpan.FromMilliseconds(50.0), timeoutSource.Token);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		return false;
	}

	private async Task<EntitySnapshot> WaitForEntityAsync(string uuid, string expected, CancellationToken cancellationToken)
	{
		string uuid2 = uuid;
		for (int i = 0; i < 30; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			FabricObservationSnapshot fabricObservationSnapshot = await _bridge.ObserveAsync(cancellationToken);
			EntitySnapshot entitySnapshot = Map(fabricObservationSnapshot.Endermen.FirstOrDefault((FabricEndermanObservation x) => x.Uuid == uuid2), fabricObservationSnapshot.Deaths);
			bool flag = (object)entitySnapshot != null;
			if (flag)
			{
				bool flag3;
				if (expected == "dead")
				{
					EntityLifeState lifeState = entitySnapshot.LifeState;
					bool flag2 = (uint)(lifeState - 1) <= 1u;
					flag3 = flag2;
				}
				else
				{
					flag3 = entitySnapshot.LifeState == EntityLifeState.Alive;
				}
				flag = flag3;
			}
			if (flag)
			{
				return entitySnapshot;
			}
			await Task.Delay(200, cancellationToken);
		}
		throw new TimeoutException("Timed out waiting for the server to observe the managed Enderman as " + expected + ".");
	}

	private EntitySnapshot? Map(FabricEndermanObservation? observation, IReadOnlyList<FabricDeathFact> deaths)
	{
		FabricEndermanObservation observation2 = observation;
		if ((object)observation2 == null)
		{
			return null;
		}
		FabricDeathFact fabricDeathFact = deaths.LastOrDefault((FabricDeathFact x) => x.EntityUuid == observation2.Uuid);
		EntityLifeState lifeState = observation2.LifeState switch
		{
			"alive" => EntityLifeState.Alive, 
			"dead" => EntityLifeState.Dead, 
			"removed" => EntityLifeState.Removed, 
			_ => EntityLifeState.Unknown, 
		};
		DateTimeOffset result;
		DateTimeOffset result2;
		return new EntitySnapshot(observation2.Uuid, observation2.EntityId.ToString(), observation2.EntityType, lifeState, observation2.Health, 40.0, fabricDeathFact?.Cause ?? observation2.DamageCause, observation2.GameTick, DateTimeOffset.TryParse(observation2.ObservedAt, out result) ? result : DateTimeOffset.UtcNow, "minecraft-java-fabric-authoritative", new WorldPositionSnapshot(observation2.X, observation2.Y, observation2.Z, observation2.Dimension), observation2.FrozenTicks, observation2.FireTicks, observation2.DamageEvents?.Select((FabricDamageFact x) => new DamageEventSnapshot(x.Cause, x.GameTick, x.BaseDamage, x.DamageTaken, x.HealthAfter, x.Blocked, DateTimeOffset.TryParse(x.ObservedAt, out result2) ? result2 : DateTimeOffset.UtcNow, x.AttackerUuid)).ToArray(), fabricDeathFact?.AttackerUuid, observation2.PositionTrace?.Select((FabricPositionSample sample) => new PositionTraceSnapshot(sample.GameTick, new WorldPositionSnapshot(sample.X, sample.Y, sample.Z, sample.Dimension), sample.CaptureSource, sample.CloudHorizontalDistance, sample.CloudRadius, sample.WithinCloudRadius)).ToArray());
	}

	private AdapterActionResult Result(PlannedStep step, EntitySnapshot? entity, string detail)
	{
		if ((object)entity != null)
		{
			_lastEntity = entity;
		}
		int? num = entity?.FrozenTicks;
		if (num.HasValue)
		{
			int valueOrDefault = num.GetValueOrDefault();
			detail += $"; frozenTicks={valueOrDefault}";
		}
		num = entity?.FireTicks;
		if (num.HasValue)
		{
			int valueOrDefault2 = num.GetValueOrDefault();
			detail += $"; fireTicks={valueOrDefault2}";
		}
		IReadOnlyList<DamageEventSnapshot> readOnlyList = entity?.DamageEvents;
		if (readOnlyList != null && readOnlyList.Count > 0)
		{
			detail = detail + "; damageEvents=" + string.Join("|", readOnlyList.Select((DamageEventSnapshot x) => $"{x.Cause}@{x.GameTick}:base={x.BaseDamage:0.##},taken={x.DamageTaken:0.##},hp={x.HealthAfter:0.##},blocked={x.Blocked}"));
		}
		QaEvent[] events = (((object)entity == null) ? Array.Empty<QaEvent>() : new QaEvent[1]
		{
			new QaEvent(_runId ?? "", step.Order, "entity.observed", entity.RunEntityId, DateTimeOffset.UtcNow, entity.GameTick, detail, entity.Provenance)
		});
		return new AdapterActionResult(step.StepId, step.Action, Succeeded: true, detail, entity, events, VisualCaptureAvailable: false);
	}

	public async Task StopAsync(long cancellationGeneration, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!_stopped)
		{
			_stopped = true;
			await ManagedFixtureCleanup.ReleaseAllAsync(_managedEntityUuids.AsEnumerable().Reverse(), async delegate(string uuid)
			{
				FabricActionResponse fabricActionResponse = await _bridge.ExecuteAllowlistedActionAsync("release_test_fixture", uuid, null, null, 1, null, CancellationToken.None);
				return (Action: fabricActionResponse.Action, EntityUuid: fabricActionResponse.EntityUuid, FixtureReleased: fabricActionResponse.FixtureReleased);
			});
		}
	}

	public ValueTask DisposeAsync()
	{
		_bridge.Dispose();
		return ValueTask.CompletedTask;
	}
}
