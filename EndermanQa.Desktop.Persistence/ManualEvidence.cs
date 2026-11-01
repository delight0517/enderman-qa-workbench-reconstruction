using System;

namespace EndermanQa.Desktop.Persistence;

public sealed record ManualEvidence(string FileName, string RelativePath, string MediaType, long SizeBytes, string Sha256, DateTimeOffset AddedAt);
