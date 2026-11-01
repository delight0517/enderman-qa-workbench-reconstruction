using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Persistence;

public sealed record ManualQaRecord(string CaseId, string Title, string Target, string GameVersion, string Preconditions, string Steps, string Expected, string Actual, string Verdict, string Notes, DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, IReadOnlyList<ManualEvidence> Evidence);
