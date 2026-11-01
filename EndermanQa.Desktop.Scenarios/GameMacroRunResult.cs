using System.Collections.Generic;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Scenarios;

public sealed record GameMacroRunResult(RunStatus Status, Verdict Verdict, IReadOnlyList<GameMacroStepResult> Steps, IReadOnlyList<GameMacroAssertionResult> Assertions, string Limitation);
