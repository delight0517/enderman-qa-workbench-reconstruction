namespace EndermanQa.Desktop.Scenarios;

public sealed record GameMacroAssertion(string Id, string AfterStepId, string ObservationKey, string Operator, string ExpectedValue);
