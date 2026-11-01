namespace EndermanQa.Desktop.Integrations;

public sealed record JiraSubmissionEntry(string SubmissionId, string CaseId, string RunId, string IssueKey, string SubmittedAt);
