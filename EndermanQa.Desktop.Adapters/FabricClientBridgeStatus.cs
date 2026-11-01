namespace EndermanQa.Desktop.Adapters;

public sealed record FabricClientBridgeStatus(int ApiVersion, string AdapterId, string TargetId, string InstanceId, long ProcessId, string GameVersion, bool ConnectedToServer, bool PlayerAvailable, bool WorldAvailable, string Dimension, string CapturedAt, string Capture, bool PlayerUsingItem = false, int PlayerUsingItemTicks = 0, string MainHandItem = "", bool HasNormalArrowAmmo = false, string PlayerUuid = "");
