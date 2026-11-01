using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Adapters;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Manager;

public sealed class ScenarioRunner
{
	public async Task<ScenarioRunResult> RunAsync(string runId, QaPlan plan, IGameAdapter adapter, CancellationToken cancellationToken)
	{
		if (plan.TargetId != adapter.Target.TargetId || plan.AdapterId != adapter.Capabilities.AdapterId)
		{
			throw new InvalidOperationException("Plan target/adapter does not match the selected adapter.");
		}
		List<AdapterActionResult> actions = new List<AdapterActionResult>();
		EntitySnapshot before = null;
		EntitySnapshot after = null;
		RunStatus status = RunStatus.Running;
		string limitation = (adapter.Capabilities.Screenshot ? "" : "This dedicated-server adapter cannot capture rendered Minecraft client pixels.");
		long generation = 0L;
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		int timeoutSeconds = plan.TimeoutSeconds;
		if ((timeoutSeconds < 1 || timeoutSeconds > 600) ? true : false)
		{
			throw new InvalidOperationException("Plan timeout is outside the manager's 1–600 second safety bound.");
		}
		timeout.CancelAfter(TimeSpan.FromSeconds(plan.TimeoutSeconds));
		try
		{
			_ = 1;
			try
			{
				await adapter.InitializeAsync(runId, plan, timeout.Token);
				foreach (PlannedStep step in plan.Steps)
				{
					timeout.Token.ThrowIfCancellationRequested();
					AdapterActionResult adapterActionResult = await adapter.ExecuteAsync(plan, step, generation, timeout.Token);
					actions.Add(adapterActionResult);
					EntitySnapshot entity = adapterActionResult.Entity;
					if ((object)entity != null)
					{
						if ((object)before == null)
						{
							before = ((entity.LifeState == EntityLifeState.Alive) ? entity : null);
						}
						after = entity;
					}
					if (!adapterActionResult.Succeeded)
					{
						status = RunStatus.Failed;
						limitation = adapterActionResult.Detail;
						break;
					}
				}
				if (status == RunStatus.Running)
				{
					status = RunStatus.Completed;
				}
			}
			catch (OperationCanceledException)
			{
				status = RunStatus.Stopped;
				limitation = (cancellationToken.IsCancellationRequested ? "Run cancelled by the operator; remaining plan steps were not dispatched." : $"Run reached the manager's {plan.TimeoutSeconds}-second scenario limit; remaining plan steps were not dispatched.");
			}
			catch (Exception ex2)
			{
				status = RunStatus.Failed;
				limitation = ex2.Message;
			}
		}
		finally
		{
			try
			{
				long cancellationGeneration = generation + 1;
				await adapter.StopAsync(cancellationGeneration, CancellationToken.None);
			}
			catch (Exception ex3)
			{
				limitation = (string.IsNullOrWhiteSpace(limitation) ? ("Adapter stop failed: " + ex3.Message) : (limitation + " Adapter stop failed: " + ex3.Message));
				status = RunStatus.Failed;
			}
		}
		Verdict verdict = ((status == RunStatus.Completed) ? JudgeExpectedOutcome(before, after, plan.ExpectedOutcome, actions) : Verdict.Inconclusive);
		if (plan.ExpectedOutcome == "fire-ticks-cleared" && (actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "fire-active")?.Entity?.FireTicks).GetValueOrDefault() <= 0)
		{
			limitation = "precondition failed: positive server-authoritative fireTicks were not observed before extinguishing; no fire-clearance result can be inferred.";
		}
		if (status == RunStatus.Completed && verdict == Verdict.Inconclusive && string.IsNullOrEmpty(limitation))
		{
			limitation = "No authoritative before/after entity state was available.";
		}
		return new ScenarioRunResult(status, before, after, actions, verdict, limitation);
	}

	private static Verdict JudgeExpectedOutcome(EntitySnapshot? before, EntitySnapshot? after, string expectedOutcome, IReadOnlyList<AdapterActionResult> actions)
	{
		bool num4;
		bool flag2;
		bool flag3;
		int num5;
		int num2;
		WorldPositionSnapshot worldPositionSnapshot;
		int num3;
		bool flag4;
		bool flag5;
		bool flag7;
		bool flag;
		switch (expectedOutcome)
		{
		case "sweep-per-uuid-once":
		{
			AdapterActionResult adapterActionResult13 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "sweep");
			if ((object)adapterActionResult13 == null || !adapterActionResult13.Succeeded || (object)before == null || (object)after == null || !adapterActionResult13.Detail.Contains("profile=sweeping1", StringComparison.Ordinal) || !adapterActionResult13.Detail.Contains("enchantmentLevel=1", StringComparison.Ordinal) || !adapterActionResult13.Detail.Contains("exactUuidReadback=True", StringComparison.OrdinalIgnoreCase))
			{
				return Verdict.Inconclusive;
			}
			QaEvent[] array3 = adapterActionResult13.Events.Where((QaEvent item) => item.Kind == "sweep-target").ToArray();
			if (array3.Length != 3)
			{
				return Verdict.Inconclusive;
			}
			(QaEvent Event, Dictionary<string, string> Values)[] parsed = array3.Select((QaEvent item) => (Event: item, Values: ParseTokens(item.Detail))).ToArray();
			string? Value(string role, string key) => parsed.FirstOrDefault(p => p.Values.GetValueOrDefault("role") == role).Values?.GetValueOrDefault(key);
			string[] source = parsed.Select(((QaEvent Event, Dictionary<string, string> Values) item) => item.Values.GetValueOrDefault("uuid")).ToArray();
			if (source.Any(string.IsNullOrWhiteSpace) || source.Distinct<string>(StringComparer.Ordinal).Count() != 3)
			{
				return Verdict.Inconclusive;
			}
			if (!parsed.Select(((QaEvent Event, Dictionary<string, string> Values) item) => item.Values.GetValueOrDefault("role")).ToHashSet<string>(StringComparer.Ordinal).SetEquals(new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "primary", "eligible-neighbor", "outside-control" })))
			{
				return Verdict.Inconclusive;
			}
			string? text = Value("primary", "uuid");
			string? text2 = adapterActionResult13.Detail.Split(';', StringSplitOptions.TrimEntries).FirstOrDefault((string token) => token.StartsWith("attackerUuid=", StringComparison.Ordinal));
			object obj;
			if (text2 == null)
			{
				obj = null;
			}
			else
			{
				string text3 = text2;
				int length = "attackerUuid=".Length;
				obj = text3.Substring(length, text3.Length - length);
			}
			string text4 = (string)obj;
			if (text == null || string.IsNullOrWhiteSpace(text4))
			{
				return Verdict.Inconclusive;
			}
			if (!TryReadTokenDouble(Value("primary", "beforeHealth"), out var value2) || !TryReadTokenDouble(Value("primary", "afterHealth"), out var value3) || !TryReadTokenDouble(Value("eligible-neighbor", "beforeHealth"), out var value4) || !TryReadTokenDouble(Value("eligible-neighbor", "afterHealth"), out var value5) || !TryReadTokenDouble(Value("outside-control", "beforeHealth"), out var value6) || !TryReadTokenDouble(Value("outside-control", "afterHealth"), out var value7) || !TryReadTokenDouble(Value("eligible-neighbor", "distance"), out var value8) || !TryReadTokenDouble(Value("outside-control", "distance"), out var value9) || !int.TryParse(Value("primary", "damageEventDelta"), out var result6) || !int.TryParse(Value("primary", "playerDamageEventDelta"), out var result7) || !int.TryParse(Value("eligible-neighbor", "damageEventDelta"), out var result8) || !int.TryParse(Value("eligible-neighbor", "playerDamageEventDelta"), out var result9) || !int.TryParse(Value("outside-control", "damageEventDelta"), out var result10) || !long.TryParse(Value("primary", "gameTick"), out var result11) || !long.TryParse(Value("primary", "attackTick"), out var result12) || !long.TryParse(Value("eligible-neighbor", "gameTick"), out var result13) || !long.TryParse(Value("eligible-neighbor", "attackTick"), out var result14) || Value("primary", "attackerUuid") == null || Value("eligible-neighbor", "attackerUuid") == null || Value("primary", "attackerMatches") == null || Value("eligible-neighbor", "attackerMatches") == null || Value("eligible-neighbor", "alive") == null || Value("outside-control", "alive") == null)
			{
				return Verdict.Inconclusive;
			}
			if (result7 == 0 || value3 >= value2)
			{
				return Verdict.Inconclusive;
			}
			if (result6 != 1 || result7 != 1 || result8 != 1 || result9 != 1 || result10 != 0 || !(Value("primary", "attackerUuid") == text4) || !(Value("eligible-neighbor", "attackerUuid") == text4) || !string.Equals(Value("primary", "attackerMatches"), "True", StringComparison.OrdinalIgnoreCase) || !string.Equals(Value("eligible-neighbor", "attackerMatches"), "True", StringComparison.OrdinalIgnoreCase) || !(value5 < value4) || value7 != value6 || !string.Equals(Value("eligible-neighbor", "alive"), "True", StringComparison.OrdinalIgnoreCase) || !string.Equals(Value("outside-control", "alive"), "True", StringComparison.OrdinalIgnoreCase) || !(value8 > 0.5) || !(value8 < 1.5) || !(value9 > 2.0) || result11 != result12 || result13 != result14)
			{
				return Verdict.Fail;
			}
			return Verdict.Pass;
		}
		case "water-blocked-teleport-no-duplicate-damage":
		{
			AdapterActionResult adapterActionResult5 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "attempt-water-destination");
			if ((object)adapterActionResult5 == null || (object)after == null || (object)before == null || after.DamageEvents == null)
			{
				return Verdict.Inconclusive;
			}
			string detail2 = adapterActionResult5.Detail;
			if (!detail2.Contains("teleportAttempted=true", StringComparison.Ordinal) || !detail2.Contains("destinationWater=true", StringComparison.Ordinal) || !detail2.Contains("vanillaDestinationAllowed=false", StringComparison.Ordinal))
			{
				return Verdict.Inconclusive;
			}
			bool num6 = detail2.Contains("teleportSucceeded=false", StringComparison.Ordinal) && detail2.Contains("targetMoved=false", StringComparison.Ordinal);
			bool flag6 = after.Health == before.Health && after.DamageEvents.Count <= (before.DamageEvents?.Count ?? 0) && after.LifeState == EntityLifeState.Alive;
			if (!(num6 && flag6))
			{
				return Verdict.Fail;
			}
			return Verdict.Pass;
		}
		case "environment-player-attribution":
		{
			if ((object)after == null || after.DamageEvents == null)
			{
				return Verdict.Inconclusive;
			}
			DamageEventSnapshot[] array = after.DamageEvents.Where((DamageEventSnapshot x) => x.Cause == "inFire").ToArray();
			DamageEventSnapshot[] array2 = after.DamageEvents.Where((DamageEventSnapshot x) => x.Cause == "player").ToArray();
			if (array.Length != 1 || array2.Length > 1)
			{
				return Verdict.Fail;
			}
			num4 = array[0].GameTick == after.GameTick;
			flag2 = after.LifeState == EntityLifeState.Dead && after.DamageCause == "player" && !string.IsNullOrWhiteSpace(after.LastAttackerUuid);
			AdapterActionResult adapterActionResult2 = actions.FirstOrDefault((AdapterActionResult x) => x.StepId == "environment-and-attack");
			flag3 = adapterActionResult2?.Detail.Contains("1/1 hits applied", StringComparison.Ordinal) ?? false;
			if ((object)adapterActionResult2 != null)
			{
				string lastAttackerUuid = after.LastAttackerUuid;
				if (lastAttackerUuid != null)
				{
					num5 = (Regex.IsMatch(adapterActionResult2.Detail, "\\battacker\\s+" + Regex.Escape(lastAttackerUuid) + "(?:[.;,]|\\s|$)", RegexOptions.IgnoreCase) ? 1 : 0);
					goto IL_081d;
				}
			}
			num5 = 0;
			goto IL_081d;
		}
		case "bare-hand-player-kill":
		{
			if ((object)after == null)
			{
				return Verdict.Inconclusive;
			}
			if (after.LifeState != EntityLifeState.Dead)
			{
				return Verdict.Fail;
			}
			AdapterActionResult adapterActionResult4 = actions.LastOrDefault((AdapterActionResult x) => x.Action == "entity.player_attack" && x.Detail.Contains("Minecraft server Player.attack", StringComparison.OrdinalIgnoreCase));
			if ((object)adapterActionResult4 == null || !(after.DamageCause == "player") || string.IsNullOrWhiteSpace(after.LastAttackerUuid) || !adapterActionResult4.Detail.Contains("attacker " + after.LastAttackerUuid + ";", StringComparison.OrdinalIgnoreCase) || !Regex.Match(adapterActionResult4.Detail, "(?<hits>\\d+)/64 hits applied").Groups["hits"].Success || !int.TryParse(Regex.Match(adapterActionResult4.Detail, "(?<hits>\\d+)/64 hits applied").Groups["hits"].Value, out var result) || result < 1 || result > 64)
			{
				return Verdict.Fail;
			}
			return Verdict.Pass;
		}
		case "fire-death-artifacts-observed":
		{
			AdapterActionResult adapterActionResult14 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-baseline");
			AdapterActionResult adapterActionResult15 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "observe-fire-outcome");
			AdapterActionResult adapterActionResult16 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-after");
			if ((object)adapterActionResult14 == null || (object)adapterActionResult15 == null || (object)adapterActionResult16 == null || (object)after == null)
			{
				return Verdict.Inconclusive;
			}
			if (!adapterActionResult14.Detail.Contains("UUID set captured", StringComparison.Ordinal) || !adapterActionResult16.Detail.Contains("allTargetUuidMatches=True", StringComparison.Ordinal) || !adapterActionResult16.Detail.Contains("duplicateArtifactUuid=False", StringComparison.Ordinal))
			{
				return Verdict.Inconclusive;
			}
			Match match5 = Regex.Match(adapterActionResult16.Detail, "newXp=(?<value>\\d+)");
			Match match6 = Regex.Match(adapterActionResult16.Detail, "newItemCount=(?<value>\\d+)");
			if (!match5.Success || !match6.Success || !int.TryParse(match5.Groups["value"].Value, out var result15) || !int.TryParse(match6.Groups["value"].Value, out var result16))
			{
				return Verdict.Inconclusive;
			}
			flag = after.LifeState == EntityLifeState.Dead;
			if (flag)
			{
				string text3 = after.DamageCause;
				bool flag8 = ((text3 == "inFire" || text3 == "onFire") ? true : false);
				flag = flag8;
			}
			if (!flag || result15 != 0 || result16 > 1)
			{
				if (after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				return Verdict.Inconclusive;
			}
			return Verdict.Pass;
		}
		case "paired-looting-deaths-observed":
		{
			AdapterActionResult adapterActionResult17 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "control-kill");
			AdapterActionResult adapterActionResult18 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "looting-kill");
			AdapterActionResult adapterActionResult19 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "control-artifacts");
			AdapterActionResult adapterActionResult20 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "looting-artifacts");
			if ((object)adapterActionResult17 == null || (object)adapterActionResult18 == null || (object)adapterActionResult19 == null || (object)adapterActionResult20 == null)
			{
				return Verdict.Inconclusive;
			}
			EntitySnapshot? entity = adapterActionResult17.Entity;
			bool num7 = (object)entity != null && entity.LifeState == EntityLifeState.Dead && adapterActionResult17.Entity.DamageCause == "player";
			EntitySnapshot? entity2 = adapterActionResult18.Entity;
			bool flag9 = (object)entity2 != null && entity2.LifeState == EntityLifeState.Dead && adapterActionResult18.Entity.DamageCause == "player" && adapterActionResult18.Detail.Contains("profileEnchantmentLevel=1", StringComparison.Ordinal) && adapterActionResult18.Detail.Contains("applied=True", StringComparison.Ordinal);
			if (!(num7 && flag9) || !ValidArtifacts(adapterActionResult19.Detail) || !ValidArtifacts(adapterActionResult20.Detail))
			{
				return Verdict.Inconclusive;
			}
			return Verdict.Pass;
		}
		case "water-death-artifacts-observed":
		{
			AdapterActionResult adapterActionResult10 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "water-lethal");
			AdapterActionResult adapterActionResult11 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-baseline");
			AdapterActionResult adapterActionResult12 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-after");
			if ((object)adapterActionResult10 == null || (object)adapterActionResult11 == null || (object)adapterActionResult12 == null || (object)after == null)
			{
				return Verdict.Inconclusive;
			}
			string detail4 = adapterActionResult10.Detail;
			if (!detail4.Contains("source=drown", StringComparison.Ordinal) || !detail4.Contains("damageApplied=true", StringComparison.OrdinalIgnoreCase) || !adapterActionResult11.Detail.Contains("UUID set captured", StringComparison.Ordinal) || !adapterActionResult12.Detail.Contains("allTargetUuidMatches=True", StringComparison.Ordinal) || !adapterActionResult12.Detail.Contains("duplicateArtifactUuid=False", StringComparison.Ordinal))
			{
				return Verdict.Inconclusive;
			}
			Match match3 = Regex.Match(adapterActionResult12.Detail, "newXp=(?<value>\\d+)");
			Match match4 = Regex.Match(adapterActionResult12.Detail, "newPearls=(?<value>\\d+)");
			if (!match3.Success || !match4.Success || !int.TryParse(match3.Groups["value"].Value, out var result4) || !int.TryParse(match4.Groups["value"].Value, out var result5))
			{
				return Verdict.Inconclusive;
			}
			if (after.LifeState != EntityLifeState.Dead || !(after.DamageCause == "drown") || result4 != 0 || result5 > 1)
			{
				if (after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				return Verdict.Inconclusive;
			}
			return Verdict.Pass;
		}
		case "lava-death-artifacts-observed":
		{
			AdapterActionResult adapterActionResult6 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "lava-lethal");
			AdapterActionResult adapterActionResult7 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-baseline");
			AdapterActionResult adapterActionResult8 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-after");
			AdapterActionResult adapterActionResult9 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "lava-item-window");
			if ((object)adapterActionResult6 == null || (object)adapterActionResult7 == null || (object)adapterActionResult9 == null || !adapterActionResult9.Succeeded || (object)adapterActionResult8 == null || (object)after == null)
			{
				return Verdict.Inconclusive;
			}
			string detail3 = adapterActionResult6.Detail;
			if (!detail3.Contains("source=lava", StringComparison.Ordinal) || !detail3.Contains("lavaReadback=true", StringComparison.OrdinalIgnoreCase) || !detail3.Contains("damageApplied=true", StringComparison.OrdinalIgnoreCase) || !adapterActionResult7.Detail.Contains("UUID set captured", StringComparison.Ordinal) || !adapterActionResult8.Detail.Contains("allTargetUuidMatches=True", StringComparison.Ordinal) || !adapterActionResult8.Detail.Contains("duplicateArtifactUuid=False", StringComparison.Ordinal))
			{
				return Verdict.Inconclusive;
			}
			Match match = Regex.Match(adapterActionResult8.Detail, "newXp=(?<value>\\d+)");
			Match match2 = Regex.Match(adapterActionResult8.Detail, "newPearls=(?<value>\\d+)");
			if (!match.Success || !match2.Success || !int.TryParse(match.Groups["value"].Value, out var result2) || !int.TryParse(match2.Groups["value"].Value, out var result3))
			{
				return Verdict.Inconclusive;
			}
			if (after.LifeState != EntityLifeState.Dead || !(after.DamageCause == "lava") || result2 != 0 || result3 > 1)
			{
				if (after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				return Verdict.Inconclusive;
			}
			return Verdict.Pass;
		}
		case "custom-component-attack-recorded":
		{
			AdapterActionResult adapterActionResult3 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "component-attack");
			if ((object)adapterActionResult3 == null || !adapterActionResult3.Succeeded || (object)after == null)
			{
				return Verdict.Inconclusive;
			}
			if (!adapterActionResult3.Detail.Contains("customComponentPresent=True", StringComparison.Ordinal) || !adapterActionResult3.Detail.Contains("1/1 hits applied", StringComparison.Ordinal) || !adapterActionResult3.Detail.Contains("profile=customName1", StringComparison.Ordinal) || after.LifeState != EntityLifeState.Dead || !(after.DamageCause == "player"))
			{
				return Verdict.Inconclusive;
			}
			return Verdict.Pass;
		}
		case "splash-water-potion-effect":
		{
			AdapterActionResult adapterActionResult = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-impact");
			if ((object)adapterActionResult == null || (object)after == null)
			{
				return Verdict.Inconclusive;
			}
			if (!adapterActionResult.Detail.Contains("projectileType=splash_water_potion", StringComparison.Ordinal) || !adapterActionResult.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) || !adapterActionResult.Detail.Contains("targetHit=True", StringComparison.Ordinal))
			{
				return Verdict.Inconclusive;
			}
			double? num = before?.Health;
			if (num.HasValue)
			{
				double valueOrDefault = num.GetValueOrDefault();
				num = after.Health;
				if (num.HasValue)
				{
					double valueOrDefault2 = num.GetValueOrDefault();
					num2 = ((Math.Abs(valueOrDefault - valueOrDefault2) > 0.01) ? 1 : 0);
					goto IL_1166;
				}
			}
			num2 = 0;
			goto IL_1166;
		}
		case "splash-harming-no-damage":
		case "splash-healing-recovery":
		case "splash-harming-no-death":
			flag = true;
			break;
		default:
			{
				flag = false;
				break;
			}
			IL_1166:
			worldPositionSnapshot = before?.Position;
			if ((object)worldPositionSnapshot != null)
			{
				WorldPositionSnapshot position = after.Position;
				if ((object)position != null && worldPositionSnapshot.Dimension == position.Dimension)
				{
					num3 = ((Math.Pow(worldPositionSnapshot.X - position.X, 2.0) + Math.Pow(worldPositionSnapshot.Y - position.Y, 2.0) + Math.Pow(worldPositionSnapshot.Z - position.Z, 2.0) >= 64.0) ? 1 : 0);
					goto IL_1206;
				}
			}
			num3 = 0;
			goto IL_1206;
			IL_081d:
			flag4 = (byte)num5 != 0;
			flag5 = after.LifeState != EntityLifeState.Dead || (flag2 && flag4);
			if (!(num4 && flag3 && flag5))
			{
				return Verdict.Fail;
			}
			return Verdict.Pass;
			IL_1206:
			flag7 = (byte)num3 != 0;
			if (((uint)num2 | (flag7 ? 1u : 0u)) == 0 && after.LifeState != EntityLifeState.Dead)
			{
				return Verdict.Fail;
			}
			return Verdict.Pass;
		}
		if (flag)
		{
			AdapterActionResult adapterActionResult21 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-impact");
			if ((object)adapterActionResult21 == null || (object)after == null)
			{
				return Verdict.Inconclusive;
			}
			string text5 = ((expectedOutcome == "splash-healing-recovery") ? "splash_healing_potion" : "splash_harming_potion");
			string value10 = ((expectedOutcome == "splash-healing-recovery") ? "effect.minecraft.instant_health" : "effect.minecraft.instant_damage");
			if (!adapterActionResult21.Detail.Contains("projectileType=" + text5, StringComparison.Ordinal) || !adapterActionResult21.Detail.Contains(value10, StringComparison.Ordinal) || !adapterActionResult21.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) || !adapterActionResult21.Detail.Contains("targetHit=True", StringComparison.Ordinal))
			{
				return Verdict.Inconclusive;
			}
			if ((expectedOutcome == "splash-harming-no-damage" || expectedOutcome == "splash-harming-no-death") ? true : false)
			{
				double? num8 = ((!(expectedOutcome == "splash-harming-no-death")) ? before?.Health : actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "low-health")?.Entity?.Health);
				if (!num8.HasValue || !after.Health.HasValue)
				{
					return Verdict.Inconclusive;
				}
				if (after.LifeState != 0 || !(Math.Abs(after.Health.Value - num8.Value) < 0.01) || after.DamageCause != null)
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			double? num9 = ((!(expectedOutcome == "splash-healing-recovery")) ? before?.Health : actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "low-health")?.Entity?.Health);
			if (!num9.HasValue || !after.Health.HasValue)
			{
				return Verdict.Inconclusive;
			}
			if (expectedOutcome == "splash-healing-recovery")
			{
				if (after.LifeState != 0 || !(after.Health.Value > num9.Value + 0.01))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			if (!(after.Health.Value < num9.Value - 0.01) && after.LifeState != EntityLifeState.Dead)
			{
				return Verdict.Fail;
			}
			return Verdict.Pass;
		}
		switch (expectedOutcome)
		{
		case "splash-potion-sources-distinct":
		{
			AdapterActionResult adapterActionResult56 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-pair");
			if ((object)adapterActionResult56 == null || !(before?.Health).HasValue || !(after?.Health).HasValue || after.DamageEvents == null)
			{
				return Verdict.Inconclusive;
			}
			bool num35 = adapterActionResult56.Detail.Contains("waterProjectileHit=True", StringComparison.Ordinal) && adapterActionResult56.Detail.Contains("harmingProjectileHit=True", StringComparison.Ordinal) && adapterActionResult56.Detail.Contains("sameImpactTick=True", StringComparison.Ordinal);
			bool flag20 = adapterActionResult56.Detail.Contains("waterEffect=no-effects", StringComparison.Ordinal) && adapterActionResult56.Detail.Contains("harmingEffect=effect.minecraft.instant_damage", StringComparison.Ordinal);
			bool flag21 = after.DamageEvents.Count == 1 && after.DamageEvents[0].Cause == "indirectMagic" && Math.Abs(after.Health.Value - (before.Health.Value - after.DamageEvents[0].DamageTaken)) < 0.01 && after.LifeState == EntityLifeState.Alive;
			if (!(num35 && flag20 && flag21))
			{
				return Verdict.Inconclusive;
			}
			return Verdict.Pass;
		}
		case "ice-slide-fall":
		{
			AdapterActionResult adapterActionResult55 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "ice-slope");
			if ((object)adapterActionResult55 == null || (object)before?.Position == null || (object)after?.Position == null || after.DamageEvents == null)
			{
				return Verdict.Inconclusive;
			}
			if (!adapterActionResult55.Detail.Contains("iceReadback=true", StringComparison.OrdinalIgnoreCase) || !adapterActionResult55.Detail.Contains("shaftAir=405/405", StringComparison.Ordinal) || !adapterActionResult55.Detail.Contains("shaftDry=405/405", StringComparison.Ordinal) || !adapterActionResult55.Detail.Contains("shaftWalls=351/351", StringComparison.Ordinal) || !adapterActionResult55.Detail.Contains("landingBlocks=15/15", StringComparison.Ordinal) || !adapterActionResult55.Detail.Contains("noAi=false", StringComparison.OrdinalIgnoreCase) || !adapterActionResult55.Detail.Contains("noGravity=false", StringComparison.OrdinalIgnoreCase) || !adapterActionResult55.Detail.Contains("noPhysics=false", StringComparison.OrdinalIgnoreCase) || !adapterActionResult55.Detail.Contains("physicsEnabled=true", StringComparison.OrdinalIgnoreCase))
			{
				return Verdict.Inconclusive;
			}
			bool num34 = after.Position.X - before.Position.X >= 4.0;
			bool flag18 = before.Position.Y - after.Position.Y >= 20.0;
			bool flag19 = after.DamageEvents.Any((DamageEventSnapshot item) => item.Cause == "fall" && item.DamageTaken > 0.0);
			if (!(num34 && flag18 && flag19))
			{
				return Verdict.Fail;
			}
			return Verdict.Pass;
		}
		case "death":
			return ManagerAgent.JudgeDeath(before, after, "dead");
		default:
			if ((object)before == null || (object)after == null || before.Provenance == "visual-only" || after.Provenance == "visual-only")
			{
				return Verdict.Inconclusive;
			}
			if (expectedOutcome.StartsWith("damage-at-least:", StringComparison.Ordinal))
			{
				if (before.LifeState != 0 || !before.Health.HasValue || !after.Health.HasValue || !double.TryParse(expectedOutcome.AsSpan("damage-at-least:".Length), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result17))
				{
					return Verdict.Inconclusive;
				}
				if (!(before.Health.Value - after.Health.Value >= result17))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			if (expectedOutcome.StartsWith("damage-exactly:", StringComparison.Ordinal))
			{
				if (before.LifeState != 0 || !before.Health.HasValue || !after.Health.HasValue || !double.TryParse(expectedOutcome.AsSpan("damage-exactly:".Length), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result18))
				{
					return Verdict.Inconclusive;
				}
				if (!(Math.Abs(before.Health.Value - after.Health.Value - result18) < 0.01))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			switch (expectedOutcome)
			{
			case "second-hit-stronger":
			case "second-hit-weaker":
			case "damage-equal":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				double? num10 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "spawn")?.Entity?.Health;
				double? num11 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "control-attack")?.Entity?.Health;
				double? num12 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "spawn-profile")?.Entity?.Health;
				double? num13 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "profile-attack")?.Entity?.Health;
				if (!num10.HasValue || !num11.HasValue || !num12.HasValue || !num13.HasValue)
				{
					return Verdict.Inconclusive;
				}
				double num14 = num10.Value - num11.Value;
				double num15 = num12.Value - num13.Value;
				if (expectedOutcome == "second-hit-stronger")
				{
					if (!(num15 > num14))
					{
						return Verdict.Fail;
					}
					return Verdict.Pass;
				}
				if (expectedOutcome == "second-hit-weaker")
				{
					if (!(num15 < num14))
					{
						return Verdict.Fail;
					}
					return Verdict.Pass;
				}
				if (!(Math.Abs(num15 - num14) < 0.01))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			switch (expectedOutcome)
			{
			case "damage":
				if (before.LifeState != 0 || !before.Health.HasValue || !after.Health.HasValue)
				{
					return Verdict.Inconclusive;
				}
				flag = after.Health < before.Health;
				if (!flag)
				{
					EntityLifeState lifeState = after.LifeState;
					bool flag8 = (uint)(lifeState - 1) <= 1u;
					flag = flag8;
				}
				if (!flag)
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			case "attack-applied":
			{
				AdapterActionResult adapterActionResult51 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "single-melee-action");
				if ((object)adapterActionResult51 == null || !adapterActionResult51.Succeeded)
				{
					return Verdict.Inconclusive;
				}
				if (!adapterActionResult51.Detail.Contains("1/1 hits applied", StringComparison.Ordinal))
				{
					if (!adapterActionResult51.Detail.Contains("0/1 hits applied", StringComparison.Ordinal))
					{
						return Verdict.Inconclusive;
					}
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "attack-cooldown-window":
			{
				AdapterActionResult adapterActionResult31 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "first-hit");
				AdapterActionResult adapterActionResult32 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "rapid-second-hit");
				AdapterActionResult adapterActionResult33 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "recovered-hit");
				if ((object)adapterActionResult31 == null || (object)adapterActionResult32 == null || (object)adapterActionResult33 == null || !adapterActionResult31.Succeeded || !adapterActionResult32.Succeeded || !adapterActionResult33.Succeeded)
				{
					return Verdict.Inconclusive;
				}
				bool num25 = adapterActionResult31.Detail.Contains("1/1 hits applied", StringComparison.Ordinal);
				bool flag11 = adapterActionResult32.Detail.Contains("0/1 hits applied", StringComparison.Ordinal);
				bool flag12 = adapterActionResult33.Detail.Contains("1/1 hits applied", StringComparison.Ordinal);
				if (!num25 || !flag11 || !flag12 || !before.Health.HasValue || !after.Health.HasValue)
				{
					return Verdict.Inconclusive;
				}
				if (after.LifeState != 0 || !(after.Health < before.Health))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "attack-before-controlled-teleport-once":
			{
				AdapterActionResult adapterActionResult54 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "attack-before-teleport");
				if ((object)adapterActionResult54 != null && adapterActionResult54.Succeeded && adapterActionResult54.Detail.Contains("teleportRequested=True", StringComparison.OrdinalIgnoreCase) && adapterActionResult54.Detail.Contains("teleportSucceeded=True", StringComparison.OrdinalIgnoreCase) && adapterActionResult54.Detail.Contains("safePadBlocks=9", StringComparison.OrdinalIgnoreCase))
				{
					double? num = before?.Health;
					if (num.HasValue)
					{
						double valueOrDefault17 = num.GetValueOrDefault();
						num = after?.Health;
						if (num.HasValue)
						{
							double valueOrDefault18 = num.GetValueOrDefault();
							if ((object)before.Position != null && (object)after.Position != null && string.Equals(before.Position.Dimension, after.Position.Dimension, StringComparison.Ordinal))
							{
								double num31 = before.Position.X - after.Position.X;
								double num32 = before.Position.Y - after.Position.Y;
								double num33 = before.Position.Z - after.Position.Z;
								bool flag17 = num31 * num31 + num32 * num32 + num33 * num33 >= 64.0;
								if (after.LifeState != 0)
								{
									return Verdict.Fail;
								}
								if (!(adapterActionResult54.Detail.Contains("1/1 hits applied", StringComparison.Ordinal) && Math.Abs(valueOrDefault17 - valueOrDefault18 - 6.0) < 0.01 && flag17))
								{
									return Verdict.Fail;
								}
								return Verdict.Pass;
							}
						}
					}
				}
				return Verdict.Inconclusive;
			}
			case "health-stable-after-water-clear":
			{
				EntitySnapshot entitySnapshot = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "water-contact")?.Entity;
				EntitySnapshot entitySnapshot2 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "observe-dry")?.Entity;
				if (!(entitySnapshot?.Health).HasValue || !(entitySnapshot2?.Health).HasValue)
				{
					return Verdict.Inconclusive;
				}
				if (entitySnapshot.LifeState != 0 || entitySnapshot2.LifeState != 0)
				{
					return Verdict.Fail;
				}
				if (!(Math.Abs(entitySnapshot.Health.Value - entitySnapshot2.Health.Value) < 0.01))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "rain-shelter-stops-exposure":
			{
				AdapterActionResult adapterActionResult52 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "rain-exposure");
				AdapterActionResult adapterActionResult53 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "rain-shelter");
				string text9 = adapterActionResult53?.Detail.Split("Fixture diagnostics: ", 2).LastOrDefault();
				if ((object)adapterActionResult52 == null || (object)adapterActionResult53 == null || !adapterActionResult52.Succeeded || !adapterActionResult53.Succeeded || !adapterActionResult52.Detail.Contains("RainAtEntity=True", StringComparison.Ordinal) || !adapterActionResult53.Detail.Contains("RainAtEntity=False", StringComparison.Ordinal) || text9 == null || !text9.StartsWith("roof=", StringComparison.Ordinal) || text9.Split("minecraft:stone").Length - 1 != 25 || !before.Health.HasValue || !after.Health.HasValue)
				{
					return Verdict.Inconclusive;
				}
				if (before.LifeState != 0 || after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				if (!(Math.Abs(before.Health.Value - after.Health.Value) < 0.01))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "water-source-flowing-recorded":
			{
				AdapterActionResult adapterActionResult24 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "source-water-contact");
				AdapterActionResult adapterActionResult25 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "flowing-water-contact");
				if ((object)adapterActionResult24 == null || (object)adapterActionResult25 == null || !adapterActionResult24.Succeeded || !adapterActionResult25.Succeeded || (object)adapterActionResult24.Entity == null || (object)adapterActionResult25.Entity == null || !adapterActionResult24.Detail.Contains("fluidAmount=8; source=true", StringComparison.OrdinalIgnoreCase) || !adapterActionResult25.Detail.Contains("fluidAmount=4; source=false", StringComparison.OrdinalIgnoreCase))
				{
					return Verdict.Inconclusive;
				}
				if (adapterActionResult24.Entity.LifeState == EntityLifeState.Unknown || adapterActionResult25.Entity.LifeState == EntityLifeState.Unknown)
				{
					return Verdict.Inconclusive;
				}
				return Verdict.Pass;
			}
			case "water-depth-compare-recorded":
			{
				AdapterActionResult adapterActionResult49 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "shallow-water-contact");
				AdapterActionResult adapterActionResult50 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "deep-water-contact");
				if ((object)adapterActionResult49 == null || (object)adapterActionResult50 == null || !adapterActionResult49.Succeeded || !adapterActionResult50.Succeeded || !(adapterActionResult49.Entity?.Health).HasValue || !(adapterActionResult50.Entity?.Health).HasValue || !adapterActionResult49.Detail.Contains("waterDepth=1; fluidAmount=8", StringComparison.Ordinal) || !adapterActionResult50.Detail.Contains("waterDepth=3; fluidAmount=24", StringComparison.Ordinal))
				{
					return Verdict.Inconclusive;
				}
				if (adapterActionResult49.Entity.LifeState != 0 || adapterActionResult50.Entity.LifeState != 0)
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "repeated-water-contact-bounded":
			{
				AdapterActionResult[] array8 = actions.Where((AdapterActionResult action) => action.StepId.StartsWith("water-contact-", StringComparison.Ordinal)).ToArray();
				if (array8.Length != 3 || !before.Health.HasValue || array8.Any((AdapterActionResult pulse) => !pulse.Succeeded || !(pulse.Entity?.Health).HasValue))
				{
					return Verdict.Inconclusive;
				}
				double num29 = before.Health.Value;
				AdapterActionResult[] array9 = array8;
				foreach (AdapterActionResult adapterActionResult44 in array9)
				{
					double value24 = adapterActionResult44.Entity.Health.Value;
					double num30 = num29 - value24;
					if (num30 < -0.01 || num30 > 1.01 || adapterActionResult44.Entity.LifeState != 0)
					{
						return Verdict.Fail;
					}
					num29 = value24;
				}
				if (after.LifeState != 0 || !after.Health.HasValue)
				{
					return Verdict.Fail;
				}
				if (!(before.Health.Value - after.Health.Value <= (double)array8.Length + 0.01))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "lava-escape-or-lethal":
			{
				AdapterActionResult? adapterActionResult29 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "set-low-health");
				AdapterActionResult adapterActionResult30 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "lava-contact");
				double? num = adapterActionResult29?.Entity?.Health;
				if (num.HasValue)
				{
					double valueOrDefault9 = num.GetValueOrDefault();
					if (!(Math.Abs(valueOrDefault9 - 6.0) > 0.01) && (object)adapterActionResult30 != null && adapterActionResult30.Succeeded && adapterActionResult30.Detail.Contains("lavaReadback=True", StringComparison.OrdinalIgnoreCase) && TryReadCoordinate(adapterActionResult30.Detail, "contactX", out var value13) && TryReadCoordinate(adapterActionResult30.Detail, "contactY", out var value14) && TryReadCoordinate(adapterActionResult30.Detail, "contactZ", out var value15))
					{
						EntityLifeState lifeState = after.LifeState;
						if ((uint)(lifeState - 1) <= 1u)
						{
							string? damageCause = after.DamageCause;
							if (damageCause == null || !damageCause.Contains("lava", StringComparison.OrdinalIgnoreCase))
							{
								return Verdict.Inconclusive;
							}
							return Verdict.Pass;
						}
						if (after.LifeState == EntityLifeState.Alive)
						{
							WorldPositionSnapshot position2 = after.Position;
							if ((object)position2 != null)
							{
								WorldPositionSnapshot worldPositionSnapshot2 = before?.Position;
								if ((object)worldPositionSnapshot2 != null && string.Equals(worldPositionSnapshot2.Dimension, position2.Dimension, StringComparison.Ordinal))
								{
									double num22 = value13 - position2.X;
									double num23 = value14 - position2.Y;
									double num24 = value15 - position2.Z;
									if (!(num22 * num22 + num23 * num23 + num24 * num24 >= 64.0))
									{
										return Verdict.Fail;
									}
									return Verdict.Pass;
								}
							}
						}
						return Verdict.Inconclusive;
					}
				}
				return Verdict.Inconclusive;
			}
			case "damage-stops-after-lava-escape":
			{
				AdapterActionResult adapterActionResult38 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "lava-contact");
				EntitySnapshot entitySnapshot3 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "observe-before-escape")?.Entity;
				AdapterActionResult adapterActionResult39 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "safe-escape");
				if ((object)adapterActionResult38 != null && adapterActionResult38.Succeeded && adapterActionResult38.Detail.Contains("lavaReadback=True", StringComparison.OrdinalIgnoreCase))
				{
					double? num = entitySnapshot3?.Health;
					if (num.HasValue)
					{
						double valueOrDefault10 = num.GetValueOrDefault();
						num = before?.Health;
						if (num.HasValue)
						{
							double valueOrDefault11 = num.GetValueOrDefault();
							if (entitySnapshot3.LifeState == EntityLifeState.Alive && !(valueOrDefault10 >= valueOrDefault11) && (object)adapterActionResult39 != null && adapterActionResult39.Succeeded)
							{
								num = adapterActionResult39.Entity?.Health;
								if (num.HasValue)
								{
									double valueOrDefault12 = num.GetValueOrDefault();
									if (adapterActionResult39.Entity.LifeState == EntityLifeState.Alive && adapterActionResult39.Detail.Contains("lavaRemoved=True", StringComparison.OrdinalIgnoreCase) && adapterActionResult39.Detail.Contains("safePadFloorBlocks=25", StringComparison.OrdinalIgnoreCase) && adapterActionResult39.Detail.Contains("safePadDryCells=18", StringComparison.OrdinalIgnoreCase) && adapterActionResult39.Detail.Contains("safePadWallBlocks=32", StringComparison.OrdinalIgnoreCase) && adapterActionResult39.Detail.Contains("targetAiDisabled=true", StringComparison.OrdinalIgnoreCase) && adapterActionResult39.Detail.Contains("teleported=true", StringComparison.OrdinalIgnoreCase))
									{
										int? fireTicks = adapterActionResult39.Entity.FireTicks;
										if (fireTicks.HasValue && fireTicks.GetValueOrDefault() == 0)
										{
											num = after.Health;
											if (num.HasValue)
											{
												double valueOrDefault13 = num.GetValueOrDefault();
												if (after.LifeState == EntityLifeState.Alive && after.DamageEvents != null && adapterActionResult39.Entity.DamageEvents != null)
												{
													WorldPositionSnapshot position3 = adapterActionResult39.Entity.Position;
													if ((object)position3 != null)
													{
														WorldPositionSnapshot position4 = after.Position;
														if ((object)position4 != null && string.Equals(position3.Dimension, position4.Dimension, StringComparison.Ordinal))
														{
															DamageEventSnapshot[] array5 = after.DamageEvents.Skip(adapterActionResult39.Entity.DamageEvents.Count).ToArray();
															bool flag16 = Math.Abs(position3.X - position4.X) > 0.01 || Math.Abs(position3.Y - position4.Y) > 0.01 || Math.Abs(position3.Z - position4.Z) > 0.01;
															if (array5.Length != 0 || flag16 || Math.Abs(valueOrDefault12 - valueOrDefault13) >= 0.01)
															{
																return Verdict.Fail;
															}
															if ((after.FireTicks ?? 1) != 0)
															{
																return Verdict.Fail;
															}
															return Verdict.Pass;
														}
													}
												}
											}
										}
									}
								}
							}
						}
					}
				}
				return Verdict.Inconclusive;
			}
			case "projectile-no-damage":
			{
				AdapterActionResult adapterActionResult28 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "projectile-hit");
				if ((object)adapterActionResult28 == null || !adapterActionResult28.Succeeded || !adapterActionResult28.Detail.Contains("targetHit=True", StringComparison.Ordinal))
				{
					return Verdict.Inconclusive;
				}
				if (!before.Health.HasValue || !after.Health.HasValue)
				{
					return Verdict.Inconclusive;
				}
				if (after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				if (!(Math.Abs(before.Health.Value - after.Health.Value) < 0.01))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "projectile-blocked-no-damage":
			{
				AdapterActionResult adapterActionResult40 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "projectile-hit");
				if ((object)adapterActionResult40 == null || !adapterActionResult40.Succeeded || !adapterActionResult40.Detail.Contains("outcome=block-hit", StringComparison.Ordinal))
				{
					return Verdict.Inconclusive;
				}
				if (adapterActionResult40.Detail.Contains("targetHit=True", StringComparison.Ordinal) || !before.Health.HasValue || !after.Health.HasValue)
				{
					return Verdict.Fail;
				}
				if (before.LifeState != 0 || after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				if (!(Math.Abs(before.Health.Value - after.Health.Value) < 0.01))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "projectile-impact-recorded":
			{
				AdapterActionResult adapterActionResult37 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "projectile-hit");
				if ((object)adapterActionResult37 != null && adapterActionResult37.Detail.Contains("secondTargetUuid=", StringComparison.Ordinal))
				{
					Dictionary<string, string> values = ParseTokens(adapterActionResult37.Detail);
					string Field(string key) => values.GetValueOrDefault(key, string.Empty);
					string text6 = Field("firstTargetUuid");
					string text7 = Field("secondTargetUuid");
					string text8 = Field("impactEntityUuid");
					if ((object)adapterActionResult37 == null || !adapterActionResult37.Succeeded || (object)before == null || (object)after == null || !Guid.TryParse(text6, out var result19) || !Guid.TryParse(text7, out result19) || string.Equals(text6, text7, StringComparison.OrdinalIgnoreCase) || text6 != before.RunEntityId || text6 != after.RunEntityId || !bool.TryParse(Field("placementVerified"), out var result20) || !result20 || !bool.TryParse(Field("targetAiDisabled"), out var result21) || !result21 || !bool.TryParse(Field("teleportSucceeded"), out var result22) || !result22 || !TryReadTokenDouble(Field("firstHealthBefore"), out var value19) || !TryReadTokenDouble(Field("firstHealthAfter"), out var value20) || !int.TryParse(Field("firstDamageEventDelta"), out var result23) || !bool.TryParse(Field("firstAlive"), out var result24) || !TryReadTokenDouble(Field("secondHealthBefore"), out var value21) || !TryReadTokenDouble(Field("secondHealthAfter"), out var value22) || !int.TryParse(Field("secondDamageEventDelta"), out var result25) || !int.TryParse(Field("secondArrowDamageEventDelta"), out var result26) || !bool.TryParse(Field("secondAlive"), out var result27) || !long.TryParse(Field("projectileGameTick"), out var result28))
					{
						return Verdict.Inconclusive;
					}
					QaEvent qaEvent = adapterActionResult37.Events.FirstOrDefault((QaEvent item) => item.Kind == "projectile-target" && item.Detail.Contains("role=second;", StringComparison.Ordinal));
					if ((object)qaEvent == null || qaEvent.EntityRunId != text7)
					{
						return Verdict.Inconclusive;
					}
					if (!result24 || Math.Abs(value19 - value20) > 0.001 || result23 != 0)
					{
						return Verdict.Fail;
					}
					if (Field("outcome") == "entity-hit" && text8 == text6)
					{
						return Verdict.Fail;
					}
					if (Field("outcome") == "entity-hit" && text8 != text7)
					{
						return Verdict.Inconclusive;
					}
					if (Field("outcome") != "entity-hit")
					{
						return Verdict.Inconclusive;
					}
					long result29;
					bool flag14 = long.TryParse(Field("secondArrowDamageTick"), out result29) && result29 == result28;
					bool num27 = value22 < value21 && result27 && result25 == 1 && result26 == 1 && flag14;
					bool flag15 = result27 && Math.Abs(value22 - value21) < 0.001 && result25 == 0 && result26 == 0;
					if (!(num27 || flag15))
					{
						return Verdict.Inconclusive;
					}
					return Verdict.Pass;
				}
				if ((object)adapterActionResult37 == null || !adapterActionResult37.Succeeded || !adapterActionResult37.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) || !adapterActionResult37.Detail.Contains("targetHit=True", StringComparison.Ordinal) || !before.Health.HasValue || !after.Health.HasValue || !after.FireTicks.HasValue)
				{
					return Verdict.Inconclusive;
				}
				return Verdict.Pass;
			}
			case "projectile-volley-single-death-drop-set":
			{
				AdapterActionResult adapterActionResult47 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-baseline");
				AdapterActionResult adapterActionResult48 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "projectile-volley");
				AdapterActionResult artifacts = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-after");
				if ((object)adapterActionResult47 == null || (object)adapterActionResult48 == null || (object)artifacts == null || (object)after == null || !adapterActionResult47.Succeeded || !adapterActionResult48.Succeeded || !artifacts.Succeeded || !adapterActionResult47.Detail.Contains("UUID set captured", StringComparison.Ordinal))
				{
					return Verdict.Inconclusive;
				}
				Dictionary<string, string> volleyValues = ParseTokens(adapterActionResult48.Detail);
				Dictionary<string, string> artifactValues = ParseTokens(artifacts.Detail);
				bool Int(string k, out int val) => int.TryParse(volleyValues.GetValueOrDefault(k), out val);
				bool ArtifactInt(string k, out int val) => int.TryParse(artifactValues.GetValueOrDefault(k), out val);
				if (!Int("projectileCount", out var value25) || value25 != 8 || !Int("terminalProjectiles", out var value26) || !Int("targetHits", out var value27) || !ArtifactInt("deathFactsForTarget", out var value28) || !ArtifactInt("newXp", out var value29) || !ArtifactInt("newItemEntities", out var value30) || !ArtifactInt("newItemCount", out var value31) || !ArtifactInt("newPearls", out var value32))
				{
					return Verdict.Inconclusive;
				}
				Match match7 = Regex.Match(artifacts.Detail, "duplicateArtifactUuid=(?<value>True|False)", RegexOptions.IgnoreCase);
				Match match8 = Regex.Match(artifacts.Detail, "allTargetUuidMatches=(?<value>True|False)", RegexOptions.IgnoreCase);
				Match match9 = Regex.Match(artifacts.Detail, "serverAlive=(?<value>True|False)", RegexOptions.IgnoreCase);
				string valueOrDefault16 = volleyValues.GetValueOrDefault("latencyCoverage");
				if (!match7.Success || !match8.Success || !match9.Success)
				{
					return Verdict.Inconclusive;
				}
				if (match7.Groups["value"].Value.Equals("True", StringComparison.OrdinalIgnoreCase) || value28 > 1 || value30 > 1 || value31 > 1 || value32 > 1 || value29 > 5)
				{
					return Verdict.Fail;
				}
				if (after.LifeState != EntityLifeState.Dead || !(after.DamageCause == "arrow") || value28 != 1 || value26 != value25 || value27 < 1 || !match8.Groups["value"].Value.Equals("True", StringComparison.OrdinalIgnoreCase) || !match9.Groups["value"].Value.Equals("True", StringComparison.OrdinalIgnoreCase))
				{
					return Verdict.Inconclusive;
				}
				if (!(valueOrDefault16 != "none"))
				{
					adapterActionResult48.Detail.Contains("bypasses client transport", StringComparison.Ordinal);
				}
				return Verdict.Inconclusive;
			}
			case "crossbow-firework-explosion":
			{
				AdapterActionResult adapterActionResult23 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "firework-hit");
				if ((object)adapterActionResult23 == null || !adapterActionResult23.Succeeded || !adapterActionResult23.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal))
				{
					return Verdict.Inconclusive;
				}
				string value11 = Regex.Match(adapterActionResult23.Detail, "impactEntityUuid=([0-9a-fA-F-]{36})").Groups[1].Value;
				string value12 = Regex.Match(adapterActionResult23.Detail, "targetUuid=([0-9a-fA-F-]{36})").Groups[1].Value;
				if (value11.Length != 36 || value12.Length != 36 || !value11.Equals(value12, StringComparison.OrdinalIgnoreCase))
				{
					return Verdict.Inconclusive;
				}
				double? num = before?.Health;
				if (num.HasValue)
				{
					double beforeHealth = num.GetValueOrDefault();
					num = after?.Health;
					if (num.HasValue)
					{
						num.GetValueOrDefault();
						if (after.DamageEvents != null)
						{
							if (!after.DamageEvents.Any((DamageEventSnapshot item) => item.Cause.Contains("firework", StringComparison.OrdinalIgnoreCase) && item.DamageTaken > 0.0 && item.HealthAfter <= beforeHealth) || after.Health >= beforeHealth)
							{
								return Verdict.Inconclusive;
							}
							return Verdict.Pass;
						}
					}
				}
				return Verdict.Inconclusive;
			}
			case "splash-water-radius-recorded":
			{
				AdapterActionResult adapterActionResult26 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-outside");
				AdapterActionResult adapterActionResult27 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-inside");
				if ((object)adapterActionResult26 != null && (object)adapterActionResult27 != null && adapterActionResult26.Succeeded && adapterActionResult27.Succeeded && adapterActionResult26.Detail.Contains("outcome=block-hit", StringComparison.Ordinal) && adapterActionResult27.Detail.Contains("outcome=block-hit", StringComparison.Ordinal))
				{
					double? num = adapterActionResult26.Entity?.Health;
					if (num.HasValue)
					{
						double valueOrDefault7 = num.GetValueOrDefault();
						num = adapterActionResult27.Entity?.Health;
						if (num.HasValue)
						{
							double valueOrDefault8 = num.GetValueOrDefault();
							bool num21 = adapterActionResult26.Entity.DamageEvents?.Any((DamageEventSnapshot item) => item.Cause.Contains("indirectMagic", StringComparison.OrdinalIgnoreCase) && item.DamageTaken > 0.0) ?? false;
							bool flag10 = adapterActionResult27.Entity.DamageEvents?.Any((DamageEventSnapshot item) => item.Cause.Contains("indirectMagic", StringComparison.OrdinalIgnoreCase) && item.DamageTaken > 0.0) ?? false;
							if (num21 || valueOrDefault7 < 39.99 || valueOrDefault8 >= valueOrDefault7 || !flag10)
							{
								return Verdict.Fail;
							}
							return Verdict.Pass;
						}
					}
				}
				return Verdict.Inconclusive;
			}
			case "lingering-water-cloud-window":
			{
				AdapterActionResult adapterActionResult35 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "water-cloud");
				AdapterActionResult adapterActionResult36 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "cloud-window");
				if ((object)adapterActionResult35 == null || !adapterActionResult35.Succeeded || !adapterActionResult35.Detail.Contains("cloudPotion=water", StringComparison.Ordinal) || !adapterActionResult35.Detail.Contains("cloudRadius=2.5", StringComparison.Ordinal) || (object)adapterActionResult36 == null || !adapterActionResult36.Succeeded || (object)after == null || !after.Health.HasValue)
				{
					return Verdict.Inconclusive;
				}
				return Verdict.Pass;
			}
			case "lingering-water-cloud-exit":
			{
				AdapterActionResult adapterActionResult41 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "isolate-ai");
				AdapterActionResult adapterActionResult42 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "water-cloud");
				AdapterActionResult adapterActionResult43 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "leave-cloud");
				EntitySnapshot exitState = adapterActionResult43?.Entity;
				if ((object)adapterActionResult41 != null && adapterActionResult41.Succeeded && adapterActionResult41.Detail.Contains("targetAiDisabled=true", StringComparison.OrdinalIgnoreCase) && (object)adapterActionResult42 != null && adapterActionResult42.Succeeded && adapterActionResult42.Detail.Contains("cloudPotion=water", StringComparison.Ordinal) && adapterActionResult42.Detail.Contains("cloudDuration=1200", StringComparison.Ordinal) && (object)adapterActionResult43 != null && adapterActionResult43.Succeeded && adapterActionResult43.Detail.Contains("targetMoved=true", StringComparison.Ordinal) && adapterActionResult43.Detail.Contains("safePadBlocks=9", StringComparison.Ordinal) && (exitState?.Health).HasValue && exitState.GameTick.HasValue && (object)after != null && after.Health.HasValue && exitState.LifeState == EntityLifeState.Alive && after.LifeState == EntityLifeState.Alive && after.GameTick.HasValue && !(after.GameTick <= exitState.GameTick))
				{
					WorldPositionSnapshot position5 = before.Position;
					if ((object)position5 != null)
					{
						WorldPositionSnapshot position6 = exitState.Position;
						if ((object)position6 != null && (object)after.Position != null)
						{
							if (HorizontalDistance(position5, position6) <= 8.0)
							{
								return Verdict.Inconclusive;
							}
							int num28 = exitState.DamageEvents?.Count ?? 0;
							int count = before.DamageEvents?.Count ?? 0;
							if (!(exitState.DamageEvents ?? Array.Empty<DamageEventSnapshot>()).Skip(count).Any((DamageEventSnapshot damage) => string.Equals(damage.Cause, "drown", StringComparison.OrdinalIgnoreCase)))
							{
								return Verdict.Inconclusive;
							}
							DamageEventSnapshot[] array6 = (after.DamageEvents ?? Array.Empty<DamageEventSnapshot>()).Where((DamageEventSnapshot damage) => damage.GameTick > exitState.GameTick).ToArray();
							if (array6.Length == 0)
							{
								if (!(Math.Abs(after.Health.Value - exitState.Health.Value) < 0.001) || (after.DamageEvents?.Count ?? 0) != num28)
								{
									return Verdict.Inconclusive;
								}
								return Verdict.Pass;
							}
							Dictionary<long, PositionTraceSnapshot> dictionary = (from sample in after.PositionTrace ?? Array.Empty<PositionTraceSnapshot>()
								group sample by sample.GameTick).ToDictionary((IGrouping<long, PositionTraceSnapshot> group) => group.Key, (IGrouping<long, PositionTraceSnapshot> group) => group.Last());
							List<bool> list = new List<bool>();
							DamageEventSnapshot[] array7 = array6;
							int length = 0;
							while (length < array7.Length)
							{
								DamageEventSnapshot damageEventSnapshot = array7[length];
								if (dictionary.TryGetValue(damageEventSnapshot.GameTick, out var value23))
								{
									double? num = value23.CloudHorizontalDistance;
									if (num.HasValue)
									{
										double valueOrDefault14 = num.GetValueOrDefault();
										num = value23.CloudRadius;
										if (num.HasValue)
										{
											double valueOrDefault15 = num.GetValueOrDefault();
											if (string.Equals(value23.Position.Dimension, position5.Dimension, StringComparison.Ordinal))
											{
												list.Add(valueOrDefault14 > valueOrDefault15);
												length++;
												continue;
											}
										}
									}
								}
								return Verdict.Inconclusive;
							}
							if (!list.All((bool outside) => outside))
							{
								return Verdict.Inconclusive;
							}
							return Verdict.Fail;
						}
					}
				}
				return Verdict.Inconclusive;
			}
			case "projectile-vehicle-impact-recorded":
			{
				AdapterActionResult adapterActionResult34 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "projectile-hit");
				if ((object)adapterActionResult34 == null || !adapterActionResult34.Succeeded || !adapterActionResult34.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal))
				{
					return Verdict.Inconclusive;
				}
				string value16 = Regex.Match(adapterActionResult34.Detail, "impactEntityUuid=([0-9a-fA-F-]{36})").Groups[1].Value;
				string value17 = Regex.Match(adapterActionResult34.Detail, "targetUuid=([0-9a-fA-F-]{36})").Groups[1].Value;
				string value18 = Regex.Match(adapterActionResult34.Detail, "vehicleUuid=([0-9a-fA-F-]{36})").Groups[1].Value;
				if (value16.Length != 36 || value17.Length != 36 || value18.Length != 36)
				{
					return Verdict.Inconclusive;
				}
				if (!value16.Equals(value17, StringComparison.OrdinalIgnoreCase) && !value16.Equals(value18, StringComparison.OrdinalIgnoreCase))
				{
					return Verdict.Inconclusive;
				}
				if (!(before?.Health).HasValue || !(after?.Health).HasValue || after.LifeState != 0)
				{
					return Verdict.Inconclusive;
				}
				return Verdict.Pass;
			}
			case "projectile-pair-recorded":
			{
				AdapterActionResult adapterActionResult45 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "bow-projectile-hit");
				AdapterActionResult adapterActionResult46 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "crossbow-projectile-hit");
				if ((object)adapterActionResult45 == null || (object)adapterActionResult46 == null || !adapterActionResult45.Succeeded || !adapterActionResult46.Succeeded || !adapterActionResult45.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) || !adapterActionResult46.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) || !adapterActionResult45.Detail.Contains("targetHit=True", StringComparison.Ordinal) || !adapterActionResult46.Detail.Contains("targetHit=True", StringComparison.Ordinal) || !before.Health.HasValue || !after.Health.HasValue)
				{
					return Verdict.Inconclusive;
				}
				if (after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "projectile-triple-recorded":
			{
				AdapterActionResult[] array10 = actions.Where(delegate(AdapterActionResult action)
				{
					switch (action.StepId)
					{
					case "arrow-one":
					case "arrow-two":
					case "arrow-three":
						return true;
					default:
						return false;
					}
				}).ToArray();
				if (array10.Length != 3 || array10.Any((AdapterActionResult action) => !action.Succeeded || !action.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) || !action.Detail.Contains("targetHit=True", StringComparison.Ordinal)))
				{
					return Verdict.Inconclusive;
				}
				string[] array11 = array10.Select((AdapterActionResult action) => Regex.Match(action.Detail, "targetUuid=([0-9a-fA-F-]{36})").Groups[1].Value).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
				if (array11.Length != 1 || array11[0].Length != 36 || (object)after == null || after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "projectile-no-stale-hit":
			{
				AdapterActionResult adapterActionResult22 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "projectile-hit");
				if ((object)adapterActionResult22 != null && adapterActionResult22.Succeeded && adapterActionResult22.Detail.Contains("teleportRequested=True", StringComparison.OrdinalIgnoreCase) && adapterActionResult22.Detail.Contains("teleportSucceeded=True", StringComparison.OrdinalIgnoreCase) && adapterActionResult22.Detail.Contains("safePadBlocks=9", StringComparison.OrdinalIgnoreCase) && !adapterActionResult22.Detail.Contains("targetHit=True", StringComparison.OrdinalIgnoreCase) && (adapterActionResult22.Detail.Contains("outcome=block-hit", StringComparison.OrdinalIgnoreCase) || adapterActionResult22.Detail.Contains("outcome=timeout", StringComparison.OrdinalIgnoreCase)))
				{
					double? num = before?.Health;
					if (num.HasValue)
					{
						double valueOrDefault3 = num.GetValueOrDefault();
						num = after?.Health;
						if (num.HasValue)
						{
							double valueOrDefault4 = num.GetValueOrDefault();
							if (after.LifeState != 0 || !(Math.Abs(valueOrDefault3 - valueOrDefault4) < 0.01))
							{
								return Verdict.Fail;
							}
							return Verdict.Pass;
						}
					}
				}
				return Verdict.Inconclusive;
			}
			case "cactus-one-contact":
			{
				double? num = before?.Health;
				if (num.HasValue)
				{
					double valueOrDefault5 = num.GetValueOrDefault();
					num = after?.Health;
					if (num.HasValue)
					{
						double valueOrDefault6 = num.GetValueOrDefault();
						if (after.DamageEvents != null)
						{
							DamageEventSnapshot[] array4 = after.DamageEvents.Where((DamageEventSnapshot item) => item.Cause == "cactus").ToArray();
							if (after.LifeState != 0)
							{
								return Verdict.Fail;
							}
							if (array4.Length != 1 || !(Math.Abs(array4[0].DamageTaken - 1.0) < 0.01) || !(Math.Abs(valueOrDefault5 - valueOrDefault6 - 1.0) < 0.01))
							{
								if (array4.Length != 0)
								{
									return Verdict.Fail;
								}
								return Verdict.Inconclusive;
							}
							return Verdict.Pass;
						}
					}
				}
				return Verdict.Inconclusive;
			}
			case "freeze-telemetry-captured":
				if (!before.FrozenTicks.HasValue || !after.FrozenTicks.HasValue)
				{
					return Verdict.Inconclusive;
				}
				return Verdict.Pass;
			case "fire-ticks-cleared":
			{
				int? num19 = actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "fire-active")?.Entity?.FireTicks;
				int? num20 = actions.FirstOrDefault(delegate(AdapterActionResult action)
				{
					string stepId = action.StepId;
					return (stepId == "clear-fire" || stepId == "water-contact") ? true : false;
				})?.Entity?.FireTicks;
				if (!num19.HasValue || !num20.HasValue || num19 <= 0)
				{
					return Verdict.Inconclusive;
				}
				if (num20 != 0)
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "damage-or-displace":
			{
				if (before.LifeState != 0 || after.LifeState == EntityLifeState.Unknown)
				{
					return Verdict.Inconclusive;
				}
				bool num26 = before.Health.HasValue && after.Health.HasValue && after.Health < before.Health;
				bool flag13 = (object)before.Position != null && (object)after.Position != null && string.Equals(before.Position.Dimension, after.Position.Dimension, StringComparison.Ordinal) && Math.Pow(before.Position.X - after.Position.X, 2.0) + Math.Pow(before.Position.Y - after.Position.Y, 2.0) + Math.Pow(before.Position.Z - after.Position.Z, 2.0) >= 64.0;
				flag = num26 || flag13;
				if (!flag)
				{
					EntityLifeState lifeState = after.LifeState;
					bool flag8 = (uint)(lifeState - 1) <= 1u;
					flag = flag8;
				}
				if (!flag)
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			case "survive":
				if (before.LifeState != 0 || after.LifeState != 0)
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			case "survive-and-displace":
			{
				if (before.LifeState != 0 || after.LifeState != 0 || (object)before.Position == null || (object)after.Position == null)
				{
					return Verdict.Fail;
				}
				if (!string.Equals(before.Position.Dimension, after.Position.Dimension, StringComparison.Ordinal))
				{
					return Verdict.Inconclusive;
				}
				double num16 = before.Position.X - after.Position.X;
				double num17 = before.Position.Y - after.Position.Y;
				double num18 = before.Position.Z - after.Position.Z;
				if (!(num16 * num16 + num17 * num17 + num18 * num18 >= 64.0))
				{
					return Verdict.Fail;
				}
				return Verdict.Pass;
			}
			default:
				return Verdict.Inconclusive;
			}
		}
		static double HorizontalDistance(WorldPositionSnapshot a, WorldPositionSnapshot b)
		{
			double num36 = a.X - b.X;
			double num37 = a.Z - b.Z;
			return Math.Sqrt(num36 * num36 + num37 * num37);
		}
		static bool ValidArtifacts(string detail)
		{
			if (detail.Contains("allTargetUuidMatches=True", StringComparison.Ordinal) && detail.Contains("duplicateArtifactUuid=False", StringComparison.Ordinal) && Regex.IsMatch(detail, "newPearls=\\d+"))
			{
				return Regex.IsMatch(detail, "newXp=\\d+");
			}
			return false;
		}
	}

	private static bool TryReadCoordinate(string detail, string name, out double value)
	{
		string name2 = name;
		value = 0.0;
		string text = detail.Split(';', StringSplitOptions.TrimEntries).FirstOrDefault((string part) => part.StartsWith(name2 + "=", StringComparison.Ordinal));
		if (text != null)
		{
			return double.TryParse(text.AsSpan(name2.Length + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
		}
		return false;
	}

	private static Dictionary<string, string> ParseTokens(string detail)
	{
		return (from token in detail.Split(';', StringSplitOptions.TrimEntries)
			select token.Split('=', 2, StringSplitOptions.TrimEntries) into parts
			where parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0])
			select parts).ToDictionary<string[], string, string>((string[] parts) => parts[0], (string[] parts) => parts[1], StringComparer.Ordinal);
	}

	private static bool TryReadTokenDouble(string? text, out double value)
	{
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}
}
