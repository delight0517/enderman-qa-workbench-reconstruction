using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Results;

internal sealed record SuiteResultReceipt(int SourceId, string CaseId, string Title, string Status, string Verdict, string Coverage, string ActualObservation, string? FirstRunId, IReadOnlyList<string> RepeatRunIds, string Repeatability, string Signature, string Limitation, string CaptureStatus, string JournalPath, string SourceCandidate, DateTimeOffset? RecordedAt, string SuiteId, string SuiteDirectory);
