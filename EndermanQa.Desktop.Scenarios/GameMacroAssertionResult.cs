namespace EndermanQa.Desktop.Scenarios;

public sealed record GameMacroAssertionResult(string AssertionId, bool Evaluated, bool Passed, string Detail);
