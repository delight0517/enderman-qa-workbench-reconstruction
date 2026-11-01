using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Scenarios;

public sealed record SuiteScenarioSelection(QaScenario Scenario, string Coverage, int RequiredRepeatTrials = 1);
