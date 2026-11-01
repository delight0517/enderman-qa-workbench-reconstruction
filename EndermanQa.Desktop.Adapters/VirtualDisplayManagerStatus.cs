namespace EndermanQa.Desktop.Adapters;

public sealed record VirtualDisplayManagerStatus(int Version, string RequestId, string State, string? RunId, int IdleSeconds, int IdleTimeoutSeconds, bool DisplayEnabled, string? ErrorCode);
