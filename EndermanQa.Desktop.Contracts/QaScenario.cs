using System.Collections.Generic;

namespace EndermanQa.Desktop.Contracts;

public sealed record QaScenario(string Id, int Version, string Title, string Objective, IReadOnlyList<string> RequiredCapabilities, IReadOnlyList<ScenarioStep> Steps, int TimeoutSeconds, string ExpectedOutcome = "death");
