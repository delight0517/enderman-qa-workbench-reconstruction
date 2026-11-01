using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Adapters;
using EndermanQa.Desktop.Contracts;
using EndermanQa.Desktop.Manager;
using EndermanQa.Desktop.Persistence;

namespace EndermanQa.Desktop.Scenarios;

public sealed class EndermanSuiteOrchestrator
{
	private const string ClientCaptureUnavailable = "unavailable: this run used the dedicated-server adapter; the task-owned client was not launched because no verified, unmirrored virtual-display lease is available";

	

	private readonly string _dataRoot;

	public EndermanSuiteOrchestrator(string dataRoot)
	{
		_dataRoot = Path.GetFullPath(dataRoot);
	}

	public async Task<IReadOnlyList<SuiteCaseReceipt>> RunAsync(IReadOnlyList<ExpectedActionPlanCase> cases, Func<CancellationToken, Task<IGameAdapter>> connectAdapter, IProgress<SuiteCaseReceipt>? progress, CancellationToken cancellationToken)
	{
		ExpectedActionPlanStore.Validate(cases);
		return await RunCasesAsync(cases, connectAdapter, progress, cancellationToken);
	}

	public async Task<IReadOnlyList<SuiteCaseReceipt>> RerunAnomaliesAsync(Func<CancellationToken, Task<IGameAdapter>> connectAdapter, IProgress<SuiteCaseReceipt>? progress, CancellationToken cancellationToken)
	{
		DirectoryInfo directoryInfo = (Directory.Exists(Path.Combine(_dataRoot, "suite-runs")) ? (from path in Directory.GetDirectories(Path.Combine(_dataRoot, "suite-runs"))
			select new DirectoryInfo(path) into info
			orderby info.CreationTimeUtc descending
			select info).FirstOrDefault() : null);
		if (directoryInfo == null)
		{
			throw new InvalidOperationException("There is no previous suite to inspect for anomalous cases.");
		}
		string path2 = Path.Combine(directoryInfo.FullName, "results.json");
		string planPath = Path.Combine(directoryInfo.FullName, "expected-actions-100.json");
		if (!File.Exists(planPath))
		{
			planPath = Path.Combine(directoryInfo.FullName, "expected-actions-selected.json");
		}
		if (!File.Exists(path2) || !File.Exists(planPath))
		{
			throw new InvalidDataException("The previous suite checkpoint is incomplete; anomaly rerun was not started.");
		}
		JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		SuiteCaseReceipt[] prior = JsonSerializer.Deserialize<SuiteCaseReceipt[]>(await File.ReadAllTextAsync(path2, cancellationToken), options) ?? Array.Empty<SuiteCaseReceipt>();
		ExpectedActionPlanCase[]? source = JsonSerializer.Deserialize<ExpectedActionPlanCase[]>(await File.ReadAllTextAsync(planPath, cancellationToken), options) ?? Array.Empty<ExpectedActionPlanCase>();
		HashSet<int> anomalyIds = (from item in prior.Where(delegate(SuiteCaseReceipt item)
			{
				switch (item.Status)
				{
				case "reproduced-anomaly":
				case "anomaly-unconfirmed":
				case "setup-inconclusive":
					return true;
				default:
					return false;
				}
			})
			select item.SourceId).ToHashSet();
		if (anomalyIds.Count == 0)
		{
			throw new InvalidOperationException("The latest suite has no unconfirmed or setup-inconclusive cases to rerun.");
		}
		ExpectedActionPlanCase[] array = source.Where((ExpectedActionPlanCase item) => anomalyIds.Contains(item.SourceId)).ToArray();
		if (array.Length != anomalyIds.Count)
		{
			throw new InvalidDataException("Anomaly IDs do not match the previous expected-action plan.");
		}
		return await RunSelectedAsync(array, connectAdapter, progress, cancellationToken);
	}

	public Task<IReadOnlyList<SuiteCaseReceipt>> RunSelectedAsync(IReadOnlyList<ExpectedActionPlanCase> cases, Func<CancellationToken, Task<IGameAdapter>> connectAdapter, IProgress<SuiteCaseReceipt>? progress, CancellationToken cancellationToken)
	{
		return RunCasesAsync(cases, connectAdapter, progress, cancellationToken);
	}

