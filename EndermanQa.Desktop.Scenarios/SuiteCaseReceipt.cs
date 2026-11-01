using System.Collections.Generic;

namespace EndermanQa.Desktop.Scenarios;

public sealed record SuiteCaseReceipt(int SourceId, string CaseId, string Title, string Status, string Verdict, string Coverage, string? FirstRunId, IReadOnlyList<string> RepeatRunIds, string? Repeatability, string? Signature, string? Limitation, string CaptureStatus, string? JournalPath);
