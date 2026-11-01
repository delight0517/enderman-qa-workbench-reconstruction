using System.Collections.Generic;

namespace EndermanQa.Desktop.Scenarios;

public sealed record ExpectedActionPlanCase(int SourceId, string CaseId, string Title, string Preconditions, IReadOnlyList<string> ExpectedActions, string ExpectedObservation, string PassCriteria, string RepeatCriteria, string EvidenceCriteria, string VisualCapture, string Capability, string PlanStatus);
