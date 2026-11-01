using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Adapters;
using EndermanQa.Desktop.Persistence;

namespace EndermanQa.Desktop.Scenarios;

internal static class Tc014ClientStalePositionRunner
{
	private sealed record AttemptOutcome(string RunId, string Verdict, string Signature, string Actual, string Limitation, string CaptureStatus, bool FixtureReleased, string JournalPath);

	private const int SourceId = 14;

	private const string CaseId = "enderman.death.idea-014";

	private const string Title = "순간이동 직후 공격이 이전 위치에 빗나가도 피해가 잘못 들어가지 않는가?";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	public static async Task<SuiteCaseReceipt> RunAsync(JavaServerWorkspace workspace, OwnedJavaServerProcess server, OwnedJavaClientProcess client, string instancesRoot, string projectDataRoot, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(workspace, "workspace");
		ArgumentNullException.ThrowIfNull(server, "server");
		ArgumentNullException.ThrowIfNull(client, "client");
		ValidateOwnership(workspace, server, client, instancesRoot);
		string suiteId = Guid.NewGuid().ToString("N");
		string suiteRoot = Path.Combine(Path.GetFullPath(projectDataRoot), "suite-runs", suiteId);
		Directory.CreateDirectory(suiteRoot);
		List<string> runIds = new List<string>();
		List<AttemptOutcome> outcomes = new List<AttemptOutcome>(3);
		await AppendLineAsync(Path.Combine(suiteRoot, "suite.jsonl"), new
		{
			kind = "suite-start",
			suiteId = suiteId,
			sourceId = 14,
			caseId = "enderman.death.idea-014",
			serverReuse = true,
			clientReuse = true,
			worldReuse = true,
			startedAt = DateTimeOffset.UtcNow
		}, cancellationToken);
		for (int attemptNumber = 1; attemptNumber <= 3; attemptNumber++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AttemptOutcome attemptOutcome = await RunAttemptAsync(workspace, server, client, instancesRoot, suiteRoot, projectDataRoot, attemptNumber, cancellationToken);
			outcomes.Add(attemptOutcome);
			runIds.Add(attemptOutcome.RunId);
			if (attemptOutcome.Verdict != "Fail")
			{
				break;
			}
		}
		AttemptOutcome first = outcomes[0];
		bool flag = outcomes.Count == 3 && outcomes.All((AttemptOutcome item) => item.Verdict == "Fail" && item.Signature == first.Signature);
		bool flag2 = outcomes.All((AttemptOutcome item) => item.Verdict == "Pass");
		bool flag3 = outcomes.Any((AttemptOutcome item) => item.Verdict == "Inconclusive");
		string verdict = (flag ? "Fail" : (flag2 ? "Pass" : "Inconclusive"));
		string status = (flag ? "failure-reproduced" : (flag2 ? "executed-partial" : "anomaly-unconfirmed"));
		string repeatability = (flag ? "confirmed-repeatable-same-signature" : ((outcomes.Count == 1 && flag2) ? "not-needed; expected behavior observed" : (flag3 ? "not-confirmed; incomplete or ambiguous client/server evidence" : "not-confirmed; anomaly did not repeat with the same signature")));
		string limitation = "Controlled cached-entity vanilla attack only; this does not test natural mouse aim, internet latency, or other network conditions. " + string.Join(" | ", (from item in outcomes
			select item.Limitation into item
			where !string.IsNullOrWhiteSpace(item)
			select item).Distinct());
		string journalPath = outcomes.LastOrDefault()?.JournalPath ?? string.Empty;
		SuiteCaseReceipt receipt = new SuiteCaseReceipt(14, "enderman.death.idea-014", "순간이동 직후 공격이 이전 위치에 빗나가도 피해가 잘못 들어가지 않는가?", status, verdict, "partial: client cached target before fixed server relocation, one vanilla attack, server health/damage/death reconciliation", outcomes[0].RunId, (from item in outcomes.Skip(1)
			select item.RunId).ToArray(), repeatability, (verdict == "Fail") ? first.Signature : string.Join("|", outcomes.Select((AttemptOutcome item) => item.Signature).Distinct()), limitation, string.Join("; ", outcomes.Select((AttemptOutcome item) => item.CaptureStatus).Distinct()), journalPath);
		await File.WriteAllTextAsync(Path.Combine(suiteRoot, "results.json"), JsonSerializer.Serialize(new SuiteCaseReceipt[1] { receipt }, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
		await File.WriteAllTextAsync(Path.Combine(suiteRoot, "expected-actions-selected.json"), JsonSerializer.Serialize(new[]
		{
			new
			{
				SourceId = 14,
				CaseId = "enderman.death.idea-014",
				Title = "순간이동 직후 공격이 이전 위치에 빗나가도 피해가 잘못 들어가지 않는가?",
				Preconditions = "App-owned Java 26.3 server and lease-bound client active in existing qa-world; managed Enderman fixture.",
				ExpectedActions = new string[5] { "observe exact UUID", "cache client reference", "relocate server-side", "attack cached reference once", "reconcile server" },
				ExpectedObservation = "Relocated Enderman remains alive with unchanged health and damage count.",
				PassCriteria = "Exact UUID moves >=8 blocks; client accepts one cached-reference attack; server reports no health loss, damage, or death.",
				Capability = "client stale-reference action and server-authoritative telemetry",
				PlanStatus = "executed"
			}
		}, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
		await AppendLineAsync(Path.Combine(suiteRoot, "suite.jsonl"), new
		{
			kind = "suite-finished",
			suiteId = suiteId,
			total = 1,
			executed = 1,
			verdict = verdict,
			repeatability = repeatability,
			finishedAt = DateTimeOffset.UtcNow
		}, CancellationToken.None);
		return receipt;
	}

	private static async Task<AttemptOutcome> RunAttemptAsync(JavaServerWorkspace workspace, OwnedJavaServerProcess server, OwnedJavaClientProcess client, string instancesRoot, string suiteRoot, string projectDataRoot, int attemptNumber, CancellationToken cancellationToken)
	{
		string runId = Guid.NewGuid().ToString("N");
		string journalPath = Path.Combine(suiteRoot, $"case-{14:000}-attempt-{attemptNumber}.jsonl");
		string stage = "server-identity";
		string entityUuid = null;
		bool released = false;
		string verdict = "Inconclusive";
		string signature = "incomplete";
		string actual = "No complete server-authoritative outcome was obtained.";
		string limitation = "Attempt did not complete all required client/server observations.";
		string captureStatus = "unavailable: no anomaly capture requested";
		FabricBridgeClient bridge = null;
		try
		{
			_ = 21;
			try
			{
				bridge = FabricBridgeClient.FromDescriptor(Path.Combine(workspace.RootPath, "endermanqa-bridge.json"));
				FabricBridgeStatus serverStatus = await bridge.GetStatusAsync(cancellationToken);
				FabricClientBridgeStatus fabricClientBridgeStatus = await client.GetBridgeStatusAsync(cancellationToken);
				if (serverStatus.ServerPid != server.ProcessId || serverStatus.InstanceId != workspace.InstanceId || serverStatus.TargetId.Length == 0 || !serverStatus.WorldMutable || fabricClientBridgeStatus.InstanceId != workspace.InstanceId || fabricClientBridgeStatus.ProcessId != client.ClientProcessId || fabricClientBridgeStatus.GameVersion != "26.3" || !fabricClientBridgeStatus.ConnectedToServer || !fabricClientBridgeStatus.WorldAvailable)
				{
					throw new InvalidOperationException("Owned server/client identity, instance, version, or active-world status did not validate.");
				}
				await AppendLineAsync(journalPath, new
				{
					kind = "case-plan",
					runId = runId,
					suiteId = Path.GetFileName(suiteRoot),
					sourceId = 14,
					caseId = "enderman.death.idea-014",
					title = "순간이동 직후 공격이 이전 위치에 빗나가도 피해가 잘못 들어가지 않는가?",
					coverage = "client cached-reference + server-authoritative reconciliation",
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "spawn-fixture";
				FabricActionResponse fabricActionResponse = await bridge.ExecuteAllowlistedActionAsync("spawn_test_enderman", null, null, null, 1, null, cancellationToken);
				if (!Guid.TryParse(fabricActionResponse.EntityUuid, out var result) || !fabricActionResponse.EntityAdded || !fabricActionResponse.EndermanTypeMatches || fabricActionResponse.EntityType != "minecraft:enderman")
				{
					throw new InvalidDataException("Managed Enderman spawn receipt failed exact type/identity validation.");
				}
				entityUuid = result.ToString("D");
				await AppendLineAsync(journalPath, new
				{
					kind = "action",
					runId = runId,
					action = new
					{
						StepId = "spawn-managed-enderman",
						Action = fabricActionResponse.Action,
						Succeeded = fabricActionResponse.EntityAdded,
						Detail = JsonSerializer.Serialize(fabricActionResponse, JsonOptions)
					},
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "server-baseline";
				FabricEndermanObservation baseline = (await bridge.ObserveAsync(cancellationToken)).Endermen.SingleOrDefault((FabricEndermanObservation item) => item.Uuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Managed Enderman was absent from authoritative server observations.");
				if (baseline.LifeState != "alive" || baseline.Health <= 0f)
				{
					throw new InvalidDataException("Managed Enderman was not alive at baseline.");
				}
				await AppendLineAsync(journalPath, new
				{
					kind = "before",
					runId = runId,
					entity = baseline
				}, cancellationToken);
				stage = "client-observe-cache";
				FabricClientActionReceipt clientObserved = await client.ObserveEndermanAsync(baseline.EntityId, entityUuid, cancellationToken);
				if (!clientObserved.AcceptedByClient || clientObserved.Action != "observe_enderman" || !clientObserved.AfterClientTick.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase) || clientObserved.AfterClientTick.EntityId != baseline.EntityId)
				{
					throw new InvalidDataException("Client did not observe the exact server-managed Enderman.");
				}
				FabricClientActionReceipt fabricClientActionReceipt = await client.CacheEndermanTargetAsync(baseline.EntityId, entityUuid, cancellationToken);
				if (!fabricClientActionReceipt.AcceptedByClient || fabricClientActionReceipt.Action != "cache_enderman_target" || !fabricClientActionReceipt.AfterClientTick.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Client did not cache the exact target before relocation.");
				}
				await AppendLineAsync(journalPath, new
				{
					kind = "action",
					runId = runId,
					action = new
					{
						StepId = "client-cache-target",
						Action = fabricClientActionReceipt.Action,
						Succeeded = fabricClientActionReceipt.AcceptedByClient,
						Detail = JsonSerializer.Serialize(fabricClientActionReceipt, JsonOptions)
					},
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "server-relocate";
				FabricActionResponse relocation;
				try
				{
					relocation = await bridge.TeleportManagedEndermanForTc014Async(entityUuid, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException))
				{
					string value2;
					try
					{
						using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5.0));
						FabricObservationSnapshot obj = await bridge.ObserveAsync(timeout.Token);
						FabricEndermanObservation fabricEndermanObservation = obj.Endermen.SingleOrDefault((FabricEndermanObservation item) => item.Uuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase));
						FabricDeathFact[] value = obj.Deaths.Where((FabricDeathFact item) => item.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase)).ToArray();
						value2 = (((object)fabricEndermanObservation == null) ? ("entity=absent; matchingDeaths=" + JsonSerializer.Serialize(value, JsonOptions)) : ("entity=" + JsonSerializer.Serialize(fabricEndermanObservation, JsonOptions) + "; matchingDeaths=" + JsonSerializer.Serialize(value, JsonOptions)));
					}
					catch (Exception ex2) when (!(ex2 is OutOfMemoryException) && !(ex2 is StackOverflowException))
					{
						value2 = "reconciliation-read-failed=" + ex2.GetType().Name + ": " + ex2.Message;
					}
					string diagnostic = $"Relocation receipt was unavailable ({ex.GetType().Name}: {ex.Message}); no relocation retry or client attack was sent. {value2}";
					await AppendLineAsync(journalPath, new
					{
						kind = "reconciliation-after-uncertain-relocation",
						runId = runId,
						entityUuid = entityUuid,
						diagnostic = diagnostic,
						timestamp = DateTimeOffset.UtcNow
					}, CancellationToken.None);
					throw new InvalidOperationException(diagnostic, ex);
				}
				if ((object)relocation.From == null || Math.Abs(relocation.From.X - clientObserved.AfterClientTick.X) > 1.0 || Math.Abs(relocation.From.Y - clientObserved.AfterClientTick.Y) > 1.0 || Math.Abs(relocation.From.Z - clientObserved.AfterClientTick.Z) > 1.0)
				{
					throw new InvalidDataException("Client cached a position that did not match the server relocation origin; fixture setup is inconclusive.");
				}
				await AppendLineAsync(journalPath, new
				{
					kind = "action",
					runId = runId,
					action = new
					{
						StepId = "server-relocate-target",
						Action = relocation.Action,
						Succeeded = relocation.Verified.GetValueOrDefault(),
						Detail = JsonSerializer.Serialize(relocation, JsonOptions)
					},
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "client-attack-once";
				FabricClientActionReceipt attack = null;
				string attackOutcome = null;
				try
				{
					attack = await client.AttackCachedEndermanAsync(baseline.EntityId, entityUuid, cancellationToken);
				}
				catch (Exception ex3) when (!(ex3 is OutOfMemoryException) && !(ex3 is StackOverflowException))
				{
					attackOutcome = "client request outcome uncertain; never resent; reconciling server by entity UUID";
					limitation = ex3.GetType().Name + ": " + ex3.Message;
					await AppendLineAsync(journalPath, new
					{
						kind = "action",
						runId = runId,
						action = new
						{
							StepId = "client-attack-cached-target-once",
							Action = "attack_cached_enderman",
							Succeeded = false,
							Detail = "Outcome uncertain; no retry. " + limitation
						},
						timestamp = DateTimeOffset.UtcNow
					}, CancellationToken.None);
				}
				if ((object)attack != null)
				{
					if (!attack.AcceptedByClient || attack.Action != "attack_cached_enderman" || (object)attack.CachedTargetBeforeServerMove == null || !attack.CachedTargetBeforeServerMove.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase) || attack.CachedTargetBeforeServerMove.EntityId != baseline.EntityId || attack.CachedTargetBeforeServerMove.Dimension != baseline.Dimension || Math.Abs(attack.CachedTargetBeforeServerMove.X - clientObserved.AfterClientTick.X) > 0.5 || Math.Abs(attack.CachedTargetBeforeServerMove.Y - clientObserved.AfterClientTick.Y) > 0.5 || Math.Abs(attack.CachedTargetBeforeServerMove.Z - clientObserved.AfterClientTick.Z) > 0.5 || !attack.Before.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidDataException("Attack receipt did not prove one accepted attack using the pre-relocation cached target.");
					}
					await AppendLineAsync(journalPath, new
					{
						kind = "action",
						runId = runId,
						action = new
						{
							StepId = "client-attack-cached-target-once",
							Action = attack.Action,
							Succeeded = true,
							Detail = JsonSerializer.Serialize(attack, JsonOptions)
						},
						timestamp = DateTimeOffset.UtcNow
					}, CancellationToken.None);
				}
				stage = "server-reconcile";
				FabricEndermanObservation final = null;
				FabricObservationSnapshot finalSnapshot = null;
				using CancellationTokenSource reconciliationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8.0));
				using CancellationTokenSource cancellationTokenSource = (cancellationToken.IsCancellationRequested ? reconciliationTimeout : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, reconciliationTimeout.Token));
				for (int i = 0; i < 30; i++)
				{
					finalSnapshot = await bridge.ObserveAsync(cancellationTokenSource.Token);
					final = finalSnapshot.Endermen.SingleOrDefault((FabricEndermanObservation item) => item.Uuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase));
					if (((object)final != null && final.GameTick >= relocation.ServerTick + 2) || finalSnapshot.Deaths.Any((FabricDeathFact item) => item.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase) && item.GameTick >= relocation.ServerTick))
					{
						break;
					}
					await Task.Delay(100, cancellationTokenSource.Token);
				}
				if ((object)finalSnapshot == null)
				{
					throw new InvalidDataException("Server did not return a reconciliation snapshot.");
				}
				FabricDeathFact[] matchingDeaths = finalSnapshot.Deaths.Where((FabricDeathFact item) => item.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase) && item.GameTick >= relocation.ServerTick).ToArray();
				await AppendLineAsync(journalPath, new
				{
					kind = "after",
					runId = runId,
					entity = final,
					serverDeaths = matchingDeaths
				}, CancellationToken.None);
				if ((object)final == null)
				{
					if (matchingDeaths.Length == 0)
					{
						throw new InvalidDataException("Server did not provide a bounded post-attack entity or death-event reconciliation.");
					}
					verdict = "Inconclusive";
					signature = $"entity-absent=true;death={matchingDeaths[0].Cause};attackAccepted={attack?.AcceptedByClient ?? false}";
					actual = "The exact managed UUID disappeared and a server death event was recorded (cause=" + matchingDeaths[0].Cause + "); final health/damage-event delta is unavailable, so defect attribution remains unconfirmed.";
					limitation = "Identity-correlated death evidence exists, but the entity snapshot was removed before final health/damage reconciliation; do not call this a confirmed attack defect.";
				}
				else
				{
					if (final.GameTick < relocation.ServerTick + 2)
					{
						throw new InvalidDataException("Server reconciliation window did not advance two ticks past relocation.");
					}
					int num = (final.DamageEvents?.Count ?? 0) - (baseline.DamageEvents?.Count ?? 0);
					bool flag = final.LifeState != "alive" || final.Health <= 0f || matchingDeaths.Length != 0;
					float num2 = baseline.Health - final.Health;
					signature = $"damage={num};healthLoss={num2:0.###};died={flag};attackAccepted={attack?.AcceptedByClient ?? false}";
					if (cancellationToken.IsCancellationRequested)
					{
						verdict = "Inconclusive";
						actual = $"Run was stopped after attack dispatch; bounded server reconciliation completed: health {baseline.Health}->{final.Health}, damage-event delta {num}, died={flag}.";
						limitation = "User stop requested after the one-shot client action; no attack retry was sent.";
					}
					else if ((object)attack == null || attackOutcome != null)
					{
						verdict = "Inconclusive";
						actual = $"Attack outcome could not be confirmed; no retry was sent. Server health {baseline.Health}->{final.Health}, damage-event delta {num}, died={flag}.";
						limitation = attackOutcome ?? limitation;
					}
					else if (num > 0 || num2 > 0.001f || flag)
					{
						verdict = "Fail";
						actual = $"Unexpected server-side damage after relocated cached-target attack: health {baseline.Health}->{final.Health}, damage-event delta {num}, died={flag}.";
						limitation = "Complete identity-correlated server receipt; repeat this anomaly with fresh managed fixtures before confirming a product defect.";
					}
					else
					{
						verdict = "Pass";
						actual = $"Expected behavior observed: relocation >=8 blocks verified; one cached-reference vanilla attack accepted; client had received relocation before attack={attack?.TargetPositionChangedSinceCache.GetValueOrDefault() ?? false}; server health {baseline.Health}->{final.Health}, damage-event delta {num}, alive={!flag}.";
						limitation = "Controlled cached-target test only; not a natural aim, latency, or general combat test.";
					}
				}
				if (verdict == "Fail")
				{
					try
					{
						OwnedJavaClientProcess.CaptureEvidence frame = await client.CaptureFramebufferAsync(cancellationToken);
						ClientFramebufferEvidenceStore.StoredEvidence storedEvidence = await new ClientFramebufferEvidenceStore().StoreAsync(instancesRoot, projectDataRoot, client.SessionRoot, frame, "#14");
						captureStatus = $"native-framebuffer:{storedEvidence.ProjectRelativeFolder}/{Path.GetFileName(frame.PngPath)} sha256={storedEvidence.Sha256}";
					}
					catch (Exception ex4) when (!(ex4 is OutOfMemoryException) && !(ex4 is StackOverflowException))
					{
						captureStatus = "unavailable: " + ex4.GetType().Name + "; " + ex4.Message;
					}
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				limitation = "Stopped at stage " + stage + "; any possibly-sent client attack was not retried. Server reconciliation/fixture cleanup is required.";
				actual = "Run cancelled before a complete verdict.";
			}
			catch (Exception ex6) when (!(ex6 is OutOfMemoryException) && !(ex6 is StackOverflowException))
			{
				limitation = $"{stage}: {ex6.GetType().Name}: {ex6.Message}";
				actual = "Attempt did not meet complete evidence criteria; classified Inconclusive.";
			}
		}
		finally
		{
			if (entityUuid != null && bridge != null)
			{
				try
				{
					using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(8.0));
					FabricActionResponse fabricActionResponse2 = await bridge.ExecuteAllowlistedActionAsync("release_test_fixture", entityUuid, null, null, 1, null, cancellationTokenSource.Token);
					released = fabricActionResponse2.Action == "release_test_fixture" && fabricActionResponse2.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase);
				}
				catch (Exception ex7) when (!(ex7 is OutOfMemoryException) && !(ex7 is StackOverflowException))
				{
					limitation = limitation + " Fixture cleanup needs reconciliation (" + ex7.GetType().Name + ").";
				}
			}
			bridge?.Dispose();
		}
		if (!released && entityUuid != null)
		{
			verdict = "Inconclusive";
			limitation += " Managed fixture cleanup was not verified.";
		}
		await AppendLineAsync(journalPath, new
		{
			kind = "attempt-verdict",
			runId = runId,
			verdict = verdict,
			signature = signature,
			actualObservation = actual,
			limitation = limitation,
			captureStatus = captureStatus,
			fixtureReleased = released,
			recordedAt = DateTimeOffset.UtcNow
		}, CancellationToken.None);
		return new AttemptOutcome(runId, verdict, signature, actual, limitation, captureStatus, released, journalPath);
	}

	private static void ValidateOwnership(JavaServerWorkspace workspace, OwnedJavaServerProcess server, OwnedJavaClientProcess client, string instancesRoot)
	{
		string fullPath = WorkbenchInstancePaths.ResolveInstanceRoot(workspace.InstanceId, workspace.RootPath, instancesRoot);
		string fullPath2 = Path.GetFullPath(client.SessionRoot);
		if (!Path.GetFullPath(workspace.WorldPath).Equals(Path.Combine(fullPath, "qa-world"), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(workspace.WorldPath) || server.HasExited || server.ProcessId <= 0 || client.HasExited || !Path.GetFullPath(server.Workspace.RootPath).Equals(fullPath, StringComparison.OrdinalIgnoreCase) || !WorkbenchInstancePaths.IsOwnedClientSession(fullPath, fullPath2))
		{
			throw new InvalidOperationException("TC-014 requires the exact running Workbench server and lease-bound client for its existing qa-world.");
		}
	}

	private static async Task AppendLineAsync(string path, object entry, CancellationToken cancellationToken)
	{
		string contents = JsonSerializer.Serialize(entry, JsonOptions) + "\n";
		await File.AppendAllTextAsync(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
	}
}
