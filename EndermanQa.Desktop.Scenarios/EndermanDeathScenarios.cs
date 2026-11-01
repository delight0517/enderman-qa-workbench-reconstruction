using System.Collections.Generic;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Scenarios;

public static class EndermanDeathScenarios
{
	public static QaScenario BareHandKillCase()
	{
		return BareHandPlayerKillCase();
	}

	public static QaScenario BareHandPlayerKillCase()
	{
		return new QaScenario("enderman.death.idea-001", 1, "맨손 Player.attack 사망 원인 및 공격자 확인", "격리 서버에서 관리 대상 Enderman의 자연 이동/순간이동 AI를 비활성화한 테스트 전용 조건으로 맨손 Player.attack을 반복한다. 사망 시 원인이 player이고 마지막 공격자가 동일한 synthetic test player인지 확인한다. 자연 행동/클라이언트 입력은 범위 밖이다.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("bare-hand-attacks-batch-1", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "bare_hand",
				["hitCount"] = "64",
				["disableTargetAi"] = "true"
			}),
			new ScenarioStep("bare-hand-attacks-batch-2", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "bare_hand",
				["hitCount"] = "64",
				["disableTargetAi"] = "true"
			})
		}), 240, "bare-hand-player-kill");
	}

	public static QaScenario BareHandKillCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "엔더맨의 회피 순간이동으로 일부 타격이 무효화될 수 있으므로, 정상 서버 틱 간격을 둔 Player.attack을 최대 2개 배치(각 64회) 실행하고 사망 상태를 확인한다. 이는 클라이언트 추적/조작 재현이 아니라 서버 측 반복 타격 검사다.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("bare-hand-attacks-batch-1", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "bare_hand",
				["hitCount"] = "64"
			}),
			new ScenarioStep("bare-hand-attacks-batch-2", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "bare_hand",
				["hitCount"] = "64"
			})
		}), 240);
	}

	public static QaScenario SimulatedMeleeKillCase(int sourceId, string title, string itemId)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "가득 충전된 테스트 플레이어로 실제 Minecraft Player.attack을 반복하고 서버의 사망/공격자 상태를 확인한다. 클라이언트 입력과 사람의 추적 이동은 별도 범위다.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[2]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("weapon-attacks", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = itemId,
				["hitCount"] = "64"
			})
		}), 90);
	}

	public static QaScenario CompareWoodenAndStoneSwordCase()
	{
		return new QaScenario("enderman.death.idea-003", 1, "돌 검과 나무 검 피해 비교", "독립된 두 엔더맨에 가득 충전된 바닐라 Player.attack을 각각 한 번 적용해 나무 검과 돌 검의 실제 HP 감소량을 비교한다. 전투 입력/클라이언트 경로는 범위 밖이다.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("control-attack", "entity.player_attack", new Dictionary<string, string> { ["itemId"] = "wooden_sword" }),
			new ScenarioStep("spawn-profile", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("profile-attack", "entity.player_attack", new Dictionary<string, string> { ["itemId"] = "stone_sword" })
		}), 30, "second-hit-stronger");
	}

	public static QaScenario SimulatedMeleeCase(int sourceId, string title, string itemId, string expectedOutcome = "damage")
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "격리 서버의 실제 Minecraft Player.attack 로직으로 지정 아이템의 피해 적용 여부를 확인한다. 입력/네트워크 경로는 범위 밖이다.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[2]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("player-attack", "entity.player_attack", new Dictionary<string, string> { ["itemId"] = itemId })
		}), 30, expectedOutcome);
	}

	public static QaScenario RapidDoubleAttackCase(int sourceId, string title, string itemId, string exactDamage)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Issue two attacks in one authoritative server action without a simulated cooldown interval and verify hurt immunity prevents duplicate damage. This is a server-side attack test, not raw keyboard input.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[2]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("rapid-double-hit", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = itemId,
				["hitCount"] = "2"
			})
		}), 30, "damage-exactly:" + exactDamage);
	}

	public static QaScenario AlternatingTestPlayersCase(int sourceId, string title, string itemId)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Create two distinct synthetic server-side player fixtures and apply one attack from each to the same managed Enderman. Human network concurrency is not modeled.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("player-a-hit", "entity.player_attack", new Dictionary<string, string> { ["itemId"] = itemId }),
			new ScenarioStep("player-b-hit", "entity.player_attack", new Dictionary<string, string> { ["itemId"] = itemId })
		}), 45, "damage");
	}

	public static QaScenario MeleeProfileHitCase(int sourceId, string title, string itemId, string attackProfile)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply one full-charge authoritative server Player.attack using the allowlisted `" + attackProfile + "` profile and record health/fire/durability telemetry. Client input is outside this scenario.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "entity.player_attack", "entity.player_attack.profile.v1" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[2]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("profile-hit", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = itemId,
				["attackProfile"] = attackProfile
			})
		}), 30, "damage");
	}

	public static QaScenario OneDurabilityLethalHit(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Set a managed Enderman to 6 HP, then apply one diamond-sword hit using a server-side test weapon with one durability remaining. Verify terminal death and record whether the held stack reached zero durability. Client inventory animation and a count of rendered break events are outside this server-only check.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "entity.player_attack", "entity.player_attack.profile.v1" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("set-low-health", "environment.configure", new Dictionary<string, string> { ["hazard"] = "health-6" }),
			new ScenarioStep("lethal-one-durability-hit", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "diamond_sword",
				["attackProfile"] = "oneDurability"
			})
		}), 45);
	}

	public static QaScenario RainStopsCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply rain at the verified managed target, observe briefly, clear weather, then verify the managed Enderman remains loaded/alive. Shelter collision and post-rain damage curves are not asserted.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[6]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string>
			{
				["entityType"] = "minecraft:enderman",
				["hazard"] = "rain-ready"
			}),
			new ScenarioStep("rain", "environment.configure", new Dictionary<string, string> { ["hazard"] = "rain" }),
			new ScenarioStep("observe-rain", "time.advance", new Dictionary<string, string> { ["seconds"] = "2" }),
			new ScenarioStep("clear-weather", "environment.configure", new Dictionary<string, string> { ["hazard"] = "rain-clear" }),
			new ScenarioStep("observe-clear", "time.advance", new Dictionary<string, string> { ["seconds"] = "3" }),
			new ScenarioStep("terminal-state", "observation.request", new Dictionary<string, string>())
		}), 240, "survive");
	}

	public static QaScenario CompareAttackProfile(int sourceId, string title, string itemId, string attackProfile, string expectedOutcome)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Compare two full-charge server-side Minecraft Player.attack calls against the same run-scoped Enderman using the allowlisted `" + attackProfile + "` test-player profile. This measures server combat logic only; client input and visual behavior are not exercised.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "entity.player_attack", "entity.player_attack.profile.v1" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("control-attack", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = itemId,
				["attackProfile"] = "standard"
			}),
			new ScenarioStep("spawn-profile", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("profile-attack", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = itemId,
				["attackProfile"] = attackProfile
			})
		}), 45, expectedOutcome);
	}

	public static QaScenario MeleeKillWithProfile(int sourceId, string title, string itemId, string attackProfile, int hitCount = 64)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply a bounded sequence of vanilla server Player.attack calls with the allowlisted `" + attackProfile + "` profile and confirm terminal death state.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "entity.player_attack", "entity.player_attack.profile.v1" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[2]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("profile-attacks", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = itemId,
				["attackProfile"] = attackProfile,
				["hitCount"] = hitCount.ToString()
			})
		}), 90);
	}

	public static QaScenario MeleeDeathLifecycle(int sourceId, string title, string itemId)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Kill the managed Enderman through vanilla server Player.attack, then observe its authoritative terminal state for five seconds to detect reappearance or a duplicate lifecycle transition.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "entity.player_attack", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("lethal-attack", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = itemId,
				["hitCount"] = "64"
			}),
			new ScenarioStep("observe-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "5" }),
			new ScenarioStep("terminal-observation", "observation.request", new Dictionary<string, string>())
		}), 120);
	}

	public static QaScenario FireDeathArtifactsCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Capture same-dimension item/XP entity UUIDs before a managed Enderman receives one vanilla inFire damage event at 1 HP. Compare post-event artifact UUIDs against the pre-action dimension snapshot. This isolates environmental death attribution and XP/drop policy, not natural fire ticks or Enderman teleport behavior.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("artifact-baseline", "observation.request", new Dictionary<string, string>
			{
				["captureDeathArtifacts"] = "true",
				["artifactBaseline"] = "true"
			}),
			new ScenarioStep("fire-lethal-hit", "environment.configure", new Dictionary<string, string> { ["hazard"] = "fire-lethal-pulse" }),
			new ScenarioStep("observe-fire-outcome", "observation.request", new Dictionary<string, string>()),
			new ScenarioStep("artifact-after", "observation.request", new Dictionary<string, string> { ["captureDeathArtifacts"] = "true" })
		}), 60, "fire-death-artifacts-observed");
	}

	public static QaScenario LootingDropPairCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "In one server trial, kill two fresh managed Endermen with vanilla iron swords: one unenchanted control and one with a verified Looting I profile. Capture same-dimension item/XP UUID deltas after each death. This is a paired exploratory observation only; three pairs cannot establish a probabilistic drop-rate difference.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "entity.player_attack", "entity.player_attack.profile.v1", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[10]
		{
			new ScenarioStep("spawn-control", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("control-health", "environment.configure", new Dictionary<string, string> { ["hazard"] = "health-6" }),
			new ScenarioStep("control-artifact-baseline", "observation.request", new Dictionary<string, string>
			{
				["captureDeathArtifacts"] = "true",
				["artifactBaseline"] = "true"
			}),
			new ScenarioStep("control-kill", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "iron_sword",
				["hitCount"] = "1",
				["disableTargetAi"] = "true"
			}),
			new ScenarioStep("control-artifacts", "observation.request", new Dictionary<string, string> { ["captureDeathArtifacts"] = "true" }),
			new ScenarioStep("spawn-looting", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("looting-health", "environment.configure", new Dictionary<string, string> { ["hazard"] = "health-6" }),
			new ScenarioStep("looting-artifact-baseline", "observation.request", new Dictionary<string, string>
			{
				["captureDeathArtifacts"] = "true",
				["artifactBaseline"] = "true"
			}),
			new ScenarioStep("looting-kill", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "iron_sword",
				["attackProfile"] = "looting1",
				["hitCount"] = "1",
				["disableTargetAi"] = "true"
			}),
			new ScenarioStep("looting-artifacts", "observation.request", new Dictionary<string, string> { ["captureDeathArtifacts"] = "true" })
		}), 120, "paired-looting-deaths-observed");
	}

	public static QaScenario WaterDeathArtifactsCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Capture a same-dimension item/XP baseline, set one managed Enderman to 1 HP, apply exactly one vanilla DROWN damage-source pulse, then observe terminal cause and the new artifact UUID set. This is a controlled source-pulse experiment, not continuous physical water exposure or autonomous teleport behavior.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("artifact-baseline", "observation.request", new Dictionary<string, string>
			{
				["captureDeathArtifacts"] = "true",
				["artifactBaseline"] = "true"
			}),
			new ScenarioStep("water-lethal", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-lethal-pulse" }),
			new ScenarioStep("observe-water-outcome", "observation.request", new Dictionary<string, string>()),
			new ScenarioStep("artifact-after", "observation.request", new Dictionary<string, string> { ["captureDeathArtifacts"] = "true" })
		}), 60, "water-death-artifacts-observed");
	}

	public static QaScenario LavaDeathArtifactsCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Capture same-dimension item/XP UUIDs, place a read-back-verified managed lava block at the Enderman fixture, set the target to 1 HP, and apply one vanilla lava damage-source pulse. Observe terminal cause, wait two seconds of server time, then inspect the artifact UUID delta. This does not inspect carried blocks or game visuals.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "observation.request", "environment.configure" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[6]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("artifact-baseline", "observation.request", new Dictionary<string, string>
			{
				["captureDeathArtifacts"] = "true",
				["artifactBaseline"] = "true"
			}),
			new ScenarioStep("lava-lethal", "environment.configure", new Dictionary<string, string> { ["hazard"] = "lava-lethal-pulse" }),
			new ScenarioStep("observe-lava-outcome", "observation.request", new Dictionary<string, string>()),
			new ScenarioStep("lava-item-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "2" }),
			new ScenarioStep("artifact-after", "observation.request", new Dictionary<string, string> { ["captureDeathArtifacts"] = "true" })
		}), 60, "lava-death-artifacts-observed");
	}

	public static QaScenario CustomComponentDeathCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Give the synthetic vanilla iron sword one allowlisted CUSTOM_NAME data component, verify the server reads it back, then apply one lethal Player.attack to a managed 6-HP Enderman and confirm terminal player attribution. This checks a harmless component path only, not arbitrary cursed or custom combat components.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "entity.player_attack", "entity.player_attack.profile.v1" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("low-health", "environment.configure", new Dictionary<string, string> { ["hazard"] = "health-6" }),
			new ScenarioStep("component-attack", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "iron_sword",
				["attackProfile"] = "customName1",
				["hitCount"] = "1",
				["disableTargetAi"] = "true"
			})
		}), 60, "custom-component-attack-recorded");
	}

	public static QaScenario EnvironmentalFall()
	{
		return new QaScenario("enderman.death.fall-platform", 1, "Enderman 낙하 피해 사망 실험", "격리 월드에서 높이 차가 있는 발판을 제거하고 실제 낙하 피해 후 사망 여부를 서버 상태로 확인한다.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn-fall", "entity.spawn", new Dictionary<string, string>
			{
				["entityType"] = "minecraft:enderman",
				["hazard"] = "fall-ready"
			}),
			new ScenarioStep("baseline", "observation.request", new Dictionary<string, string> { ["capture"] = "pre-fall-state" }),
			new ScenarioStep("apply-fall", "environment.configure", new Dictionary<string, string> { ["hazard"] = "fall" }),
			new ScenarioStep("observe-fall", "observation.request", new Dictionary<string, string>
			{
				["until"] = "dead-or-timeout",
				["timeoutSeconds"] = "20"
			})
		}), 45);
	}

	public static QaScenario EnvironmentalWater()
	{
		return new QaScenario("enderman.death.water-contact", 1, "Enderman 물 접촉 반응 실험", "격리 월드에서 물 블록에 정확히 한 서버 틱 접촉시킨 뒤 물을 제거하고 생존 여부를 확인한다. 물 피해 후 순간이동은 확률 동작이므로 필수 통과 조건으로 강제하지 않는다.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn-water", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("baseline", "observation.request", new Dictionary<string, string> { ["capture"] = "pre-water-state" }),
			new ScenarioStep("apply-water-once", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-pulse" }),
			new ScenarioStep("observe-water", "observation.request", new Dictionary<string, string>())
		}), 45, "survive");
	}

	public static QaScenario WaterThenMeleeCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply one confirmed server-tick water contact to a run-scoped Enderman, clear the managed water fixture, wait 1.2 seconds for the vanilla hurt-immunity interval to expire, then issue one vanilla server Player.attack against the same native UUID. Require that the attack step itself reports exactly one applied hit. This is partial server coverage: it does not count duplicate damage events or reproduce client-side reach/input.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("water-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-pulse" }),
			new ScenarioStep("hurt-immunity-clear", "time.advance", new Dictionary<string, string> { ["seconds"] = "1.2" }),
			new ScenarioStep("single-melee-action", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "bare_hand",
				["hitCount"] = "1"
			})
		}), 45, "attack-applied");
	}

	public static QaScenario WaterStopsDamageCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply one confirmed server-tick water contact, remove only the bridge-owned water block, then compare the authoritative alive/health snapshot at removal with a snapshot after a two-second dry window. This does not measure continuous fluid cadence, a scripted teleport destination, or client visuals.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("water-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-pulse" }),
			new ScenarioStep("dry-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "2" }),
			new ScenarioStep("observe-dry", "observation.request", new Dictionary<string, string>())
		}), 45, "health-stable-after-water-clear");
	}

	public static QaScenario ProjectileNoDamageCase(int sourceId, string title, string projectileType)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Spawn one vanilla server-side " + projectileType + " with a managed shooter and fire it at the managed Enderman. The oracle requires an authoritative on-entity-hit record whose hit UUID equals the target UUID; only then compare target health/alive state. Block impact or timeout remains Inconclusive, never a pass.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("projectile-hit", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = projectileType }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 45, "projectile-no-damage");
	}

	public static QaScenario ProjectileBlockedCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Place one run-owned obsidian barrier in the vanilla arrow path. Require a server block-hit callback, no entity-hit on the managed Enderman, and unchanged target health/alive state. Fixture release restores the exact prior block.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("barrier", "environment.configure", new Dictionary<string, string> { ["hazard"] = "projectile-wall" }),
			new ScenarioStep("projectile-hit", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "arrow" }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 45, "projectile-blocked-no-damage");
	}

	public static QaScenario ProjectileImpactTelemetryCase(int sourceId, string title, string projectileType)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Fire one managed vanilla " + projectileType + " at a managed Enderman and record exact collision identity, HP before/after, alive state, and server fire ticks. This exploratory check validates telemetry and impact attribution; it does not emulate player bow input.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("projectile-hit", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = projectileType }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 45, "projectile-impact-recorded");
	}

	public static QaScenario ProjectileVehicleCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Disable target AI for fixture stability, mount the managed Enderman in one run-owned oak boat, and fire one vanilla arrow. Record the exact collision UUID/type and authoritative target HP/life state. This is a controlled boat-only partial probe; it does not cover minecarts, client input, or all projectile types.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("freeze-target-ai", "environment.configure", new Dictionary<string, string> { ["hazard"] = "disable-target-ai" }),
			new ScenarioStep("mount-boat", "environment.configure", new Dictionary<string, string> { ["hazard"] = "vehicle-boat" }),
			new ScenarioStep("projectile-hit", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "arrow" }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 60, "projectile-vehicle-impact-recorded");
	}

	public static QaScenario CrossbowFireworkCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Disable target AI, launch one vanilla firework rocket from a crossbow at the managed Enderman, and record exact projectile collision plus authoritative damage/death events. This is one fixed small-ball rocket recipe; it does not cover player input, custom recipes or blast-radius boundaries.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("freeze-target-ai", "environment.configure", new Dictionary<string, string> { ["hazard"] = "disable-target-ai" }),
			new ScenarioStep("firework-hit", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "firework_crossbow" }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 60, "crossbow-firework-explosion");
	}

	public static QaScenario SplashWaterPotionCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Throw one actual vanilla water splash potion at a managed Enderman. Require the tracked splash potion's impact UUID to equal the exact target UUID, then compare authoritative health, life state and position for water damage or Enderman relocation. No splash-radius boundary or cloud behavior is inferred.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("isolate-target-ai", "environment.configure", new Dictionary<string, string> { ["hazard"] = "disable-target-ai" }),
			new ScenarioStep("splash-landing-pads", "environment.configure", new Dictionary<string, string> { ["hazard"] = "splash-radius-pad" }),
			new ScenarioStep("potion-impact", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "splash_water_potion" }),
			new ScenarioStep("post-impact", "observation.request", new Dictionary<string, string>())
		}), 60, "splash-water-potion-effect");
	}

	public static QaScenario SplashWaterRadiusCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Disable target AI, first land a real vanilla water splash potion at a point 4.5 blocks laterally away, then at a point 2.5 blocks away. Record exact projectile outcome, impact position, target HP and damage events after each. This is a fixed two-distance server probe, not a full splash-radius boundary sweep.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[7]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("isolate-target-ai", "environment.configure", new Dictionary<string, string> { ["hazard"] = "disable-target-ai" }),
			new ScenarioStep("splash-landing-pads", "environment.configure", new Dictionary<string, string> { ["hazard"] = "splash-radius-pad" }),
			new ScenarioStep("potion-outside", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "splash_water_outside" }),
			new ScenarioStep("observe-outside", "observation.request", new Dictionary<string, string>()),
			new ScenarioStep("potion-inside", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "splash_water_inside" }),
			new ScenarioStep("observe-inside", "observation.request", new Dictionary<string, string>())
		}), 90, "splash-water-radius-recorded");
	}

	public static QaScenario LingeringWaterCloudCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Spawn an actual Minecraft AreaEffectCloud with water potion contents, radius 2.5 and duration 400 ticks above the managed Enderman; observe a bounded 20-second server window and record HP/damage membership. Exploratory coverage does not assert cloud exit or teleport behavior.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("water-cloud", "environment.configure", new Dictionary<string, string> { ["hazard"] = "lingering-water-cloud" }),
			new ScenarioStep("cloud-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "20" }),
			new ScenarioStep("observe-after-cloud", "observation.request", new Dictionary<string, string>())
		}), 60, "lingering-water-cloud-window");
	}

	public static QaScenario WaterBlockedTeleportAttemptCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Spawn one managed Enderman on a verified dry pad with AI disabled. Place a run-owned water block beneath one explicit teleport candidate eight blocks away, confirm Enderman.canRandomlyTeleportTo rejects it, call the vanilla Enderman.teleport candidate method once, and compare position, health, life state and damage-event count. This is one controlled candidate attempt, not proof that every random destination is blocked or that autonomous water-avoidance AI retries fail.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("isolate-ai", "environment.configure", new Dictionary<string, string> { ["hazard"] = "disable-target-ai" }),
			new ScenarioStep("attempt-water-destination", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-blocked-teleport-attempt" }),
			new ScenarioStep("observe-after-attempt", "observation.request", new Dictionary<string, string>())
		}), 30, "water-blocked-teleport-no-duplicate-damage");
	}

	public static QaScenario LingeringWaterCloudExitCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Spawn a verified unowned long-lived water AreaEffectCloud, observe the managed Enderman inside it for five seconds, move it to a managed safe pad anchored 12 blocks from the recorded cloud origin, observe and record position after one second, then observe nine more seconds and compare health/life/damage-event count. If no damage occurred in-cloud, report that limitation rather than claiming an observed damage cadence stopped.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[9]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("isolate-ai", "environment.configure", new Dictionary<string, string> { ["hazard"] = "disable-target-ai" }),
			new ScenarioStep("water-cloud", "environment.configure", new Dictionary<string, string> { ["hazard"] = "lingering-water-cloud-long" }),
			new ScenarioStep("cloud-entry-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "5" }),
			new ScenarioStep("leave-cloud", "environment.configure", new Dictionary<string, string> { ["hazard"] = "move-target-outside-cloud" }),
			new ScenarioStep("initial-exit-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "1" }),
			new ScenarioStep("observe-initial-exit", "observation.request", new Dictionary<string, string>()),
			new ScenarioStep("cloud-after-exit", "time.advance", new Dictionary<string, string> { ["seconds"] = "9" }),
			new ScenarioStep("observe-after-exit", "observation.request", new Dictionary<string, string>())
		}), 60, "lingering-water-cloud-exit");
	}

	public static QaScenario SplashEffectPotionCase(int sourceId, string title, string projectileType, string expectedOutcome, bool setLowHealth = false)
	{
		string id = $"enderman.death.idea-{sourceId:d3}";
		string objective = $"Throw one actual vanilla {projectileType} at the managed Enderman and require an exact tracked projectile-to-target UUID hit. {(setLowHealth ? "The target is set to 6 HP first." : "The target starts at full health.")} Compare the authoritative health/life state after the impact; this does not cover splash-radius boundaries, clouds or client throw input.";
		IReadOnlyList<string> requiredCapabilities;
		if (!setLowHealth)
		{
			IReadOnlyList<string> readOnlyList = new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "projectile.throw", "observation.request" });
			requiredCapabilities = readOnlyList;
		}
		else
		{
			IReadOnlyList<string> readOnlyList = new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "projectile.throw", "observation.request" });
			requiredCapabilities = readOnlyList;
		}
		IReadOnlyList<ScenarioStep> steps;
		if (!setLowHealth)
		{
			IReadOnlyList<ScenarioStep> readOnlyList2 = new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
			{
				new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
				new ScenarioStep("potion-impact", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = projectileType }),
				new ScenarioStep("post-impact", "observation.request", new Dictionary<string, string>())
			});
			steps = readOnlyList2;
		}
		else
		{
			IReadOnlyList<ScenarioStep> readOnlyList2 = new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
			{
				new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
				new ScenarioStep("low-health", "environment.configure", new Dictionary<string, string> { ["hazard"] = "health-6" }),
				new ScenarioStep("potion-impact", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = projectileType }),
				new ScenarioStep("post-impact", "observation.request", new Dictionary<string, string>())
			});
			steps = readOnlyList2;
		}
		return new QaScenario(id, 1, title, objective, requiredCapabilities, steps, 60, expectedOutcome);
	}

	public static QaScenario SplashPotionPairCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "In one server-thread action, launch one vanilla water potion and one vanilla Harming potion toward the same managed Enderman. Require both exact projectile UUIDs to hit the target during the same game tick, and compare damage-event count with the water-only expected result. The Harming potion should not add a second hit because Endermen only take direct potion damage from water-sensitive-tagged contents.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("potion-pair", "projectile.throw", new Dictionary<string, string>
			{
				["projectileType"] = "splash_water_potion",
				["secondProjectileType"] = "splash_harming_potion"
			}),
			new ScenarioStep("post-impact", "observation.request", new Dictionary<string, string>())
		}), 60, "splash-potion-sources-distinct");
	}

	public static QaScenario IceSlideFallCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Place a reversible five-block packed-ice runway beside a readback-verified sealed dry shaft and stone landing floor, preserve normal Enderman AI and gravity, apply one bounded horizontal velocity, then measure displacement, fall depth and authoritative damage cause. This controlled fixture does not generalize to every natural ice layout.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("ice-slope", "environment.configure", new Dictionary<string, string> { ["hazard"] = "ice-slope" }),
			new ScenarioStep("physics-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "3" }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 45, "ice-slide-fall");
	}

	public static QaScenario ProjectilePairTelemetryCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "On one managed Enderman, launch a bow-backed arrow and a crossbow-backed arrow sequentially. Require each vanilla projectile callback to report an entity hit on the exact managed UUID. This is collision/telemetry evidence only; it does not test Enderman AI evasion, player input, or compare evasion rates.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[4]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("bow-projectile-hit", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "arrow" }),
			new ScenarioStep("crossbow-projectile-hit", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "crossbow_arrow" }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 60, "projectile-pair-recorded");
	}

	public static QaScenario ProjectileTripleTelemetryCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Launch three independently tracked vanilla arrows sequentially at the same managed Enderman and record each collision UUID and health snapshot. This establishes per-projectile attribution only; it does not emulate simultaneous arrival or client/network timing.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("arrow-one", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "arrow" }),
			new ScenarioStep("arrow-two", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "arrow" }),
			new ScenarioStep("arrow-three", "projectile.throw", new Dictionary<string, string> { ["projectileType"] = "arrow" }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 75, "projectile-triple-recorded");
	}

	public static QaScenario ProjectileTeleportStaleCoordinateCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Launch a vanilla arrow at the managed Enderman's original position, then move that same UUID to a verified safe pad on the same server-thread action before the arrow ticks. Observe the pending arrow outcome and target HP. This isolates stale-coordinate damage; it does not test a human bow or autonomous teleport AI.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("projectile-hit", "projectile.throw", new Dictionary<string, string>
			{
				["projectileType"] = "arrow",
				["teleportTargetAfterLaunch"] = "true"
			}),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 45, "projectile-no-stale-hit");
	}

	public static QaScenario ProjectileSecondTargetAfterTeleportCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Server-only deterministic fixture: spawn managed Enderman A and B on one reserved ticking chunk, with B 3.25 blocks behind A along the vanilla arrow lane. Prepare a zero-inaccuracy vanilla arrow toward B, move A perpendicular to the lane, wait one server entity tick, and add the arrow only if A remains off-lane. Record the exact impact UUID and both entities' HP/life/damage-event deltas and movement because B may use vanilla projectile avoidance. This excludes player bow input, network latency, and autonomous Enderman AI/teleport selection.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "projectile.throw", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("projectile-hit", "projectile.throw", new Dictionary<string, string>
			{
				["projectileType"] = "arrow",
				["teleportTargetAfterLaunch"] = "true",
				["secondTargetAfterTeleport"] = "true"
			}),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 45, "projectile-impact-recorded");
	}

	public static QaScenario CactusOneContactCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply a run-owned cactus until its first authoritative cactus damage event, restore the original block immediately, then confirm the same managed Enderman remains alive with exactly one cactus damage event. This bounded contact test excludes magma and autonomous teleport timing.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("cactus-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "cactus-pulse" }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 45, "cactus-one-contact");
	}

	public static QaScenario EarlyDuplicateAttackCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply one vanilla server Player.attack, an immediate same-item repeat inside the target hurt-immunity window, wait two seconds of server ticks, then attack again. Record applied-hit counts and HP for each action. Does not emulate human clicks, client attack cooldown animation or reach.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "entity.player_attack", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[6]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("first-hit", "entity.player_attack", new Dictionary<string, string> { ["itemId"] = "iron_sword" }),
			new ScenarioStep("rapid-second-hit", "entity.player_attack", new Dictionary<string, string> { ["itemId"] = "iron_sword" }),
			new ScenarioStep("wait-recovery", "time.advance", new Dictionary<string, string> { ["seconds"] = "2" }),
			new ScenarioStep("recovered-hit", "entity.player_attack", new Dictionary<string, string> { ["itemId"] = "iron_sword" }),
			new ScenarioStep("observe-final", "observation.request", new Dictionary<string, string>())
		}), 60, "attack-cooldown-window");
	}

	public static QaScenario AttackImmediatelyBeforeTeleportCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Issue one iron-sword vanilla server Player.attack and, in the same server-thread action, relocate the surviving managed Enderman to a run-owned safe pad. Require exactly one hit and one normal hit-sized HP delta. This controlled transition does not test client timing or autonomous Enderman teleport selection.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.player_attack" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[2]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("attack-before-teleport", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "iron_sword",
				["hitCount"] = "1",
				["teleportAfterHit"] = "true"
			})
		}), 30, "attack-before-controlled-teleport-once");
	}

	public static QaScenario SweepingAttackMultiplicityCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Use one vanilla server Player.attack with a verified iron sword carrying Sweeping Edge I. The Fabric fixture creates one primary Enderman, one in-range neighbor and one out-of-range control, isolates AI, then records per-UUID health and damage-event deltas. This verifies only the fixed server-side sweep path; it does not test client attack input, aim, latency, or natural Enderman behavior.", new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "entity.spawn", "entity.sweep_attack.v1" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[2]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("sweep", "entity.sweep_attack", new Dictionary<string, string>
			{
				["itemId"] = "iron_sword",
				["attackProfile"] = "sweeping1",
				["hitCount"] = "1",
				["disableTargetAi"] = "true"
			})
		}), 45, "sweep-per-uuid-once");
	}

	public static QaScenario WaterSourceFlowComparison(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "On two separately spawned managed Endermen, apply a verified source-water block and a verified flowing-water block for one authoritative server tick each. Record fluid amount/source state and post-contact HP/alive/position. This is a controlled one-tick comparison, not a sustained damage cadence or player movement test.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn-source", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("source-water-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-source-pulse" }),
			new ScenarioStep("spawn-flowing", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("flowing-water-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-flowing-pulse" }),
			new ScenarioStep("observe-flowing", "observation.request", new Dictionary<string, string>())
		}), 60, "water-source-flowing-recorded");
	}

	public static QaScenario WaterDepthComparison(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "On two separately spawned managed Endermen, apply a one-block source-water fixture and a three-block source-water column for one authoritative tick each. Verify each block's fluid state before the contact tick, restore every owned block, and record each target's HP/alive/position after exposure. This does not measure sustained damage cadence or teleport count.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn-shallow", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("shallow-water-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-shallow-pulse" }),
			new ScenarioStep("spawn-deep", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("deep-water-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-deep-pulse" }),
			new ScenarioStep("observe-deep", "observation.request", new Dictionary<string, string>())
		}), 60, "water-depth-compare-recorded");
	}

	public static QaScenario RepeatedWaterContacts(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply three distinct one-tick source-water contacts to the same managed Enderman, removing each fixture before the next. Record HP/alive after each pulse and require per-contact loss ≤1 HP and total loss ≤3 HP. This is a discrete-contact bound, not continuous damage cadence or teleport-event counting.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("water-contact-1", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-pulse" }),
			new ScenarioStep("water-contact-2", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-pulse" }),
			new ScenarioStep("water-contact-3", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-pulse" }),
			new ScenarioStep("observe-final", "observation.request", new Dictionary<string, string>())
		}), 60, "repeated-water-contact-bounded");
	}

	public static QaScenario LavaTeleportOrderingCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Set a managed Enderman to 6 HP, place one reversible lava block at its occupied block and verify the block readback. Observe the server result for eight seconds. Record only a lava-attributed terminal death or survival with at least eight blocks of displacement; missing authoritative state is inconclusive. This does not assert client animation or teleport visuals.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("set-low-health", "environment.configure", new Dictionary<string, string> { ["hazard"] = "health-6" }),
			new ScenarioStep("lava-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "lava" }),
			new ScenarioStep("lava-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "8" }),
			new ScenarioStep("observe-final", "observation.request", new Dictionary<string, string>())
		}), 60, "lava-escape-or-lethal");
	}

	public static QaScenario LavaEscapeDamageStopsCase(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Apply verified managed lava, observe one second of server exposure, then teleport into a run-owned dry 5x5 obsidian enclosure, restore the original lava block, disable target AI and clear fire ticks. Read back all floor, wall and dry interior cells before comparing the authoritative health, damage-event count and position after three seconds. This partial check does not establish autonomous Enderman teleport selection or natural fire-tick clearing.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[7]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("lava-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "lava" }),
			new ScenarioStep("lava-exposure-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "1" }),
			new ScenarioStep("observe-before-escape", "observation.request", new Dictionary<string, string>()),
			new ScenarioStep("safe-escape", "environment.configure", new Dictionary<string, string> { ["hazard"] = "lava-escape" }),
			new ScenarioStep("post-escape-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "3" }),
			new ScenarioStep("observe-after-escape", "observation.request", new Dictionary<string, string>())
		}), 60, "damage-stops-after-lava-escape");
	}

	public static QaScenario FireTicksClearedCase(int sourceId, string title, bool clearWithWater)
	{
		string id = $"enderman.death.idea-{sourceId:d3}";
		string objective = (clearWithWater ? "Ignite the managed Enderman in the disposable fixture, require positive server fire ticks, apply one server-tick water contact, and compare the authoritative fire-tick count after contact." : "Ignite the managed Enderman in the disposable fixture, require positive server fire ticks, remove the owned fire block and extinguish the managed entity, then verify the authoritative fire-tick count is zero.");
		_003C_003Ez__ReadOnlyArray<string> requiredCapabilities = new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" });
		IReadOnlyList<ScenarioStep> steps;
		if (!clearWithWater)
		{
			IReadOnlyList<ScenarioStep> readOnlyList = new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[6]
			{
				new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
				new ScenarioStep("ignite", "environment.configure", new Dictionary<string, string> { ["hazard"] = "fire" }),
				new ScenarioStep("wait-for-fire", "time.advance", new Dictionary<string, string> { ["seconds"] = "1" }),
				new ScenarioStep("fire-active", "observation.request", new Dictionary<string, string>()),
				new ScenarioStep("clear-fire", "environment.configure", new Dictionary<string, string> { ["hazard"] = "fire-clear" }),
				new ScenarioStep("fire-cleared", "observation.request", new Dictionary<string, string>())
			});
			steps = readOnlyList;
		}
		else
		{
			IReadOnlyList<ScenarioStep> readOnlyList = new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[6]
			{
				new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
				new ScenarioStep("ignite", "environment.configure", new Dictionary<string, string> { ["hazard"] = "fire" }),
				new ScenarioStep("wait-for-fire", "time.advance", new Dictionary<string, string> { ["seconds"] = "1" }),
				new ScenarioStep("fire-active", "observation.request", new Dictionary<string, string>()),
				new ScenarioStep("water-contact", "environment.configure", new Dictionary<string, string> { ["hazard"] = "water-pulse" }),
				new ScenarioStep("fire-cleared", "observation.request", new Dictionary<string, string>())
			});
			steps = readOnlyList;
		}
		return new QaScenario(id, 1, title, objective, requiredCapabilities, steps, 45, "fire-ticks-cleared");
	}

	public static QaScenario EnvironmentalHazardCase(int sourceId, string title, string hazard, string expectedOutcome = "damage", int timeoutSeconds = 20)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "격리 월드에서 실제 Minecraft " + hazard + " 환경 효과를 적용하고 체력/생존 상태를 관측한다. 화면/클라이언트 이동 검증은 별도다.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("hazard", "environment.configure", new Dictionary<string, string> { ["hazard"] = hazard }),
			new ScenarioStep("observe", "observation.request", new Dictionary<string, string>
			{
				["until"] = "dead-or-timeout",
				["timeoutSeconds"] = timeoutSeconds.ToString()
			})
		}), timeoutSeconds + 20, expectedOutcome);
	}

	public static QaScenario EnvironmentalRain(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Move the run-scoped Enderman onto a tracked, dry platform in a rain-capable biome with open sky, verify rain at its actual position, and observe survival.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string>
			{
				["entityType"] = "minecraft:enderman",
				["hazard"] = "rain-ready"
			}),
			new ScenarioStep("rain", "environment.configure", new Dictionary<string, string> { ["hazard"] = "rain" }),
			new ScenarioStep("observe", "observation.request", new Dictionary<string, string>
			{
				["until"] = "dead-or-timeout",
				["timeoutSeconds"] = "20"
			})
		}), 180, "survive");
	}

	public static QaScenario EnvironmentalRainShelter(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "On the same managed Enderman, verify rain reaches its exposed location, place a run-owned 5×5 solid roof above it, and verify rain/sky exposure becomes false and HP/alive state remains unchanged for two seconds. The roof is restored when the fixture is released. This is server-side exposure evidence only.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "time.advance", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[5]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string>
			{
				["entityType"] = "minecraft:enderman",
				["hazard"] = "rain-ready"
			}),
			new ScenarioStep("rain-exposure", "environment.configure", new Dictionary<string, string> { ["hazard"] = "rain" }),
			new ScenarioStep("rain-shelter", "environment.configure", new Dictionary<string, string> { ["hazard"] = "rain-shelter" }),
			new ScenarioStep("dry-window", "time.advance", new Dictionary<string, string> { ["seconds"] = "2" }),
			new ScenarioStep("observe-after", "observation.request", new Dictionary<string, string>())
		}), 60, "rain-shelter-stops-exposure");
	}

	public static QaScenario EnvironmentalPlayerAttackAttributionCase99(string title)
	{
		return new QaScenario("enderman.death.idea-099", 1, title, "Set the managed Enderman to six health, then in one authoritative server-thread action apply one inFire source event immediately before one vanilla synthetic-player iron-sword attack. Require the environmental event and terminal player-attributed death to share a server tick, with the death record identifying the attacker UUID. Lethal hits are sourced from AFTER_DEATH because Fabric does not emit AFTER_DAMAGE for the lethal player hit. This is controlled API ordering, not natural tick scheduling or client/network concurrency.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "entity.spawn", "environment.configure", "entity.player_attack", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("set-low-health", "environment.configure", new Dictionary<string, string> { ["hazard"] = "health-6" }),
			new ScenarioStep("environment-and-attack", "entity.player_attack", new Dictionary<string, string>
			{
				["itemId"] = "iron_sword",
				["environmentDamageSource"] = "inFire"
			})
		}), 45, "environment-player-attribution");
	}

	public static QaScenario ProjectileVolleyDeathArtifactsPartial(int sourceId, string title)
	{
		return new QaScenario($"enderman.death.idea-{sourceId:d3}", 1, title, "Controlled server-side partial probe: set the managed Enderman to six health, capture the artifact UUID baseline, then create eight tracked vanilla arrows in one server-thread action and reconcile all projectile outcomes, target death facts, and new XP/item entities. This exercises duplicate death/drop handling under a server-origin volley only; it does not apply client TCP latency, player bow input, or guarantee the Enderman teleports.", new _003C_003Ez__ReadOnlyArray<string>(new string[5] { "entity.spawn", "environment.configure", "observation.request", "projectile.throw_volley", "time.advance" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[7]
		{
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string> { ["entityType"] = "minecraft:enderman" }),
			new ScenarioStep("disable-target-ai", "environment.configure", new Dictionary<string, string> { ["hazard"] = "disable-target-ai" }),
			new ScenarioStep("set-low-health", "environment.configure", new Dictionary<string, string> { ["hazard"] = "health-6" }),
			new ScenarioStep("artifact-baseline", "observation.request", new Dictionary<string, string>
			{
				["captureDeathArtifacts"] = "true",
				["artifactBaseline"] = "true"
			}),
			new ScenarioStep("projectile-volley", "projectile.throw_volley", new Dictionary<string, string> { ["projectileCount"] = "8" }),
			new ScenarioStep("settle", "time.advance", new Dictionary<string, string> { ["seconds"] = "2" }),
			new ScenarioStep("artifact-after", "observation.request", new Dictionary<string, string>
			{
				["until"] = "dead-or-timeout",
				["timeoutSeconds"] = "8",
				["captureDeathArtifacts"] = "true",
				["artifactBaseline"] = "false"
			})
		}), 60, "projectile-volley-single-death-drop-set");
	}

	public static QaScenario ControlledFall()
	{
		return new QaScenario("enderman.death.controlled-fall", 1, "Controlled fall death", "Check that a tracked Enderman receives fall damage and death is confirmed by authoritative entity state.", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "fixture.create", "entity.spawn", "environment.configure", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[6]
		{
			new ScenarioStep("fixture", "fixture.create", new Dictionary<string, string> { ["kind"] = "isolated-flat-platform-and-drop-zone" }),
			new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string>
			{
				["entityType"] = "minecraft:enderman",
				["runScopedId"] = "auto"
			}),
			new ScenarioStep("baseline", "observation.request", new Dictionary<string, string> { ["capture"] = "pre-damage-state" }),
			new ScenarioStep("fall", "environment.configure", new Dictionary<string, string>
			{
				["hazard"] = "controlled-fall",
				["fixtureOwned"] = "true"
			}),
			new ScenarioStep("observe-death", "observation.request", new Dictionary<string, string>
			{
				["until"] = "dead-or-timeout",
				["timeoutSeconds"] = "20"
			}),
			new ScenarioStep("evidence", "capture.request", new Dictionary<string, string>
			{
				["when"] = "damage-and-terminal-state",
				["screenshot"] = "required",
				["video"] = "best-effort"
			})
		}), 60);
	}
}
