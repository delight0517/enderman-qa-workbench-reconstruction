using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Contracts;

public sealed record QaPlan(string PlanId, string ScenarioId, string ScenarioHash, string AdapterId, string TargetId, DateTimeOffset CreatedAt, int TimeoutSeconds, IReadOnlyList<PlannedStep> Steps, IReadOnlyList<string> Assertions, IReadOnlyList<string> EvidenceRequirements, string PlanHash, string ExpectedOutcome = "death");