	private async Task<IReadOnlyList<SuiteCaseReceipt>> RunCasesAsync(IReadOnlyList<ExpectedActionPlanCase> cases, Func<CancellationToken, Task<IGameAdapter>> connectAdapter, IProgress<SuiteCaseReceipt>? progress, CancellationToken cancellationToken)
	{
		int count = cases.Count;
		bool flag = ((count < 1 || count > 100) ? true : false);
		if (flag || cases.Select((ExpectedActionPlanCase item) => item.SourceId).Distinct().Count() != cases.Count)
		{
			throw new InvalidDataException("The selected case batch must contain 1–100 unique planned cases.");
		}
		string suiteId = Guid.NewGuid().ToString("N");
		string suiteRoot = Path.Combine(_dataRoot, "suite-runs", suiteId);
		Directory.CreateDirectory(suiteRoot);
		string path = ((cases.Count == 100) ? "expected-actions-100.json" : "expected-actions-selected.json");
		await File.WriteAllTextAsync(Path.Combine(suiteRoot, path), JsonSerializer.Serialize(cases, new JsonSerializerOptions
		{
			WriteIndented = true
		}), cancellationToken);
		List<SuiteCaseReceipt> receipts = new List<SuiteCaseReceipt>(cases.Count);
		await new RunJournal(Path.Combine(suiteRoot, "suite.jsonl")).AppendAsync(new
		{
			kind = "suite-start",
			suiteId = suiteId,
			caseCount = cases.Count,
			serverReuse = true,
			worldReuse = true,
			startedAt = DateTimeOffset.UtcNow
		}, cancellationToken);
		bool suitePrepared = false;
		foreach (ExpectedActionPlanCase item2 in cases)
		{
			cancellationToken.ThrowIfCancellationRequested();
			SuiteScenarioSelection selection = SuiteScenarioFactory.TryCreate(item2);
			if ((object)selection == null)
			{
				SuiteCaseReceipt unsupported = new SuiteCaseReceipt(item2.SourceId, item2.CaseId, item2.Title, "not-run-capability", "NotRun", item2.Capability, null, Array.Empty<string>(), null, null, "Required runtime actions/oracle are not implemented in the current Java adapter; no Minecraft test was dispatched.", "unavailable: no run, no visual capture", null);
				receipts.Add(unsupported);
				await AppendReceipt(suiteRoot, suiteId, item2, unsupported, cancellationToken);
				progress?.Report(unsupported);
				continue;
			}
			List<(string RunId, ScenarioRunResult Result)> attempts = new List<(string, ScenarioRunResult)>();
			List<string> validationErrors = new List<string>();
			count = selection.RequiredRepeatTrials;
			if ((count < 1 || count > 20) ? true : false)
			{
				throw new InvalidDataException($"Case {item2.SourceId:000} repeat policy must be between 1 and 20 attempts.");
			}
			int attemptLimit = Math.Max(3, selection.RequiredRepeatTrials);
			for (int attempt = 0; attempt < attemptLimit; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				IGameAdapter adapter = await connectAdapter(cancellationToken);
				var (plan, planValidation) = new ManagerAgent().CreatePlan(selection.Scenario, adapter.Target, adapter.Capabilities);
				if (!planValidation.IsValid || (object)plan == null)
				{
					validationErrors.AddRange(planValidation.Errors);
					await adapter.DisposeAsync();
					break;
				}
				if (!suitePrepared && adapter is ISuiteLifecycleAdapter suiteLifecycleAdapter)
				{
					string preparation = await suiteLifecycleAdapter.PrepareSuiteAsync(cancellationToken);
					await new RunJournal(Path.Combine(suiteRoot, "suite.jsonl")).AppendAsync(new
					{
						kind = "suite-fixture-prepared",
						suiteId = suiteId,
						preparation = preparation,
						target = adapter.Target,
						timestamp = DateTimeOffset.UtcNow
					}, cancellationToken);
					suitePrepared = true;
				}
				string runId = Guid.NewGuid().ToString("N");
				string filePath = Path.Combine(suiteRoot, $"case-{item2.SourceId:000}-attempt-{attempt + 1}.jsonl");
				RunJournal journal = new RunJournal(filePath);
				var provenance = new
				{
					suiteId = suiteId,
					sourceId = item2.SourceId,
					CaseId = item2.CaseId,
					ExpectedActions = item2.ExpectedActions,
					ExpectedObservation = item2.ExpectedObservation,
					PassCriteria = item2.PassCriteria,
					Coverage = selection.Coverage
				};
				await journal.AppendAsync(new
				{
					kind = "case-plan",
					runId = runId,
					provenance = provenance,
					plan = plan,
					target = adapter.Target,
					timestamp = DateTimeOffset.UtcNow
				}, cancellationToken);
				ScenarioRunResult result;
				try
				{
					result = await new ScenarioRunner().RunAsync(runId, plan, adapter, cancellationToken);
				}
				finally
				{
					await adapter.DisposeAsync();
				}
				foreach (AdapterActionResult action in result.Actions)
				{
					await journal.AppendAsync(new
					{
						kind = "action",
						runId = runId,
						action = action
					}, CancellationToken.None);
				}
				if ((object)result.Before != null)
				{
					await journal.AppendAsync(new
					{
						kind = "before",
						runId = runId,
						entity = result.Before
					}, CancellationToken.None);
				}
				if ((object)result.After != null)
				{
					await journal.AppendAsync(new
					{
						kind = "after",
						runId = runId,
						entity = result.After
					}, CancellationToken.None);
				}
				string signature = MakeSignature(result, item2.SourceId);
				await journal.AppendAsync(new
				{
					kind = "attempt-verdict",
					runId = runId,
					Status = result.Status,
					Verdict = result.Verdict,
					signature = signature,
					Limitation = result.Limitation,
					captureStatus = "unavailable: Java dedicated-server adapter has no visual pixel source",
					timestamp = DateTimeOffset.UtcNow
				}, CancellationToken.None);
				attempts.Add((runId, result));
				if (SuiteRetryPolicy.IsInvalidSetup(result.Limitation))
				{
					await journal.AppendAsync(new
					{
						kind = "retry-stopped",
						reason = "invalid-setup-requires-correction",
						runId = runId,
						Limitation = result.Limitation
					}, CancellationToken.None);
					break;
				}
				flag = result.Status == RunStatus.Completed;
				if (flag)
				{
					Verdict verdict = result.Verdict;
					bool flag2 = ((verdict == Verdict.Pass || verdict == Verdict.NotRun) ? true : false);
					flag = flag2;
				}
				if ((flag && attempt + 1 >= selection.RequiredRepeatTrials) || attempt + 1 >= attemptLimit)
				{
					break;
				}
				result = null;
			}
			if (validationErrors.Count > 0 || attempts.Count == 0)
			{
				SuiteCaseReceipt unsupported = new SuiteCaseReceipt(item2.SourceId, item2.CaseId, item2.Title, "not-run-capability", "NotRun", selection.Coverage, null, Array.Empty<string>(), null, null, string.Join("; ", validationErrors), "unavailable: not dispatched", null);
				receipts.Add(unsupported);
				await AppendReceipt(suiteRoot, suiteId, item2, unsupported, cancellationToken);
				progress?.Report(unsupported);
				continue;
			}
			if (item2.SourceId == 77 && attempts.Any<(string, ScenarioRunResult)>(((string RunId, ScenarioRunResult Result) x) => x.Result.Status == RunStatus.Completed && x.Result.Verdict == Verdict.Pass) && attempts.All<(string, ScenarioRunResult)>(delegate((string RunId, ScenarioRunResult Result) x)
			{
				bool flag8 = x.Result.Status == RunStatus.Completed;
				if (flag8)
				{
					Verdict verdict2 = x.Result.Verdict;
					bool flag9 = ((verdict2 == Verdict.Pass || verdict2 == Verdict.Inconclusive) ? true : false);
					flag8 = flag9;
				}
				return flag8;
			}))
			{
				(string, ScenarioRunResult)[] array = attempts.Where<(string, ScenarioRunResult)>(((string RunId, ScenarioRunResult Result) x) => x.Result.Status == RunStatus.Completed && x.Result.Verdict == Verdict.Pass).ToArray();
				(string RunId, ScenarioRunResult Result) representative = array[0];
				bool flag3 = array.Select(((string RunId, ScenarioRunResult Result) x) => MakeSignature(x.Result, item2.SourceId)).Distinct<string>(StringComparer.Ordinal).Count() == 1;
				string repeatability = ((array.Length >= 2 && flag3) ? "confirmed-repeatable-same-signature-over-valid-same-tick-pairs" : $"{array.Length}-valid-same-tick-pair(s); other trials inconclusive because Enderman relocation prevented a comparable second impact");
				int value = attempts.FindIndex(((string RunId, ScenarioRunResult Result) x) => x.RunId == representative.RunId) + 1;
				SuiteCaseReceipt unsupported = new SuiteCaseReceipt(item2.SourceId, item2.CaseId, item2.Title, "executed-partial", "Pass", selection.Coverage, representative.RunId, (from x in attempts
					where x.RunId != representative.RunId
					select x.RunId).ToArray(), repeatability, MakeSignature(representative.Result, item2.SourceId), (attempts.Count == array.Length) ? representative.Result.Limitation : "Only same-tick exact-hit pairs are comparable; other attempts are retained as inconclusive relocation diagnostics.", "unavailable: this run used the dedicated-server adapter; the task-owned client was not launched because no verified, unmirrored virtual-display lease is available", Path.Combine(suiteRoot, $"case-{item2.SourceId:000}-attempt-{value}.jsonl"));
				receipts.Add(unsupported);
				await AppendReceipt(suiteRoot, suiteId, item2, unsupported, cancellationToken);
				progress?.Report(unsupported);
				continue;
			}
			(string RunId, ScenarioRunResult Result) first = attempts[0];
			(string, ScenarioRunResult) tuple2 = attempts[attempts.Count - 1];
			bool flag4 = first.Result.Status != RunStatus.Completed || first.Result.Verdict == Verdict.Fail;
			bool flag5 = first.Result.Verdict == Verdict.Inconclusive;
			int num = attempts.Skip(1).Count<(string, ScenarioRunResult)>(((string RunId, ScenarioRunResult Result) x) => x.Result.Status == first.Result.Status && x.Result.Verdict == first.Result.Verdict && MakeSignature(x.Result, item2.SourceId) == MakeSignature(first.Result, item2.SourceId));
			bool num2 = attempts.Any<(string, ScenarioRunResult)>(((string RunId, ScenarioRunResult Result) x) => SuiteRetryPolicy.IsInvalidSetup(x.Result.Limitation));
			bool flag6 = selection.RequiredRepeatTrials > 1;
			bool flag7 = flag6 && attempts.Count == selection.RequiredRepeatTrials && attempts.All<(string, ScenarioRunResult)>(((string RunId, ScenarioRunResult Result) x) => x.Result.Status == RunStatus.Completed && x.Result.Verdict == first.Result.Verdict) && attempts.Select<(string, ScenarioRunResult), string>(((string RunId, ScenarioRunResult Result) x) => MakeSignature(x.Result, item2.SourceId)).Distinct<string>(StringComparer.Ordinal).Count() == 1 && (item2.SourceId != 100 || FrozenTicksWithinOneTick(attempts));
			string repeatability2 = (num2 ? "invalid-test-setup-not-a-game-reproduction" : (flag5 ? "repeatable-inconclusive-evidence-not-a-product-reproduction" : ((!flag6) ? ((!flag4) ? "not-needed" : ((num == 2) ? "confirmed-repeatable-same-signature" : "not-confirmed-signatures-diverged-or-incomplete")) : (flag7 ? "confirmed-repeatable-same-signature" : "not-confirmed-signatures-diverged-or-incomplete"))));
			string status = (num2 ? "setup-inconclusive" : (flag5 ? "anomaly-unconfirmed" : ((flag6 && !flag4 && !flag7) ? "anomaly-unconfirmed" : (flag4 ? ((num == 2) ? "reproduced-anomaly" : "anomaly-unconfirmed") : (selection.Coverage.StartsWith("full:", StringComparison.Ordinal) ? "executed-pass" : "executed-partial")))));
			string signature2 = MakeSignature(tuple2.Item2, item2.SourceId);
			string journalPath = Path.Combine(suiteRoot, $"case-{item2.SourceId:000}-attempt-{attempts.Count}.jsonl");
			SuiteCaseReceipt receipt = new SuiteCaseReceipt(item2.SourceId, item2.CaseId, item2.Title, status, tuple2.Item2.Verdict.ToString(), selection.Coverage, first.RunId, (from x in attempts.Skip(1)
				select x.RunId).ToArray(), repeatability2, signature2, tuple2.Item2.Limitation, "unavailable: this run used the dedicated-server adapter; the task-owned client was not launched because no verified, unmirrored virtual-display lease is available", journalPath);
			receipts.Add(receipt);
			await AppendReceipt(suiteRoot, suiteId, item2, receipt, cancellationToken);
			progress?.Report(receipt);
		}
		await File.WriteAllTextAsync(Path.Combine(suiteRoot, "results.json"), JsonSerializer.Serialize(receipts, new JsonSerializerOptions
		{
			WriteIndented = true
		}), cancellationToken);
		await File.WriteAllTextAsync(Path.Combine(suiteRoot, "results.csv"), ToCsv(receipts), cancellationToken);
		await new RunJournal(Path.Combine(suiteRoot, "suite.jsonl")).AppendAsync(new
		{
			kind = "suite-finished",
			suiteId = suiteId,
			total = receipts.Count,
			executed = receipts.Count((SuiteCaseReceipt x) => x.FirstRunId != null),
			unsupported = receipts.Count((SuiteCaseReceipt x) => x.Status == "not-run-capability"),
			anomalies = receipts.Count(delegate(SuiteCaseReceipt x)
			{
				string status2 = x.Status;
				return (status2 == "reproduced-anomaly" || status2 == "anomaly-unconfirmed") ? true : false;
			}),
			finishedAt = DateTimeOffset.UtcNow
		}, CancellationToken.None);
		return receipts;
	}

