namespace EndermanQa.Desktop.Contracts;

public sealed record GameTarget(string TargetId, string GameId, string? Edition, string GameVersion, string RuntimeKind, string Ownership);
