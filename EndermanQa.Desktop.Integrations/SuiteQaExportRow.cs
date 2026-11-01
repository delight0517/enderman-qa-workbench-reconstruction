namespace EndermanQa.Desktop.Integrations;

public sealed record SuiteQaExportRow(string CaseNumber, string CaseId, string Title, string Status, string Verdict, string Preconditions, string ExpectedActions, string ExpectedObservation, string PassCriteria, string Actual, string Coverage, string Attempts, string Repeatability, string Limitation, string CaptureStatus, string SourceCandidate, string SuiteId, string JournalPath, string MediaPaths = "", string FollowUpGuidance = "");