	private static string MakeSignature(ScenarioRunResult result, int sourceId = 0)
	{
		Dictionary<string, string> fields;
		if (sourceId == 55)
		{
			AdapterActionResult adapterActionResult = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "projectile-hit");
			fields = ParseSignatureFields(adapterActionResult?.Detail ?? "");
			string text = Field("firstTargetUuid");
			string text2 = Field("secondTargetUuid");
			string s = string.Join("|", result.Status, result.Verdict, adapterActionResult?.Succeeded, Field("outcome"), !string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2) && !text.Equals(text2, StringComparison.OrdinalIgnoreCase), Field("impactEntityUuid") == text2, Field("placementVerified"), Field("targetAiDisabled"), Field("teleportSucceeded"), Field("firstDamageEventDelta"), ReadHealthDelta(Field("firstHealthBefore"), Field("firstHealthAfter")), Field("firstAlive"), Field("secondDamageEventDelta"), Field("secondArrowDamageEventDelta"), ReadHealthDelta(Field("secondHealthBefore"), Field("secondHealthAfter")), Field("secondAlive"), Field("secondArrowDamageTick") == Field("projectileGameTick"));
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
		}
		if (sourceId == 27)
		{
			AdapterActionResult adapterActionResult2 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "sweep");
			Dictionary<string, string>[] source = (from item in adapterActionResult2?.Events
				where item.Kind == "sweep-target"
				select ParseSignatureFields(item.Detail)).OrderBy<Dictionary<string, string>, string>((Dictionary<string, string> row) => row.GetValueOrDefault("role"), StringComparer.Ordinal).ToArray() ?? Array.Empty<Dictionary<string, string>>();
			string text3 = string.Join("|", source.Select((Dictionary<string, string> row) => string.Join(":", row.GetValueOrDefault("role"), row.GetValueOrDefault("damageEventDelta"), row.GetValueOrDefault("playerDamageEventDelta"), ReadHealthDelta(row.GetValueOrDefault("beforeHealth"), row.GetValueOrDefault("afterHealth")))));
			string s2 = string.Join("|", result.Status, result.Verdict, adapterActionResult2?.Succeeded, adapterActionResult2?.Detail.Contains("enchantmentLevel=1", StringComparison.Ordinal), text3);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s2))).ToLowerInvariant();
		}
		if (sourceId == 1)
		{
			AdapterActionResult adapterActionResult3 = result.Actions.LastOrDefault((AdapterActionResult x) => x.Action == "entity.player_attack" && x.Detail.Contains("Minecraft server Player.attack", StringComparison.OrdinalIgnoreCase));
			EntitySnapshot? after = result.After;
			int result2;
			bool flag = (object)after != null && after.LifeState == EntityLifeState.Dead && result.After.DamageCause == "player" && !string.IsNullOrWhiteSpace(result.After.LastAttackerUuid) && (object)adapterActionResult3 != null && adapterActionResult3.Detail.Contains("attacker " + result.After.LastAttackerUuid + ";", StringComparison.OrdinalIgnoreCase) && Regex.Match(adapterActionResult3.Detail, "(?<hits>\\d+)/64 hits applied").Groups["hits"].Success && int.TryParse(Regex.Match(adapterActionResult3.Detail, "(?<hits>\\d+)/64 hits applied").Groups["hits"].Value, out result2) && result2 >= 1 && result2 <= 64;
			string s3 = string.Join("|", result.Status, result.Verdict, flag);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s3))).ToLowerInvariant();
		}
		object[] obj;
		int num2;
		if (sourceId == 18)
		{
			AdapterActionResult adapterActionResult4 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "first-hit");
			AdapterActionResult adapterActionResult5 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "rapid-second-hit");
			AdapterActionResult? adapterActionResult6 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "recovered-hit");
			bool flag2 = adapterActionResult4?.Detail.Contains("1/1 hits applied", StringComparison.Ordinal) ?? false;
			bool flag3 = adapterActionResult5?.Detail.Contains("0/1 hits applied", StringComparison.Ordinal) ?? false;
			bool flag4 = adapterActionResult6?.Detail.Contains("1/1 hits applied", StringComparison.Ordinal) ?? false;
			obj = new object[7]
			{
				result.Status,
				result.Verdict,
				flag2,
				flag3,
				flag4,
				result.After?.LifeState,
				null
			};
			double? num = result.Before?.Health;
			if (num.HasValue)
			{
				double valueOrDefault = num.GetValueOrDefault();
				num = result.After?.Health;
				if (num.HasValue)
				{
					double valueOrDefault2 = num.GetValueOrDefault();
					num2 = ((valueOrDefault2 < valueOrDefault) ? 1 : 0);
					goto IL_06e8;
				}
			}
			num2 = 0;
			goto IL_06e8;
		}
		bool flag5;
		if (sourceId == 98)
		{
			EntitySnapshot? after2 = result.After;
			flag5 = (object)after2 != null && after2.LifeState == EntityLifeState.Dead;
			if (flag5)
			{
				string damageCause = result.After.DamageCause;
				bool flag6 = ((damageCause == "inFire" || damageCause == "onFire") ? true : false);
				flag5 = flag6;
			}
			bool flag7 = flag5;
			string input = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-after")?.Detail ?? "";
			Match match = Regex.Match(input, "newXp=(?<value>\\d+)");
			Match match2 = Regex.Match(input, "newItemCount=(?<value>\\d+)");
			int result3;
			bool flag8 = match.Success && match2.Success && match.Groups["value"].Value == "0" && int.TryParse(match2.Groups["value"].Value, out result3) && result3 <= 1;
			string s4 = string.Join("|", result.Status, result.Verdict, flag7, flag8);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s4))).ToLowerInvariant();
		}
		if (sourceId == 25)
		{
			AdapterActionResult adapterActionResult7 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "control-kill");
			AdapterActionResult adapterActionResult8 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "looting-kill");
			string text4 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "control-artifacts")?.Detail ?? "";
			string text5 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "looting-artifacts")?.Detail ?? "";
			string text6 = string.Join("|", result.Status, result.Verdict, (object)adapterActionResult7 != null && (adapterActionResult7.Entity?.LifeState).GetValueOrDefault() == EntityLifeState.Dead && adapterActionResult7.Entity.DamageCause == "player", (object)adapterActionResult8 != null && (adapterActionResult8.Entity?.LifeState).GetValueOrDefault() == EntityLifeState.Dead && adapterActionResult8.Entity.DamageCause == "player" && adapterActionResult8.Detail.Contains("profileEnchantmentLevel=1", StringComparison.Ordinal) && adapterActionResult8.Detail.Contains("applied=True", StringComparison.Ordinal), Regex.IsMatch(text4, "newPearls=\\d+") && Regex.IsMatch(text5, "newPearls=\\d+"), text4.Contains("allTargetUuidMatches=True", StringComparison.Ordinal), text5.Contains("allTargetUuidMatches=True", StringComparison.Ordinal));
			text6 = text6 + "|" + (text4.Contains("duplicateArtifactUuid=False", StringComparison.Ordinal) && text5.Contains("duplicateArtifactUuid=False", StringComparison.Ordinal));
			text6 = Regex.Replace(text6, "newPearls=\\d+", "newPearls=<random>");
			text6 = Regex.Replace(text6, "newItemCount=\\d+", "newItemCount=<random>");
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text6))).ToLowerInvariant();
		}
		if (sourceId == 80)
		{
			EntitySnapshot? after3 = result.After;
			bool flag9 = (object)after3 != null && after3.LifeState == EntityLifeState.Dead && result.After.DamageCause == "drown";
			string text7 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-after")?.Detail ?? "";
			bool flag10 = text7.Contains("duplicateArtifactUuid=False", StringComparison.Ordinal) && text7.Contains("allTargetUuidMatches=True", StringComparison.Ordinal);
			Match match3 = Regex.Match(text7, "newPearls=(?<value>\\d+)");
			int result4;
			bool flag11 = match3 != null && match3.Success && int.TryParse(match3.Groups["value"].Value, out result4) && result4 <= 1;
			string s5 = string.Join("|", result.Status, result.Verdict, flag9, flag10, flag11);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s5))).ToLowerInvariant();
		}
		if (sourceId == 97)
		{
			EntitySnapshot? after4 = result.After;
			bool flag12 = (object)after4 != null && after4.LifeState == EntityLifeState.Dead && result.After.DamageCause == "lava";
			string text8 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "lava-lethal")?.Detail ?? "";
			bool flag13 = result.Actions.Any((AdapterActionResult action) => action.StepId == "lava-item-window" && action.Succeeded && action.Detail.Contains("2 seconds", StringComparison.Ordinal));
			string text9 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "artifact-after")?.Detail ?? "";
			bool flag14 = text8.Contains("source=lava", StringComparison.Ordinal) && text8.Contains("lavaReadback=true", StringComparison.OrdinalIgnoreCase) && text8.Contains("damageApplied=true", StringComparison.OrdinalIgnoreCase);
			bool flag15 = text9.Contains("duplicateArtifactUuid=False", StringComparison.Ordinal) && text9.Contains("allTargetUuidMatches=True", StringComparison.Ordinal);
			string s6 = string.Join("|", result.Status, result.Verdict, flag12, flag14, flag13, flag15);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s6))).ToLowerInvariant();
		}
		if (sourceId == 35)
		{
			AdapterActionResult adapterActionResult9 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "component-attack");
			bool flag16 = adapterActionResult9?.Detail.Contains("customComponentPresent=True", StringComparison.Ordinal) ?? false;
			bool flag17 = adapterActionResult9?.Detail.Contains("1/1 hits applied", StringComparison.Ordinal) ?? false;
			bool flag18 = (object)adapterActionResult9 != null && (adapterActionResult9.Entity?.LifeState).GetValueOrDefault() == EntityLifeState.Dead && adapterActionResult9.Entity.DamageCause == "player";
			string s7 = string.Join("|", result.Status, result.Verdict, flag16, flag17, flag18);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s7))).ToLowerInvariant();
		}
		bool flag19;
		int num3;
		if (sourceId == 71)
		{
			AdapterActionResult adapterActionResult10 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-impact");
			flag19 = (object)adapterActionResult10 != null && adapterActionResult10.Detail.Contains("projectileType=splash_water_potion", StringComparison.Ordinal) && adapterActionResult10.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) && adapterActionResult10.Detail.Contains("targetHit=True", StringComparison.Ordinal);
			double? num = result.Before?.Health;
			if (num.HasValue)
			{
				double valueOrDefault3 = num.GetValueOrDefault();
				num = result.After?.Health;
				if (num.HasValue)
				{
					double valueOrDefault4 = num.GetValueOrDefault();
					num3 = ((Math.Abs(valueOrDefault3 - valueOrDefault4) > 0.01) ? 1 : 0);
					goto IL_1043;
				}
			}
			num3 = 0;
			goto IL_1043;
		}
		int num4 = sourceId;
		bool flag20;
		double? obj2;
		if (((uint)(num4 - 75) <= 1u || num4 == 78) ? true : false)
		{
			AdapterActionResult adapterActionResult11 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-impact");
			string text10 = ((sourceId == 76) ? "splash_healing_potion" : "splash_harming_potion");
			flag20 = (object)adapterActionResult11 != null && adapterActionResult11.Detail.Contains("projectileType=" + text10, StringComparison.Ordinal) && adapterActionResult11.Detail.Contains((sourceId == 76) ? "effect.minecraft.instant_health" : "effect.minecraft.instant_damage", StringComparison.Ordinal) && adapterActionResult11.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) && adapterActionResult11.Detail.Contains("targetHit=True", StringComparison.Ordinal);
			double? num5 = ((sourceId != 76) ? result.Before?.Health : result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "low-health")?.Entity?.Health);
			if (num5.HasValue)
			{
				double valueOrDefault5 = num5.GetValueOrDefault();
				double? num = result.After?.Health;
				if (num.HasValue)
				{
					double valueOrDefault6 = num.GetValueOrDefault();
					obj2 = Math.Round(valueOrDefault6 - valueOrDefault5, 2);
					goto IL_1345;
				}
			}
			obj2 = null;
			goto IL_1345;
		}
		if (sourceId == 77)
		{
			AdapterActionResult adapterActionResult12 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-pair");
			bool flag21 = (object)adapterActionResult12 != null && adapterActionResult12.Detail.Contains("waterProjectileHit=True", StringComparison.Ordinal) && adapterActionResult12.Detail.Contains("harmingProjectileHit=True", StringComparison.Ordinal) && adapterActionResult12.Detail.Contains("sameImpactTick=True", StringComparison.Ordinal);
			EntitySnapshot? after5 = result.After;
			bool flag22 = (object)after5 != null && (after5.DamageEvents?.Count).GetValueOrDefault() == 1 && result.After.DamageEvents[0].Cause == "indirectMagic";
			double num6 = (flag22 ? Math.Round(result.After.DamageEvents[0].DamageTaken, 2) : 0.0);
			string s8 = string.Join("|", result.Status, result.Verdict, flag21, flag22, num6, result.After?.LifeState);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s8))).ToLowerInvariant();
		}
		bool flag23;
		int num7;
		if (sourceId == 94)
		{
			string text11 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "ice-slope")?.Detail ?? "";
			flag23 = text11.Contains("iceReadback=True", StringComparison.Ordinal) && text11.Contains("shaftAir=135/135", StringComparison.Ordinal) && text11.Contains("landingBlocks=5/5", StringComparison.Ordinal);
			WorldPositionSnapshot worldPositionSnapshot = result.Before?.Position;
			if ((object)worldPositionSnapshot != null)
			{
				WorldPositionSnapshot worldPositionSnapshot2 = result.After?.Position;
				if ((object)worldPositionSnapshot2 != null)
				{
					num7 = ((worldPositionSnapshot2.X - worldPositionSnapshot.X >= 4.0) ? 1 : 0);
					goto IL_1744;
				}
			}
			num7 = 0;
			goto IL_1744;
		}
		int num8;
		if (sourceId == 95)
		{
			DamageEventSnapshot[] array = result.After?.DamageEvents?.Where((DamageEventSnapshot item) => item.Cause == "cactus").ToArray() ?? Array.Empty<DamageEventSnapshot>();
			if (array.Length == 1 && Math.Abs(array[0].DamageTaken - 1.0) < 0.01)
			{
				double? num = result.Before?.Health;
				if (num.HasValue)
				{
					double valueOrDefault7 = num.GetValueOrDefault();
					num = result.After?.Health;
					if (num.HasValue)
					{
						double valueOrDefault8 = num.GetValueOrDefault();
						num8 = ((Math.Abs(valueOrDefault7 - valueOrDefault8 - 1.0) < 0.01) ? 1 : 0);
						goto IL_198f;
					}
				}
			}
			num8 = 0;
			goto IL_198f;
		}
		int num9;
		if (sourceId == 100)
		{
			double? num = result.Before?.Health;
			if (num.HasValue)
			{
				double valueOrDefault9 = num.GetValueOrDefault();
				num = result.After?.Health;
				if (num.HasValue)
				{
					double valueOrDefault10 = num.GetValueOrDefault();
					num9 = ((Math.Abs(valueOrDefault9 - valueOrDefault10) < 0.01) ? 1 : 0);
					goto IL_1a90;
				}
			}
			num9 = 0;
			goto IL_1a90;
		}
		if (sourceId == 99)
		{
			DamageEventSnapshot[] array2 = (result.After?.DamageEvents ?? Array.Empty<DamageEventSnapshot>()).Where((DamageEventSnapshot x) => x.Cause == "inFire").ToArray();
			string text12 = result.After?.LastAttackerUuid;
			string input2 = result.Actions.FirstOrDefault((AdapterActionResult x) => x.StepId == "environment-and-attack")?.Detail ?? "";
			bool flag24 = !string.IsNullOrWhiteSpace(text12) && Regex.IsMatch(input2, "\\battacker\\s+" + Regex.Escape(text12) + "(?:[.;,]|\\s|$)", RegexOptions.IgnoreCase);
			EntitySnapshot? after6 = result.After;
			bool flag25 = (object)after6 != null && after6.LifeState == EntityLifeState.Dead && result.After.DamageCause == "player" && flag24;
			bool flag26 = array2.Length == 1 && result.After?.GameTick == array2[0].GameTick;
			string s9 = string.Join("|", result.Status, result.Verdict, flag26, flag25);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s9))).ToLowerInvariant();
		}
		if (sourceId == 87)
		{
			IReadOnlyList<DamageEventSnapshot> source2 = result.After?.DamageEvents ?? Array.Empty<DamageEventSnapshot>();
			DamageEventSnapshot[] array3 = source2.Where(delegate(DamageEventSnapshot x)
			{
				string cause = x.Cause;
				return (cause == "inFire" || cause == "onFire") ? true : false;
			}).ToArray();
			DamageEventSnapshot[] lava = source2.Where((DamageEventSnapshot x) => x.Cause == "lava").ToArray();
			bool flag27 = array3.Any((DamageEventSnapshot f) => lava.Any((DamageEventSnapshot l) => l.GameTick == f.GameTick));
			bool flag28 = (from x in source2
				group x by (GameTick: x.GameTick, Cause: x.Cause)).All((IGrouping<(long GameTick, string Cause), DamageEventSnapshot> group) => group.Count() == 1);
			bool flag29 = array3.All((DamageEventSnapshot x) => Math.Abs(x.DamageTaken - 1.0) < 0.01) && lava.All((DamageEventSnapshot x) => Math.Abs(x.DamageTaken - 4.0) < 0.01);
			string s10 = string.Join("|", result.Status, result.Verdict, array3.Length != 0, lava.Length != 0, flag27, flag28, flag29);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s10))).ToLowerInvariant();
		}
		bool flag30;
		bool flag31;
		object obj3;
		if (sourceId == 13)
		{
			AdapterActionResult adapterActionResult13 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "attack-before-teleport");
			flag30 = adapterActionResult13?.Detail.Contains("1/1 hits applied", StringComparison.Ordinal) ?? false;
			flag31 = (object)adapterActionResult13 != null && adapterActionResult13.Detail.Contains("teleportSucceeded=True", StringComparison.OrdinalIgnoreCase) && adapterActionResult13.Detail.Contains("safePadBlocks=9", StringComparison.OrdinalIgnoreCase);
			double? num = result.Before?.Health;
			if (num.HasValue)
			{
				double valueOrDefault11 = num.GetValueOrDefault();
				num = result.After?.Health;
				if (num.HasValue)
				{
					double valueOrDefault12 = num.GetValueOrDefault();
					obj3 = (valueOrDefault11 - valueOrDefault12).ToString("F2");
					goto IL_1fe9;
				}
			}
			obj3 = "unknown";
			goto IL_1fe9;
		}
		if (sourceId == 59)
		{
			AdapterActionResult adapterActionResult14 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "projectile-hit");
			bool flag32 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "mount-boat")?.Detail.Contains("mounted=true", StringComparison.OrdinalIgnoreCase) ?? false;
			bool flag33 = (object)adapterActionResult14 != null && adapterActionResult14.Succeeded && adapterActionResult14.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) && adapterActionResult14.Detail.Contains("targetHit=True", StringComparison.Ordinal);
			string text13 = (from item in result.After?.DamageEvents?.Where((DamageEventSnapshot item) => item.Cause == "arrow")
				select $"{item.Cause}:{item.BaseDamage:F2}:{item.DamageTaken:F2}:{item.HealthAfter:F2}:{item.Blocked}").FirstOrDefault() ?? "none";
			string s11 = string.Join("|", result.Status, result.Verdict, flag32, flag33, result.Before?.Health?.ToString("F2"), result.After?.Health?.ToString("F2"), result.After?.LifeState, text13);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s11))).ToLowerInvariant();
		}
		if (sourceId == 49)
		{
			AdapterActionResult adapterActionResult15 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "firework-hit");
			string text14 = (((object)adapterActionResult15 == null) ? "" : Regex.Match(adapterActionResult15.Detail, "impactEntityUuid=([0-9a-fA-F-]{36})").Groups[1].Value);
			string value = (((object)adapterActionResult15 == null) ? "" : Regex.Match(adapterActionResult15.Detail, "targetUuid=([0-9a-fA-F-]{36})").Groups[1].Value);
			bool flag34 = (object)adapterActionResult15 != null && adapterActionResult15.Succeeded && adapterActionResult15.Detail.Contains("outcome=entity-hit", StringComparison.Ordinal) && text14.Length == 36 && text14.Equals(value, StringComparison.OrdinalIgnoreCase);
			string text15 = (from item in result.After?.DamageEvents?.Where((DamageEventSnapshot item) => item.Cause.Equals("fireworks", StringComparison.OrdinalIgnoreCase))
				select $"{item.Cause}:{item.BaseDamage:F2}:{item.DamageTaken:F2}:{item.HealthAfter:F2}:{item.Blocked}").FirstOrDefault() ?? "none";
			string s12 = string.Join("|", result.Status, result.Verdict, flag34, result.Before?.Health?.ToString("F2"), result.After?.Health?.ToString("F2"), result.After?.LifeState, text15);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s12))).ToLowerInvariant();
		}
		if (sourceId == 72)
		{
			AdapterActionResult adapterActionResult16 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-outside");
			AdapterActionResult adapterActionResult17 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "potion-inside");
			string text16 = (from item in adapterActionResult16?.Entity?.DamageEvents?.Where((DamageEventSnapshot item) => item.Cause.Contains("indirectMagic", StringComparison.OrdinalIgnoreCase))
				select $"{item.Cause}:{item.DamageTaken:F2}:{item.HealthAfter:F2}").FirstOrDefault() ?? "none";
			string text17 = (from item in adapterActionResult17?.Entity?.DamageEvents?.Where((DamageEventSnapshot item) => item.Cause.Contains("indirectMagic", StringComparison.OrdinalIgnoreCase))
				select $"{item.Cause}:{item.DamageTaken:F2}:{item.HealthAfter:F2}").FirstOrDefault() ?? "none";
			string s13 = string.Join("|", result.Status, result.Verdict, adapterActionResult16?.Detail.Contains("outcome=block-hit", StringComparison.Ordinal) ?? false, adapterActionResult16?.Entity?.Health?.ToString("F2"), text16, adapterActionResult17?.Detail.Contains("outcome=block-hit", StringComparison.Ordinal) ?? false, adapterActionResult17?.Entity?.Health?.ToString("F2"), text17);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s13))).ToLowerInvariant();
		}
		if (sourceId == 84)
		{
			double? num = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "set-low-health")?.Entity?.Health;
			int num10;
			if (num.HasValue)
			{
				double valueOrDefault13 = num.GetValueOrDefault();
				num10 = ((Math.Abs(valueOrDefault13 - 6.0) < 0.01) ? 1 : 0);
			}
			else
			{
				num10 = 0;
			}
			bool flag35 = (byte)num10 != 0;
			AdapterActionResult adapterActionResult18 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "lava-contact");
			bool flag36 = (object)adapterActionResult18 != null && adapterActionResult18.Succeeded && adapterActionResult18.Detail.Contains("lavaReadback=True", StringComparison.OrdinalIgnoreCase);
			EntitySnapshot after7 = result.After;
			string s14 = string.Join("|", result.Status, result.Verdict, flag35, flag36, after7?.LifeState, after7?.Health?.ToString("F2"), after7?.DamageCause);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s14))).ToLowerInvariant();
		}
		AdapterActionResult adapterActionResult19;
		bool flag37;
		EntitySnapshot after8;
		int num11;
		if (sourceId == 83)
		{
			adapterActionResult19 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "safe-escape");
			flag37 = (object)adapterActionResult19 != null && adapterActionResult19.Succeeded && adapterActionResult19.Detail.Contains("lavaRemoved=True", StringComparison.OrdinalIgnoreCase) && adapterActionResult19.Detail.Contains("safePadFloorBlocks=25", StringComparison.OrdinalIgnoreCase) && adapterActionResult19.Detail.Contains("safePadDryCells=18", StringComparison.OrdinalIgnoreCase) && adapterActionResult19.Detail.Contains("safePadWallBlocks=32", StringComparison.OrdinalIgnoreCase) && adapterActionResult19.Detail.Contains("targetAiDisabled=true", StringComparison.OrdinalIgnoreCase) && adapterActionResult19.Detail.Contains("teleported=true", StringComparison.OrdinalIgnoreCase);
			after8 = result.After;
			double? num = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "observe-before-escape")?.Entity?.Health;
			if (num.HasValue)
			{
				double valueOrDefault14 = num.GetValueOrDefault();
				num = after8?.Health;
				if (num.HasValue)
				{
					double valueOrDefault15 = num.GetValueOrDefault();
					num11 = ((Math.Abs(valueOrDefault14 - valueOrDefault15) < 0.01) ? 1 : 0);
					goto IL_2a61;
				}
			}
			num11 = 0;
			goto IL_2a61;
		}
		num4 = sourceId;
		AdapterActionResult adapterActionResult22;
		bool flag38;
		bool flag39;
		bool flag40;
		bool flag41;
		bool flag42;
		DamageEventSnapshot[] array4;
		if ((num4 == 74 || num4 == 79) ? true : false)
		{
			AdapterActionResult? adapterActionResult20 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "isolate-ai");
			AdapterActionResult adapterActionResult21 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "water-cloud");
			adapterActionResult22 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "leave-cloud");
			WorldPositionSnapshot worldPositionSnapshot3 = result.Actions.FirstOrDefault((AdapterActionResult action) => action.StepId == "observe-initial-exit")?.Entity?.Position;
			flag38 = adapterActionResult20?.Detail.Contains("targetAiDisabled=true", StringComparison.OrdinalIgnoreCase) ?? false;
			flag39 = (object)adapterActionResult21 != null && adapterActionResult21.Detail.Contains("cloudPotion=water", StringComparison.Ordinal) && adapterActionResult21.Detail.Contains("cloudDuration=1200", StringComparison.Ordinal) && adapterActionResult21.Detail.Contains("cloudOwner=none", StringComparison.Ordinal);
			IReadOnlyList<DamageEventSnapshot> readOnlyList = adapterActionResult22?.Entity?.DamageEvents;
			flag40 = readOnlyList != null && (result.Before?.DamageEvents?.Count).GetValueOrDefault() < readOnlyList.Count && readOnlyList.Skip((result.Before?.DamageEvents?.Count).GetValueOrDefault()).Any((DamageEventSnapshot damage) => string.Equals(damage.Cause, "drown", StringComparison.OrdinalIgnoreCase));
			flag41 = (object)adapterActionResult22 != null && adapterActionResult22.Succeeded && adapterActionResult22.Detail.Contains("targetMoved=true", StringComparison.Ordinal) && adapterActionResult22.Detail.Contains("safePadBlocks=9", StringComparison.Ordinal);
			WorldPositionSnapshot worldPositionSnapshot4 = result.Before?.Position;
			flag42 = (object)worldPositionSnapshot4 != null && (object)worldPositionSnapshot3 != null && HorizontalDistance(worldPositionSnapshot4, worldPositionSnapshot3) > 8.0;
			long? num12 = adapterActionResult22?.Entity?.GameTick;
			if (num12.HasValue)
			{
				long exitTick = num12.GetValueOrDefault();
				IReadOnlyList<DamageEventSnapshot> readOnlyList2 = result.After?.DamageEvents;
				if (readOnlyList2 != null)
				{
					array4 = readOnlyList2.Where((DamageEventSnapshot damage) => damage.GameTick > exitTick).ToArray();
					goto IL_2ee6;
				}
			}
			array4 = Array.Empty<DamageEventSnapshot>();
			goto IL_2ee6;
		}
		string s15 = string.Join("|", result.Status, result.Verdict, result.Before?.LifeState, result.Before?.Health?.ToString("F2"), result.After?.LifeState, result.After?.Health?.ToString("F2"), result.After?.DamageCause, string.Join(",", result.Actions.Select((AdapterActionResult a) => a.Action + ":" + a.Succeeded + ":" + NormalizeDetail(a.Detail, sourceId))));
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s15))).ToLowerInvariant();
		IL_1345:
		double? num13 = obj2;
		switch (sourceId)
		{
		case 75:
		{
			EntitySnapshot? after10 = result.After;
			flag5 = (object)after10 != null && after10.LifeState == EntityLifeState.Alive && num13 == 0.0;
			break;
		}
		case 76:
		{
			EntitySnapshot? after11 = result.After;
			flag5 = (object)after11 != null && after11.LifeState == EntityLifeState.Alive && num13 > 0.0;
			break;
		}
		default:
		{
			EntitySnapshot? after9 = result.After;
			flag5 = (object)after9 != null && after9.LifeState == EntityLifeState.Alive && num13 == 0.0 && result.After.DamageCause == null;
			break;
		}
		}
		bool flag43 = flag5;
		string s16 = string.Join("|", result.Status, result.Verdict, sourceId, flag20, flag43, result.After?.LifeState, result.After?.DamageCause);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s16))).ToLowerInvariant();
		IL_06e8:
		obj[6] = (byte)num2 != 0;
		string s17 = string.Join("|", obj);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s17))).ToLowerInvariant();
		IL_2ee6:
		DamageEventSnapshot[] array5 = array4;
		Dictionary<long, PositionTraceSnapshot> traceByTick = (from sample in result.After?.PositionTrace ?? Array.Empty<PositionTraceSnapshot>()
			group sample by sample.GameTick).ToDictionary((IGrouping<long, PositionTraceSnapshot> group) => group.Key, (IGrouping<long, PositionTraceSnapshot> group) => group.Last());
		(DamageEventSnapshot Damage, PositionTraceSnapshot Sample)[] source3 = array5.Select(delegate(DamageEventSnapshot damage)
		{
			PositionTraceSnapshot value2;
			PositionTraceSnapshot item4 = (traceByTick.TryGetValue(damage.GameTick, out value2) ? value2 : null);
			return (Damage: damage, Sample: item4);
		}).ToArray();
		bool flag44 = source3.Any(delegate((DamageEventSnapshot Damage, PositionTraceSnapshot Sample) pair)
		{
			PositionTraceSnapshot item3 = pair.Sample;
			if ((object)item3 != null)
			{
				double? cloudHorizontalDistance2 = item3.CloudHorizontalDistance;
				if (cloudHorizontalDistance2.HasValue)
				{
					double valueOrDefault18 = cloudHorizontalDistance2.GetValueOrDefault();
					double? cloudRadius2 = item3.CloudRadius;
					if (cloudRadius2.HasValue)
					{
						double valueOrDefault19 = cloudRadius2.GetValueOrDefault();
						return valueOrDefault18 > valueOrDefault19;
					}
				}
			}
			return false;
		});
		bool flag45 = source3.Any(delegate((DamageEventSnapshot Damage, PositionTraceSnapshot Sample) pair)
		{
			PositionTraceSnapshot item2 = pair.Sample;
			if ((object)item2 != null)
			{
				double? cloudHorizontalDistance = item2.CloudHorizontalDistance;
				if (cloudHorizontalDistance.HasValue)
				{
					double valueOrDefault16 = cloudHorizontalDistance.GetValueOrDefault();
					double? cloudRadius = item2.CloudRadius;
					if (cloudRadius.HasValue)
					{
						double valueOrDefault17 = cloudRadius.GetValueOrDefault();
						return valueOrDefault16 <= valueOrDefault17;
					}
				}
			}
			return false;
		});
		bool flag46 = source3.Any(((DamageEventSnapshot Damage, PositionTraceSnapshot Sample) pair) => (object)pair.Sample == null || !pair.Sample.CloudHorizontalDistance.HasValue || !pair.Sample.CloudRadius.HasValue);
		string s18 = string.Join("|", result.Status, result.Verdict, flag38, flag39, flag41, result.Before?.LifeState, adapterActionResult22?.Entity?.LifeState, result.After?.LifeState, flag40, flag42, array5.Length != 0, flag44, flag45, flag46, (adapterActionResult22?.Entity?.DamageEvents?.Count).GetValueOrDefault());
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s18))).ToLowerInvariant();
		IL_1a90:
		bool flag47 = (byte)num9 != 0;
		bool flag48 = (result.After?.FrozenTicks).HasValue;
		string s19 = string.Join("|", result.Status, result.Verdict, result.Before?.LifeState, result.After?.LifeState, flag47, flag48);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s19))).ToLowerInvariant();
		IL_1796:
		int num14;
		bool flag49 = (byte)num14 != 0;
		EntitySnapshot? after12 = result.After;
		bool flag50 = (object)after12 != null && (after12.DamageEvents?.Any((DamageEventSnapshot item) => item.Cause == "fall" && item.DamageTaken > 0.0)).GetValueOrDefault();
		bool flag51;
		string s20 = string.Join("|", result.Status, result.Verdict, flag23, flag51, flag49, flag50, result.After?.LifeState);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s20))).ToLowerInvariant();
		IL_2aaa:
		int num15;
		bool flag52 = (byte)num15 != 0;
		WorldPositionSnapshot worldPositionSnapshot5 = adapterActionResult19?.Entity?.Position;
		int num16;
		if ((object)worldPositionSnapshot5 != null)
		{
			WorldPositionSnapshot worldPositionSnapshot6 = after8?.Position;
			if ((object)worldPositionSnapshot6 != null && Math.Abs(worldPositionSnapshot5.X - worldPositionSnapshot6.X) < 0.01 && Math.Abs(worldPositionSnapshot5.Y - worldPositionSnapshot6.Y) < 0.01)
			{
				num16 = ((Math.Abs(worldPositionSnapshot5.Z - worldPositionSnapshot6.Z) < 0.01) ? 1 : 0);
				goto IL_2b40;
			}
		}
		num16 = 0;
		goto IL_2b40;
		IL_2a61:
		bool flag53 = (byte)num11 != 0;
		IReadOnlyList<DamageEventSnapshot> readOnlyList3 = adapterActionResult19?.Entity?.DamageEvents;
		if (readOnlyList3 != null)
		{
			IReadOnlyList<DamageEventSnapshot> readOnlyList4 = after8?.DamageEvents;
			if (readOnlyList4 != null)
			{
				num15 = ((readOnlyList4.Count == readOnlyList3.Count) ? 1 : 0);
				goto IL_2aaa;
			}
		}
		num15 = 0;
		goto IL_2aaa;
		IL_1043:
		WorldPositionSnapshot worldPositionSnapshot7 = result.Before?.Position;
		int num17;
		if ((object)worldPositionSnapshot7 != null)
		{
			WorldPositionSnapshot worldPositionSnapshot8 = result.After?.Position;
			if ((object)worldPositionSnapshot8 != null && worldPositionSnapshot7.Dimension == worldPositionSnapshot8.Dimension)
			{
				num17 = ((Math.Pow(worldPositionSnapshot7.X - worldPositionSnapshot8.X, 2.0) + Math.Pow(worldPositionSnapshot7.Y - worldPositionSnapshot8.Y, 2.0) + Math.Pow(worldPositionSnapshot7.Z - worldPositionSnapshot8.Z, 2.0) >= 64.0) ? 1 : 0);
				goto IL_10f5;
			}
		}
		num17 = 0;
		goto IL_10f5;
		IL_10f5:
		bool flag54 = (byte)num17 != 0;
		int num18;
		if (((uint)num3 | (flag54 ? 1u : 0u)) == 0)
		{
			EntitySnapshot? after13 = result.After;
			num18 = (((object)after13 != null && after13.LifeState == EntityLifeState.Dead) ? 1 : 0);
		}
		else
		{
			num18 = 1;
		}
		bool flag55 = (byte)num18 != 0;
		object[] obj4 = new object[5] { result.Status, result.Verdict, flag19, flag55, null };
		EntitySnapshot? after14 = result.After;
		obj4[4] = (object)after14 != null && after14.LifeState == EntityLifeState.Dead;
		string s21 = string.Join("|", obj4);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s21))).ToLowerInvariant();
		IL_2b40:
		bool flag56 = (byte)num16 != 0;
		string s22 = string.Join("|", result.Status, result.Verdict, flag37, flag53, flag52, flag56, after8?.LifeState);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s22))).ToLowerInvariant();
		IL_198f:
		bool flag57 = (byte)num8 != 0;
		string s23 = string.Join("|", result.Status, result.Verdict, flag57, result.After?.LifeState);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s23))).ToLowerInvariant();
		IL_1744:
		flag51 = (byte)num7 != 0;
		WorldPositionSnapshot worldPositionSnapshot9 = result.Before?.Position;
		if ((object)worldPositionSnapshot9 != null)
		{
			WorldPositionSnapshot worldPositionSnapshot10 = result.After?.Position;
			if ((object)worldPositionSnapshot10 != null)
			{
				num14 = ((worldPositionSnapshot9.Y - worldPositionSnapshot10.Y >= 20.0) ? 1 : 0);
				goto IL_1796;
			}
		}
		num14 = 0;
		goto IL_1796;
		IL_1fe9:
		string text18 = (string)obj3;
		string s24 = string.Join("|", result.Status, result.Verdict, flag30, text18, flag31, result.After?.LifeState);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s24))).ToLowerInvariant();
		string? Field(string key)
		{
			return fields.GetValueOrDefault(key);
		}
		static double HorizontalDistance(WorldPositionSnapshot from, WorldPositionSnapshot to)
		{
			double num19 = from.X - to.X;
			double num20 = from.Z - to.Z;
			return Math.Sqrt(num19 * num19 + num20 * num20);
		}
		static string NormalizeDetail(string detail, int sourceId)
		{
			if (sourceId == 25)
			{
				detail = Regex.Replace(detail, "newPearls=\\d+", "newPearls=<random>");
				detail = Regex.Replace(detail, "newItemCount=\\d+", "newItemCount=<random>");
				detail = Regex.Replace(detail, "newXp=\\d+", "newXp=<random>");
			}
			detail = Regex.Replace(detail, "attacker [0-9a-fA-F-]{36}", "attacker <uuid>");
			detail = Regex.Replace(detail, "(projectileUuid|impactEntityUuid|targetUuid)=[0-9a-fA-F-]{36}", "$1=<uuid>");
			if (sourceId == 100)
			{
				detail = Regex.Replace(detail, "frozenTicks=\\d+", "frozenTicks=<within-1>");
			}
			bool flag58;
			switch (sourceId)
			{
			case 42:
			case 44:
			case 45:
			case 46:
			case 47:
			case 48:
			case 50:
			case 51:
			case 52:
			case 53:
			case 54:
			case 56:
			case 58:
				flag58 = true;
				break;
			default:
				flag58 = false;
				break;
			}
			if (flag58)
			{
				detail = Regex.Replace(detail, "lastPosition=\\([^)]*\\)", "lastPosition=<trajectory>");
				detail = Regex.Replace(detail, "lastVelocity=\\([^)]*\\)", "lastVelocity=<trajectory>");
			}
			return detail;
		}
	}

	private static bool FrozenTicksWithinOneTick(IReadOnlyList<(string RunId, ScenarioRunResult Result)> attempts)
	{
		int?[] array = attempts.Select<(string, ScenarioRunResult), int?>(((string RunId, ScenarioRunResult Result) item) => item.Result.After?.FrozenTicks).ToArray();
		if (array.Length == 0 || array.Any((int? sample) => !sample.HasValue))
		{
			return false;
		}
		int baseline = array[0].Value;
		return array.All((int? sample) => Math.Abs(sample.Value - baseline) <= 1);
	}

	private static Dictionary<string, string> ParseSignatureFields(string detail)
	{
		return (from token in detail.Split(';', StringSplitOptions.TrimEntries)
			select token.Split('=', 2, StringSplitOptions.TrimEntries) into parts
			where parts.Length == 2
			select parts).GroupBy<string[], string>((string[] parts) => parts[0], StringComparer.Ordinal).ToDictionary<IGrouping<string, string[]>, string, string>((IGrouping<string, string[]> group) => group.Key, (IGrouping<string, string[]> group) => group.Last()[1], StringComparer.Ordinal);
	}

	private static string ReadHealthDelta(string? before, string? after)
	{
		if (!double.TryParse(before, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !double.TryParse(after, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2))
		{
			return "unknown";
		}
		return (result - result2).ToString("F2", CultureInfo.InvariantCulture);
	}

	private static async Task AppendReceipt(string root, string suiteId, ExpectedActionPlanCase item, SuiteCaseReceipt receipt, CancellationToken token)
	{
		SuiteCaseReceipt receipt2 = receipt;
		await new RunJournal(Path.Combine(root, "suite.jsonl")).AppendAsync(new
		{
			kind = "case-receipt",
			suiteId = suiteId,
			expected = item,
			receipt = receipt2,
			recordedAt = DateTimeOffset.UtcNow
		}, token);
		string path = Path.Combine(root, "results.json");
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true
		};
		List<SuiteCaseReceipt> list = ((!File.Exists(path)) ? new List<SuiteCaseReceipt>() : (JsonSerializer.Deserialize<List<SuiteCaseReceipt>>(await File.ReadAllTextAsync(path, token), options) ?? new List<SuiteCaseReceipt>()));
		List<SuiteCaseReceipt> saved = list;
		saved.RemoveAll((SuiteCaseReceipt x) => x.SourceId == receipt2.SourceId);
		saved.Add(receipt2);
		string temporary = path + ".tmp";
		await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(saved, options), CancellationToken.None);
		File.Move(temporary, path, overwrite: true);
		try
		{
			await QaSopMonitor.WriteAsync(root, saved);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			await new RunJournal(Path.Combine(root, "suite.jsonl")).AppendAsync(new
			{
				kind = "sop-monitor-warning",
				error = ex.GetType().Name,
				message = "SOP review could not be saved; completed case evidence is retained."
			}, CancellationToken.None);
		}
	}

	private static string ToCsv(IEnumerable<SuiteCaseReceipt> receipts)
	{
		List<string> list = new List<string> { "sourceId,caseId,title,status,verdict,coverage,firstRunId,repeatRunIds,repeatability,signature,limitation,captureStatus,journalPath" };
		list.AddRange(receipts.Select((SuiteCaseReceipt r) => string.Join(',', r.SourceId, Q(r.CaseId), Q(r.Title), Q(r.Status), Q(r.Verdict), Q(r.Coverage), Q(r.FirstRunId), Q(string.Join(';', r.RepeatRunIds)), Q(r.Repeatability), Q(r.Signature), Q(r.Limitation), Q(r.CaptureStatus), Q(r.JournalPath))));
		return string.Join(Environment.NewLine, list) + Environment.NewLine;
		static string Q(string? value)
		{
			return "\"" + (value ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
		}
	}
}
