namespace EndermanQa.Desktop.Adapters;

public sealed record FabricBridgeStatus(int ApiVersion, string AdapterId, string TargetId, string GameVersion, string RuntimeKind, string Ownership, bool WorldMutable, long ServerTick, long ServerPid = 0L, string InstanceId = "");
