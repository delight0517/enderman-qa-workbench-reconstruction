namespace EndermanQa.Desktop.Integrations;

public sealed record QaExportRow(string CaseId, string Title, string Target, string Version, string Expected, string Actual, string Verdict, string Evidence, string RunId);
