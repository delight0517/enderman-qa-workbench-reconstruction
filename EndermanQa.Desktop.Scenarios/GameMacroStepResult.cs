using System.Collections.Generic;

namespace EndermanQa.Desktop.Scenarios;

public sealed record GameMacroStepResult(string StepId, bool Succeeded, string Detail, IReadOnlyDictionary<string, string> Observations, IReadOnlyList<string> EvidenceReferences);
