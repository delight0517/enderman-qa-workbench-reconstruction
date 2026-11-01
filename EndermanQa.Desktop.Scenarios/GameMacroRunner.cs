using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Adapters;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Scenarios;

public sealed class GameMacroRunner
{
	private const string MissingObservationSentinel = "unknown";

	public async Task<GameMacroRunResult> RunAsync(GameAutomationMacro macro, IGameMacroAdapter adapter, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(macro, "macro");
		ArgumentNullException.ThrowIfNull(adapter, "adapter");
		List<GameMacroStepResult> stepResults = new List<GameMacroStepResult>();
		List<GameMacroAssertionResult> assertionResults = new List<GameMacroAssertionResult>();
		RunStatus status = RunStatus.Running;
		Verdict verdict = Verdict.Inconclusive;
		string limitation = string.Empty;
		long generation = 0L;
		try
		{
			_ = 1;
			try
			{
				IReadOnlyList<string> readOnlyList = GameMacroValidation.Validate(macro, adapter.Descriptor);
				if (readOnlyList.Count != 0 || macro.Target != adapter.Target)
				{
					status = RunStatus.Failed;
					limitation = ((readOnlyList.Count == 0) ? "Macro target does not match the connected game." : string.Join("; ", readOnlyList));
				}
				else if (!GameMacroTargetSafety.IsVerified(adapter.TargetVerification, adapter.Descriptor, adapter.Target))
				{
					status = RunStatus.Failed;
					limitation = "Connected adapter target/session verification is missing, expired or insufficient.";
				}
				else
				{
					await adapter.InitializeAsync(macro, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
					foreach (GameMacroStep step in macro.Steps)
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (!GameMacroTargetSafety.IsLiveProcessStillBound(adapter.TargetVerification, adapter.Descriptor, adapter.Target))
						{
							throw new InvalidOperationException("The verified game process exited or its path/start-time identity changed; no further macro action was dispatched.");
						}
						GameMacroStepResult gameMacroStepResult = await adapter.ExecuteAsync(macro, step, generation, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
						if (!string.Equals(gameMacroStepResult.StepId, step.Id, StringComparison.Ordinal))
						{
							status = RunStatus.Failed;
							limitation = "Adapter returned result for unexpected step '" + gameMacroStepResult.StepId + "'.";
							break;
						}
						stepResults.Add(gameMacroStepResult);
						if (cancellationToken.IsCancellationRequested)
						{
							status = RunStatus.Stopped;
							verdict = Verdict.Inconclusive;
							limitation = "Macro cancelled while an action was in flight; its returned result was retained, no assertion or later action was evaluated, and adapter cleanup was requested.";
							break;
						}
						if (!gameMacroStepResult.Succeeded)
						{
							status = RunStatus.Failed;
							limitation = gameMacroStepResult.Detail;
							break;
						}
						bool flag = false;
						foreach (GameMacroAssertion item in macro.Assertions.Where((GameMacroAssertion x) => x.AfterStepId == step.Id))
						{
							GameMacroAssertionResult gameMacroAssertionResult = Evaluate(item, gameMacroStepResult.Observations);
							assertionResults.Add(gameMacroAssertionResult);
							if (!gameMacroAssertionResult.Passed)
							{
								status = RunStatus.Completed;
								verdict = (gameMacroAssertionResult.Evaluated ? Verdict.Fail : Verdict.Inconclusive);
								limitation = gameMacroAssertionResult.Detail;
								flag = true;
								break;
							}
						}
						if (flag)
						{
							break;
						}
					}
					if (status == RunStatus.Running)
					{
						status = RunStatus.Completed;
						verdict = ((assertionResults.Count != macro.Assertions.Count || !assertionResults.All((GameMacroAssertionResult x) => x.Passed)) ? Verdict.Inconclusive : Verdict.Pass);
						if (verdict == Verdict.Inconclusive)
						{
							limitation = "One or more required assertions were not observed.";
						}
					}
				}
			}
			catch (OperationCanceledException)
			{
				status = RunStatus.Stopped;
				verdict = Verdict.Inconclusive;
				limitation = "Macro cancelled; no remaining steps were dispatched.";
			}
			catch (Exception ex2)
			{
				status = RunStatus.Failed;
				verdict = Verdict.Inconclusive;
				limitation = ex2.Message;
			}
		}
		finally
		{
			try
			{
				long cancellationGeneration = generation + 1;
				await adapter.StopAsync(cancellationGeneration, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex3)
			{
				status = RunStatus.Failed;
				verdict = Verdict.Inconclusive;
				limitation = (string.IsNullOrWhiteSpace(limitation) ? ("Adapter stop failed: " + ex3.Message) : (limitation + " Adapter stop failed: " + ex3.Message));
			}
		}
		return new GameMacroRunResult(status, verdict, stepResults, assertionResults, limitation);
	}

	private static GameMacroAssertionResult Evaluate(GameMacroAssertion assertion, IReadOnlyDictionary<string, string> observations)
	{
		if (!observations.TryGetValue(assertion.ObservationKey, out string value))
		{
			return new GameMacroAssertionResult(assertion.Id, Evaluated: false, Passed: false, $"No observation '{assertion.ObservationKey}' was recorded after step '{assertion.AfterStepId}'.");
		}
		if (string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase))
		{
			return new GameMacroAssertionResult(assertion.Id, Evaluated: false, Passed: false, $"Observation '{assertion.ObservationKey}' reported the missing-value sentinel '{"unknown"}' after step '{assertion.AfterStepId}'; the assertion cannot be evaluated.");
		}
		string @operator = assertion.Operator;
		bool flag;
		if ((@operator == "greater-than" || @operator == "less-than") ? true : false)
		{
			if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !double.IsFinite(result) || !double.TryParse(assertion.ExpectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) || !double.IsFinite(result2))
			{
				return new GameMacroAssertionResult(assertion.Id, Evaluated: false, Passed: false, "Numeric assertion '" + assertion.Id + "' received a non-finite or non-numeric value.");
			}
			flag = ((assertion.Operator == "greater-than") ? (result > result2) : (result < result2));
		}
		else
		{
			flag = ((assertion.Operator == "equals") ? string.Equals(value, assertion.ExpectedValue, StringComparison.Ordinal) : (!string.Equals(value, assertion.ExpectedValue, StringComparison.Ordinal)));
		}
		return new GameMacroAssertionResult(assertion.Id, Evaluated: true, flag, flag ? $"{assertion.ObservationKey} {assertion.Operator} {assertion.ExpectedValue} passed." : $"Expected {assertion.ObservationKey} {assertion.Operator} {assertion.ExpectedValue}; observed '{value}'.");
	}
}
