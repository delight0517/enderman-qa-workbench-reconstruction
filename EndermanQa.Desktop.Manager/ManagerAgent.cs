using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Manager;

public sealed class ManagerAgent
{
	private static readonly HashSet<string> AllowedActionFamilies = new HashSet<string>(StringComparer.Ordinal)
	{
		"fixture.create", "entity.spawn", "entity.apply_damage", "entity.player_attack", "entity.sweep_attack", "environment.configure", "projectile.throw", "projectile.throw_volley", "time.advance", "observation.request",
		"capture.request"
	};

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = false
	};

	public (QaPlan? Plan, PlanValidation Validation) CreatePlan(QaScenario scenario, GameTarget target, AdapterCapabilities capabilities)
	{
		List<string> list = new List<string>();
		if (scenario.Version < 1 || string.IsNullOrWhiteSpace(scenario.Id))
		{
			list.Add("Scenario identity/version is invalid.");
		}
		string ownership = target.Ownership;
		if ((!(ownership == "owned") && !(ownership == "explicit-disposable")) || 1 == 0)
		{
			list.Add("Target is not verified as owned or disposable.");
		}
		if (capabilities.AdapterId.Length == 0 || capabilities.ContractVersion != "1.0")
		{
			list.Add("Adapter identity or contract version is unsupported.");
		}
		foreach (string requiredCapability in scenario.RequiredCapabilities)
		{
			if (!capabilities.Actions.Contains(requiredCapability))
			{
				list.Add("Adapter does not support required capability: " + requiredCapability);
			}
		}
		foreach (ScenarioStep step in scenario.Steps)
		{
			if (!AllowedActionFamilies.Contains(step.Action))
			{
				list.Add("Action is outside the allowlist: " + step.Action);
			}
			else if (!capabilities.Actions.Contains(step.Action))
			{
				list.Add("Adapter does not support action: " + step.Action);
			}
		}
		int timeoutSeconds = scenario.TimeoutSeconds;
		if ((timeoutSeconds < 1 || timeoutSeconds > 600) ? true : false)
		{
			list.Add("Scenario timeout must be between 1 and 600 seconds.");
		}
		string expectedOutcome = scenario.ExpectedOutcome;
		bool flag;
		switch (expectedOutcome)
		{
		case "projectile-no-damage":
		case "survive-and-displace":
		case "damage-or-displace":
		case "fire-ticks-cleared":
		case "cactus-one-contact":
		case "bare-hand-player-kill":
		case "lava-escape-or-lethal":
		case "damage-stops-after-lava-escape":
		case "repeated-water-contact-bounded":
		case "paired-looting-deaths-observed":
		case "water-death-artifacts-observed":
		case "splash-potion-sources-distinct":
		case "environment-player-attribution":
		case "attack-applied":
		case "ice-slide-fall":
		case "crossbow-firework-explosion":
		case "rain-shelter-stops-exposure":
		case "fire-death-artifacts-observed":
		case "lava-death-artifacts-observed":
		case "water-source-flowing-recorded":
		case "splash-water-radius-recorded":
		case "water-depth-compare-recorded":
		case "lingering-water-cloud-window":
		case "projectile-blocked-no-damage":
		case "splash-water-potion-effect":
		case "projectile-triple-recorded":
		case "lingering-water-cloud-exit":
		case "projectile-impact-recorded":
		case "projectile-pair-recorded":
		case "splash-harming-no-damage":
		case "splash-harming-no-death":
		case "splash-healing-recovery":
		case "projectile-no-stale-hit":
		case "second-hit-stronger":
		case "sweep-per-uuid-once":
		case "death":
		case "survive":
		case "damage":
		case "attack-before-controlled-teleport-once":
		case "freeze-telemetry-captured":
		case "attack-cooldown-window":
		case "health-stable-after-water-clear":
		case "projectile-vehicle-impact-recorded":
		case "water-blocked-teleport-no-duplicate-damage":
		case "projectile-volley-single-death-drop-set":
		case "custom-component-attack-recorded":
		case "second-hit-weaker":
		case "damage-equal":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag && (!expectedOutcome.StartsWith("damage-at-least:", StringComparison.Ordinal) || !double.TryParse(expectedOutcome.AsSpan("damage-at-least:".Length), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result) || !(result > 0.0) || !(result <= 40.0)) && (!expectedOutcome.StartsWith("damage-exactly:", StringComparison.Ordinal) || !double.TryParse(expectedOutcome.AsSpan("damage-exactly:".Length), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result2) || !(result2 > 0.0) || !(result2 <= 40.0)))
		{
			list.Add("Scenario expected outcome is not supported.");
		}
		timeoutSeconds = scenario.Steps.Count;
		if ((timeoutSeconds < 1 || timeoutSeconds > 100) ? true : false)
		{
			list.Add("Scenario must contain between 1 and 100 steps.");
		}
		if (scenario.Steps.Select((ScenarioStep s) => s.Id).Distinct<string>(StringComparer.Ordinal).Count() != scenario.Steps.Count)
		{
			list.Add("Scenario step IDs must be unique.");
		}
		foreach (ScenarioStep step2 in scenario.Steps)
		{
			if (step2.Arguments.Count > 20 || step2.Arguments.Any<KeyValuePair<string, string>>(delegate(KeyValuePair<string, string> pair)
			{
				int length = pair.Key.Length;
				return ((length < 1 || length > 80) ? true : false) || pair.Value.Length > 500;
			}))
			{
				list.Add("Step '" + step2.Id + "' has oversized arguments.");
			}
		}
		if (list.Count > 0)
		{
			return (Plan: null, Validation: new PlanValidation(IsValid: false, list));
		}
		string scenarioHash = Sha256(JsonSerializer.Serialize(scenario, JsonOptions));
		string planId = Guid.NewGuid().ToString("N");
		PlannedStep[] steps = scenario.Steps.Select((ScenarioStep s, int i) => new PlannedStep(i + 1, s.Id, s.Action, s.Arguments)).ToArray();
		string[] assertions = new string[3]
		{
			"Entity life state is established from adapter telemetry, not pixels alone.",
			"Expected outcome: " + scenario.ExpectedOutcome + ".",
			"Unknown or missing entity evidence remains inconclusive."
		};
		string[] array = new string[4] { "ordered telemetry timeline", "target identity", "screenshot or explicit unavailable reason", "video or explicit unavailable reason" };
		string planHash = Sha256(JsonSerializer.Serialize(new
		{
			planId = planId,
			scenarioId = scenario.Id,
			scenarioHash = scenarioHash,
			adapterId = capabilities.AdapterId,
			targetId = target.TargetId,
			timeoutSeconds = scenario.TimeoutSeconds,
			expectedOutcome = scenario.ExpectedOutcome,
			steps = steps,
			assertions = assertions,
			evidence = array
		}, JsonOptions));
		return (Plan: new QaPlan(planId, scenario.Id, scenarioHash, capabilities.AdapterId, target.TargetId, DateTimeOffset.UtcNow, scenario.TimeoutSeconds, steps, assertions, array, planHash, scenario.ExpectedOutcome), Validation: new PlanValidation(IsValid: true, Array.Empty<string>()));
	}

	public static Verdict JudgeDeath(EntitySnapshot? before, EntitySnapshot? after, string expectedDeath)
	{
		if ((object)before == null || (object)after == null || before.Provenance == "visual-only" || after.Provenance == "visual-only")
		{
			return Verdict.Inconclusive;
		}
		bool flag = before.LifeState == EntityLifeState.Alive;
		if (flag)
		{
			EntityLifeState lifeState = after.LifeState;
			bool flag2 = (uint)(lifeState - 1) <= 1u;
			flag = flag2;
		}
		bool flag3 = flag;
		if (expectedDeath == "dead" != flag3)
		{
			return Verdict.Fail;
		}
		return Verdict.Pass;
	}

	private static string Sha256(string value)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
	}
}
