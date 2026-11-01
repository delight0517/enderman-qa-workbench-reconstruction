using System.Collections.Generic;

namespace EndermanQa.Desktop.Integrations;

public sealed record JiraQaCandidate(string SubmissionId, string CaseId, string RunId, string Title, string IssueType, string Description, IReadOnlyList<string> Attachments, string SourceLabel);
