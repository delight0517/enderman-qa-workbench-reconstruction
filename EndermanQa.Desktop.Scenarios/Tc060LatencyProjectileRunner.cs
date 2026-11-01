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

internal static class Tc060LatencyProjectileRunner
{
	private const int SourceId = 60;

	private const string CaseId = "enderman.death.idea-060";

	private const string Title = "서버 지연 중 투사체가 맞고 순간이동해도 죽음과 드롭이 중복되지 않는가?";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	public static async Task<SuiteCaseReceipt> RunAsync(JavaServerWorkspace workspace, OwnedJavaServerProcess server, OwnedJavaClientProcess client, OwnedLoopbackLatencyProxy proxy, string instancesRoot, string projectDataRoot, CancellationToken cancellationToken, int repeatDepth = 0, bool persistReceipt = true)
	{
		ValidateOwnership(workspace, server, client, proxy, instancesRoot);
		string suiteId = Guid.NewGuid().ToString("N");
		string suiteRoot = Path.Combine(Path.GetFullPath(projectDataRoot), "suite-runs", suiteId);
		Directory.CreateDirectory(suiteRoot);
		string runId = Guid.NewGuid().ToString("N");
		string journalPath = Path.Combine(suiteRoot, "case-060-attempt-1.jsonl");
		Guid result = Guid.NewGuid();
		string arrowSessionId = result.ToString("D");
		string entityUuid = null;
		string ownerUuid = null;
		bool fixtureReleased = false;
		bool observerMayExist = false;
		bool observerStopAttempted = false;
		string verdict = "Inconclusive";
		string limitation = "A full simultaneous projectile volley and causal teleport/death/drop attribution are not supported by this attempt.";
		string signature = "incomplete";
		string actual = "No complete authoritative outcome was obtained.";
		string captureStatus = "unavailable: no reproduced failure";
		FabricClientArrowSessionSnapshot arrowSession = null;
		FabricBridgeClient bridge = null;
		Array.Empty<FabricDeathArtifactObservation>();
		List<OwnedJavaClientProcess.ClientBowAttemptReceipt> bowReceipts = new List<OwnedJavaClientProcess.ClientBowAttemptReceipt>();
		string stage = "identity";
		await AppendAsync(journalPath, new
		{
			kind = "case-plan",
			runId = runId,
			suiteId = suiteId,
			SourceId = 60,
			CaseId = "enderman.death.idea-060",
			Title = "서버 지연 중 투사체가 맞고 순간이동해도 죽음과 드롭이 중복되지 않는가?",
			coverage = "real client vanilla bow actions through existing per-read TCP delay proxy; serial shots, not a simultaneous volley",
			arrowSessionId = arrowSessionId,
			requestIdPersistedBeforeCreate = true,
			timestamp = DateTimeOffset.UtcNow
		}, CancellationToken.None);
		try
		{
			
			try
			{
				bridge = FabricBridgeClient.FromDescriptor(Path.Combine(workspace.RootPath, "endermanqa-bridge.json"));
				FabricBridgeStatus serverStatus = await bridge.GetStatusAsync(cancellationToken);
				FabricClientBridgeStatus fabricClientBridgeStatus = await client.GetBridgeStatusAsync(cancellationToken);
				if (serverStatus.ServerPid != server.ProcessId || serverStatus.InstanceId != workspace.InstanceId || string.IsNullOrWhiteSpace(serverStatus.TargetId) || !serverStatus.WorldMutable || fabricClientBridgeStatus.InstanceId != workspace.InstanceId || fabricClientBridgeStatus.ProcessId != client.ClientProcessId || fabricClientBridgeStatus.GameVersion != "26.3" || !fabricClientBridgeStatus.ConnectedToServer || !fabricClientBridgeStatus.WorldAvailable || !fabricClientBridgeStatus.PlayerAvailable || !Guid.TryParse(fabricClientBridgeStatus.PlayerUuid, out result) || fabricClientBridgeStatus.MainHandItem != "minecraft:bow" || !fabricClientBridgeStatus.HasNormalArrowAmmo || !proxy.Matches(server) || !proxy.ClientAccepted || !proxy.ConnectedToServer || proxy.ClientToServerDelayMilliseconds + proxy.ServerToClientDelayMilliseconds <= 0)
				{
					throw new InvalidOperationException("TC-060 preflight failed: exact owned world/client, connected delayed proxy, player UUID, held bow and normal arrow ammo are required.");
				}
				ownerUuid = Guid.Parse(fabricClientBridgeStatus.PlayerUuid).ToString("D");
				await AppendAsync(journalPath, new
				{
					kind = "preflight",
					runId = runId,
					serverPid = server.ProcessId,
					clientPid = client.ClientProcessId,
					instanceId = workspace.InstanceId,
					gameVersion = fabricClientBridgeStatus.GameVersion,
					ownerUuid = ownerUuid,
					mainHand = fabricClientBridgeStatus.MainHandItem,
					normalArrowAmmo = fabricClientBridgeStatus.HasNormalArrowAmmo,
					delaySemantics = "per TCP stream read; not packet RTT",
					c2sMs = proxy.ClientToServerDelayMilliseconds,
					s2cMs = proxy.ServerToClientDelayMilliseconds,
					proxyConnected = proxy.ConnectedToServer,
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "prepare-existing-qa-world";
				FabricActionResponse fabricActionResponse = await bridge.ExecuteAllowlistedActionAsync("prepare_qa_suite", null, null, null, 1, null, cancellationToken);
				if (fabricActionResponse.Action != "prepare_qa_suite" || !fabricActionResponse.NormalizedQaCage.GetValueOrDefault() || !fabricActionResponse.WeatherCleared.GetValueOrDefault() || !fabricActionResponse.SpawnMobsDisabled.GetValueOrDefault())
				{
					throw new InvalidDataException("Existing QA world setup was not fully acknowledged; no case action was sent.");
				}
				await AppendAsync(journalPath, new
				{
					kind = "world-preparation",
					runId = runId,
					action = fabricActionResponse,
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "spawn-managed-fixture";
				FabricActionResponse fabricActionResponse2 = await bridge.ExecuteAllowlistedActionAsync("spawn_test_enderman", null, null, null, 1, null, cancellationToken);
				if (!Guid.TryParse(fabricActionResponse2.EntityUuid, out var result2) || !fabricActionResponse2.EntityAdded || !fabricActionResponse2.EndermanTypeMatches || fabricActionResponse2.EntityType != "minecraft:enderman")
				{
					throw new InvalidDataException("Managed Enderman spawn identity was not verified.");
				}
				entityUuid = result2.ToString("D");
				await AppendAsync(journalPath, new
				{
					kind = "action",
					runId = runId,
					step = "spawn-managed-enderman",
					action = fabricActionResponse2,
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "configure-bounded-health";
				FabricActionResponse fabricActionResponse3 = await bridge.ExecuteAllowlistedActionAsync("configure_hazard", entityUuid, "health-6", null, 1, null, cancellationToken);
				bool flag = fabricActionResponse3.Action != "configure_hazard" || !fabricActionResponse3.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase);
				if (!flag)
				{
					float? afterHealth = fabricActionResponse3.AfterHealth;
					bool flag2 = ((!afterHealth.HasValue || afterHealth.GetValueOrDefault() <= 0f) ? true : false);
					flag = flag2;
				}
				if (flag)
				{
					throw new InvalidDataException("Bounded health fixture was not confirmed; stop before client input.");
				}
				await AppendAsync(journalPath, new
				{
					kind = "action",
					runId = runId,
					step = "configure-health-6",
					action = fabricActionResponse3,
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "baseline";
				FabricObservationSnapshot beforeSnapshot = await bridge.ObserveAsync(cancellationToken);
				FabricEndermanObservation baseline = beforeSnapshot.Endermen.SingleOrDefault((FabricEndermanObservation item) => item.Uuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Exact managed Enderman was absent from server baseline.");
				if (baseline.LifeState != "alive" || baseline.Health <= 0f)
				{
					throw new InvalidDataException("Managed Enderman was not alive before the attempt.");
				}
				IReadOnlyList<FabricDeathArtifactObservation> artifactBaseline = await bridge.GetDeathArtifactsAsync(entityUuid, cancellationToken);
				await AppendAsync(journalPath, new
				{
					kind = "before",
					runId = runId,
					target = baseline,
					deathEventsForTarget = beforeSnapshot.Deaths.Count((FabricDeathFact item) => item.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase)),
					artifacts = artifactBaseline,
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				stage = "begin-arrow-observer";
				observerMayExist = true;
				try
				{
					arrowSession = await bridge.BeginClientArrowSessionAsync(arrowSessionId, ownerUuid, entityUuid, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException))
				{
					try
					{
						arrowSession = await bridge.GetClientArrowSessionAsync(arrowSessionId, ownerUuid, entityUuid, CancellationToken.None);
					}
					catch (Exception ex2) when (!(ex2 is OutOfMemoryException) && !(ex2 is StackOverflowException))
					{
						limitation = $"Observer start acknowledgement uncertain; same pre-recorded ID lookup failed ({ex2.GetType().Name}); create was not repeated. Original: {ex.GetType().Name}.";
						throw new InvalidDataException(limitation, ex);
					}
				}
				if (arrowSession.State != "active")
				{
					throw new InvalidDataException("Arrow observer did not become active; no bow action was sent.");
				}
				await AppendAsync(journalPath, new
				{
					kind = "arrow-observer-started",
					runId = runId,
					session = arrowSession,
					timestamp = DateTimeOffset.UtcNow
				}, CancellationToken.None);
				for (int i = 1; i <= 3; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					FabricObservationSnapshot fabricObservationSnapshot = await bridge.ObserveAsync(cancellationToken);
					FabricEndermanObservation fabricEndermanObservation = fabricObservationSnapshot.Endermen.SingleOrDefault((FabricEndermanObservation item) => item.Uuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase));
					if ((object)fabricEndermanObservation == null || fabricEndermanObservation.LifeState != "alive" || fabricEndermanObservation.Health <= 0f || fabricObservationSnapshot.Deaths.Any((FabricDeathFact item) => item.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase)))
					{
						break;
					}
					stage = $"client-bow-shot-{i}";
					OwnedJavaClientProcess.ClientBowAttemptReceipt bow = await client.FireBowAtEndermanAsync(fabricEndermanObservation.EntityId, entityUuid, 20, cancellationToken);
					bowReceipts.Add(bow);
					await AppendAsync(journalPath, new
					{
						kind = "client-bow-attempt",
						runId = runId,
						shot = i,
						action = bow,
						note = "one vanilla client action; any uncertain result is not resent",
						timestamp = DateTimeOffset.UtcNow
					}, CancellationToken.None);
					if (bow.Release?.Action != "release_main_hand" || !bow.Release.AcceptedByClient || !bow.RequestedDrawDurationReached)
					{
						break;
					}
					await Task.Delay(500, cancellationToken);
				}
				stage = "stop-arrow-observer";
				observerStopAttempted = true;
				try
				{
					arrowSession = await bridge.EndClientArrowSessionAsync(arrowSessionId, ownerUuid, entityUuid, CancellationToken.None);
				}
				catch (Exception ex3) when (!(ex3 is OutOfMemoryException) && !(ex3 is StackOverflowException))
				{
					try
					{
						arrowSession = await bridge.GetClientArrowSessionAsync(arrowSessionId, ownerUuid, entityUuid, CancellationToken.None);
					}
					catch (Exception ex4) when (!(ex4 is OutOfMemoryException) && !(ex4 is StackOverflowException))
					{
						limitation = $"Observer stop state uncertain; same-ID read failed ({ex4.GetType().Name}); DELETE was not repeated. Stop: {ex3.GetType().Name}.";
					}
				}
				flag = (object)arrowSession == null;
				if (!flag)
				{
					bool flag2;
					switch (arrowSession.State)
					{
					case "active":
					case "stop_pending":
					case "start_pending":
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					flag = flag2;
				}
				observerMayExist = flag;
				await AppendAsync(journalPath, new
				{
					kind = "arrow-observer-final",
					runId = runId,
					session = arrowSession,
					stateMayRemainActive = observerMayExist,
					timestamp = DateTimeOffset.UtcNow
				}, CancellationToken.None);
				stage = "server-reconcile";
				FabricObservationSnapshot fabricObservationSnapshot2;
				using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8.0)))
				{
					fabricObservationSnapshot2 = await bridge.ObserveAsync(timeout.Token);
				}
				FabricEndermanObservation finalTarget = fabricObservationSnapshot2.Endermen.SingleOrDefault((FabricEndermanObservation item) => item.Uuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase));
				FabricDeathFact[] matchingDeaths = fabricObservationSnapshot2.Deaths.Where((FabricDeathFact item) => item.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase)).ToArray();
				IReadOnlyList<FabricDeathArtifactObservation> readOnlyList = await bridge.GetDeathArtifactsAsync(entityUuid, CancellationToken.None);
				HashSet<string> beforeArtifactIds = artifactBaseline.Select((FabricDeathArtifactObservation item) => item.ArtifactUuid).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
				FabricDeathArtifactObservation[] array = readOnlyList.Where((FabricDeathArtifactObservation item) => !beforeArtifactIds.Contains(item.ArtifactUuid)).ToArray();
				IReadOnlyList<FabricClientArrowObservation> arrows = arrowSession?.Projectiles ?? Array.Empty<FabricClientArrowObservation>();
				FabricDamageFact[] array2 = finalTarget?.DamageEvents?.Where((FabricDamageFact item) => arrows.Any((FabricClientArrowObservation arrow) => arrow.ProjectileUuid.Equals(item.AttackerUuid, StringComparison.OrdinalIgnoreCase))).ToArray() ?? Array.Empty<FabricDamageFact>();
				IReadOnlyList<FabricPositionSample> readOnlyList2 = finalTarget?.PositionTrace ?? Array.Empty<FabricPositionSample>();
				bool flag3 = readOnlyList2.Zip(readOnlyList2.Skip(1), (FabricPositionSample left, FabricPositionSample right) => left.Dimension != right.Dimension || Distance(left.X, left.Y, left.Z, right.X, right.Y, right.Z) >= 8.0).Any((bool value) => value);
				int num2 = matchingDeaths.Length;
				bool value2 = (object)finalTarget != null && finalTarget.LifeState == "alive" && finalTarget.Health > 0f && num2 == 0;
				signature = $"arrows={arrows.Count};correlatedDamage={array2.Length};teleportTrace={flag3};deathEvents={num2};newArtifactIds={array.Select((FabricDeathArtifactObservation item) => item.ArtifactUuid).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count()};observerClosed={arrowSession?.State == "closed"}";
				actual = $"Observed {arrows.Count} player-owned arrow(s); arrow-UUID-correlated damage={array2.Length}; teleport trace={flag3}; exact target death events={num2}; new artifact UUIDs={array.Length}; target alive={value2}.";
				limitation = "Partial coverage: serial vanilla bow actions under configured per-read TCP stream delay. This does not simulate a simultaneous volley or measured packet RTT. Server arrow samples show trajectories, not hit/teleport causality unless attacker UUID matches. Death and item artifacts are recorded separately; item count semantics are not treated as duplicate-drop proof.";
				if (observerMayExist)
				{
					limitation += " Arrow observer closure could not be confirmed; reconcile its recorded session ID before another attempt.";
				}
				if ((arrowSession?.Overflow ?? false) || arrows.Any((FabricClientArrowObservation item) => item.SampleOverflow))
				{
					limitation += " Telemetry sample overflow occurred.";
				}
				if (num2 > 1)
				{
					verdict = "Fail";
					limitation += " Multiple server death facts for the same managed UUID were observed in one bounded attempt; repeatability is unconfirmed.";
				}
				else
				{
					verdict = "Inconclusive";
					limitation += " Even if the hit/death chain is observed, this adapter cannot establish drop duplication semantics or a simultaneous volley, so the formal case remains Inconclusive.";
				}
				await AppendAsync(journalPath, new
				{
					kind = "after",
					runId = runId,
					target = finalTarget,
					deaths = matchingDeaths,
					arrowSession = arrowSession,
					correlatedDamage = array2,
					teleportTraceObserved = flag3,
					deathArtifactsBefore = artifactBaseline,
					deathArtifactsAfter = readOnlyList,
					newDeathArtifacts = array,
					timestamp = DateTimeOffset.UtcNow
				}, CancellationToken.None);
				if (verdict == "Fail")
				{
					try
					{
						OwnedJavaClientProcess.CaptureEvidence frame = await client.CaptureFramebufferAsync(CancellationToken.None);
						ClientFramebufferEvidenceStore.StoredEvidence storedEvidence = await new ClientFramebufferEvidenceStore().StoreAsync(instancesRoot, projectDataRoot, client.SessionRoot, frame, "#60");
						captureStatus = $"native-framebuffer:{storedEvidence.ProjectRelativeFolder}/{Path.GetFileName(frame.PngPath)} sha256={storedEvidence.Sha256}";
					}
					catch (Exception ex5) when (!(ex5 is OutOfMemoryException) && !(ex5 is StackOverflowException))
					{
						captureStatus = "unavailable: " + ex5.GetType().Name + "; " + ex5.Message;
					}
				}
			}
			catch (OperationCanceledException)
			{
				limitation = "Stopped at " + stage + "; any uncertain bow/observer action was not repeated. Reconcile journal and fixture/session state.";
				actual = "Run stopped before complete server-authoritative reconciliation.";
				verdict = "Inconclusive";
			}
			catch (Exception ex7) when (!(ex7 is OutOfMemoryException) && !(ex7 is StackOverflowException))
			{
				limitation = $"{stage}: {ex7.GetType().Name}: {ex7.Message}";
				actual = "Attempt did not meet complete evidence criteria; classified Inconclusive.";
				verdict = "Inconclusive";
			}
		}
		finally
		{
			if (observerMayExist && bridge != null && ownerUuid != null && entityUuid != null)
			{
				try
				{
					arrowSession = await bridge.GetClientArrowSessionAsync(arrowSessionId, ownerUuid, entityUuid, CancellationToken.None);
				}
				catch (Exception ex8) when (!(ex8 is OutOfMemoryException) && !(ex8 is StackOverflowException))
				{
					limitation = limitation + " Observer same-ID reconciliation unavailable (" + ex8.GetType().Name + ").";
				}
				bool flag = !observerStopAttempted;
				if (flag)
				{
					bool flag2;
					switch (arrowSession?.State)
					{
					case "active":
					case "stop_pending":
					case "start_pending":
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					flag = flag2;
				}
				if (flag)
				{
					try
					{
						arrowSession = await bridge.EndClientArrowSessionAsync(arrowSessionId, ownerUuid, entityUuid, CancellationToken.None);
					}
					catch (Exception ex9) when (!(ex9 is OutOfMemoryException) && !(ex9 is StackOverflowException))
					{
						limitation = limitation + " Observer cleanup uncertain (" + ex9.GetType().Name + "); do not repeat create/delete.";
					}
				}
			}
			if (entityUuid != null && bridge != null)
			{
				try
				{
					FabricActionResponse fabricActionResponse4 = await bridge.ExecuteAllowlistedActionAsync("release_test_fixture", entityUuid, null, null, 1, null, CancellationToken.None);
					fixtureReleased = fabricActionResponse4.Action == "release_test_fixture" && fabricActionResponse4.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase);
				}
				catch (Exception ex10) when (!(ex10 is OutOfMemoryException) && !(ex10 is StackOverflowException))
				{
					limitation = limitation + " Managed fixture cleanup requires reconciliation (" + ex10.GetType().Name + ").";
				}
			}
			bridge?.Dispose();
		}
		if (!fixtureReleased && entityUuid != null)
		{
			verdict = "Inconclusive";
			limitation += " Fixture release was not verified.";
		}
		List<string> repeatRunIds = new List<string>();
		string repeatability = ((verdict == "Fail") ? "anomaly-unconfirmed; same-world repeats pending" : "not-confirmed; no reproducible defect signature");
		if (verdict == "Fail" && !cancellationToken.IsCancellationRequested && repeatDepth < 2)
		{
			try
			{
				SuiteCaseReceipt repeat = await RunAsync(workspace, server, client, proxy, instancesRoot, projectDataRoot, cancellationToken, repeatDepth + 1, persistReceipt: false);
				await AppendAsync(journalPath, new
				{
					kind = "repeat-comparison",
					firstRunId = runId,
					repeatFirstRunId = repeat.FirstRunId,
					repeatRunIds = repeat.RepeatRunIds,
					repeatJournalPath = repeat.JournalPath,
					firstSignature = signature,
					repeatSignature = repeat.Signature,
					repeatVerdict = repeat.Verdict,
					timestamp = DateTimeOffset.UtcNow
				}, CancellationToken.None);
				if (!string.IsNullOrWhiteSpace(repeat.FirstRunId))
				{
					repeatRunIds.Add(repeat.FirstRunId);
				}
				repeatRunIds.AddRange(repeat.RepeatRunIds);
				if (repeat.Verdict == "Fail" && string.Equals(repeat.Signature, signature, StringComparison.Ordinal))
				{
					verdict = "Fail";
					if (repeatDepth == 0)
					{
						repeatability = "confirmed-repeatable-same-signature-across-three-fresh-fixtures-in-the-existing-world";
						limitation += " The same duplicate-death signature repeated across three fresh managed fixtures in this same qa-world.";
					}
					else
					{
						repeatability = "two-matching-attempts; top-level confirmation still pending";
						limitation += " Two attempts have the same signature; this nested receipt does not independently confirm a defect.";
					}
				}
				else
				{
					repeatability = "not-confirmed; same-signature anomaly did not repeat across fresh fixtures";
					verdict = "Inconclusive";
					limitation += " Suspicious first result was repeated in the same qa-world with fresh managed fixtures; matching signature was not confirmed.";
				}
			}
			catch (Exception ex11) when (!(ex11 is OutOfMemoryException) && !(ex11 is StackOverflowException))
			{
				verdict = "Inconclusive";
				repeatability = "not-confirmed; repeat attempt could not complete";
				limitation = limitation + " Repeat attempt did not complete (" + ex11.GetType().Name + "); first anomaly remains unconfirmed.";
			}
		}
		else if (verdict == "Fail")
		{
			if (cancellationToken.IsCancellationRequested)
			{
				verdict = "Inconclusive";
				repeatability = "not-confirmed; operator stopped before repeat";
				limitation += " Anomaly was not repeat-confirmed.";
			}
			else
			{
				repeatability = "anomaly-unconfirmed; final bounded repeat awaiting its parent comparison";
				limitation += " This bounded repeat is retained for comparison by the parent run; it is not independently confirmed.";
			}
		}
		SuiteCaseReceipt receipt = new SuiteCaseReceipt(60, "enderman.death.idea-060", "서버 지연 중 투사체가 맞고 순간이동해도 죽음과 드롭이 중복되지 않는가?", (verdict == "Fail" && repeatability.StartsWith("confirmed-repeatable", StringComparison.Ordinal)) ? "failure-reproduced" : ((verdict == "Fail" || (verdict == "Inconclusive" && repeatRunIds.Count > 0)) ? "anomaly-unconfirmed" : "executed-partial"), verdict, "partial: owned client serial bow actions through delayed TCP proxy; server arrow UUID samples, damage/death, position trace and artifact UUID comparison", runId, repeatRunIds, repeatability, signature, limitation, captureStatus, journalPath);
		await AppendAsync(journalPath, new
		{
			kind = "attempt-verdict",
			runId = runId,
			verdict = verdict,
			signature = signature,
			actualObservation = actual,
			limitation = limitation,
			captureStatus = captureStatus,
			fixtureReleased = fixtureReleased,
			observerState = arrowSession?.State,
			repeatability = repeatability,
			repeatRunIds = repeatRunIds,
			finishedAt = DateTimeOffset.UtcNow
		}, CancellationToken.None);
		if (persistReceipt)
		{
			await File.WriteAllTextAsync(Path.Combine(suiteRoot, "results.json"), JsonSerializer.Serialize(new SuiteCaseReceipt[1] { receipt }, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), CancellationToken.None);
		}
		await File.WriteAllTextAsync(Path.Combine(suiteRoot, "expected-actions-selected.json"), JsonSerializer.Serialize(new[]
		{
			new
			{
				SourceId = 60,
				CaseId = "enderman.death.idea-060",
				Title = "서버 지연 중 투사체가 맞고 순간이동해도 죽음과 드롭이 중복되지 않는가?",
				Preconditions = "Existing owned qa-world; exact owned server and lease-bound client; connected delayed TCP proxy; client bow and normal arrows.",
				ExpectedActions = new string[7] { "record observer session UUID", "spawn managed Enderman", "record baseline artifacts", "observe client-owned arrows", "fire at most three serial vanilla shots", "reconcile exact UUID, damage/death/position/drop artifacts", "release fixture" },
				ExpectedObservation = "One exact target death after an arrow-correlated hit and teleport trace, with a closed observer session and unique artifact identities.",
				PassCriteria = "Do not claim a full pass: serial shots do not exercise a volley and item artifact observations do not determine duplicate-drop semantics. A duplicate-death anomaly requires three same-signature fixtures before failure confirmation.",
				PlanStatus = "executed",
				Coverage = "partial; not a simultaneous volley or RTT test"
			}
		}, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), CancellationToken.None);
		await AppendAsync(Path.Combine(suiteRoot, "suite.jsonl"), new
		{
			kind = "suite-finished",
			suiteId = suiteId,
			total = 1,
			executed = 1,
			verdict = verdict,
			repeatDepth = repeatDepth,
			repeatability = repeatability,
			repeatRunIds = repeatRunIds,
			repeatReceiptsPersistedInChildRuns = false,
			finishedAt = DateTimeOffset.UtcNow
		}, CancellationToken.None);
		return receipt;
	}

	private static void ValidateOwnership(JavaServerWorkspace workspace, OwnedJavaServerProcess server, OwnedJavaClientProcess client, OwnedLoopbackLatencyProxy proxy, string instancesRoot)
	{
		string fullPath = WorkbenchInstancePaths.ResolveInstanceRoot(workspace.InstanceId, workspace.RootPath, instancesRoot);
		string fullPath2 = Path.GetFullPath(client.SessionRoot);
		if (!Path.GetFullPath(workspace.WorldPath).Equals(Path.Combine(fullPath, "qa-world"), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(workspace.WorldPath) || server.HasExited || client.HasExited || !proxy.Matches(server) || !Path.GetFullPath(server.Workspace.RootPath).Equals(fullPath, StringComparison.OrdinalIgnoreCase) || !WorkbenchInstancePaths.IsOwnedClientSession(fullPath, fullPath2))
		{
			throw new InvalidOperationException("TC-060 requires the exact running app-owned server/client/proxy and existing qa-world.");
		}
	}

	private static double Distance(double x1, double y1, double z1, double x2, double y2, double z2)
	{
		return Math.Sqrt(Math.Pow(x2 - x1, 2.0) + Math.Pow(y2 - y1, 2.0) + Math.Pow(z2 - z1, 2.0));
	}

	private static Task AppendAsync(string path, object row, CancellationToken token)
	{
		return File.AppendAllTextAsync(path, JsonSerializer.Serialize(row, JsonOptions) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), token);
	}
}
