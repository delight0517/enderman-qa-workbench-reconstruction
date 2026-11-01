using System;

namespace EndermanQa.Desktop.Contracts;

public sealed record QaEvent(string RunId, long Sequence, string Kind, string? EntityRunId, DateTimeOffset ObservedAt, long? GameTick, string Detail, string Provenance);
