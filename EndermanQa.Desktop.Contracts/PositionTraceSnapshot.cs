namespace EndermanQa.Desktop.Contracts;

public sealed record PositionTraceSnapshot(long GameTick, WorldPositionSnapshot Position, string CaptureSource, double? CloudHorizontalDistance = null, double? CloudRadius = null, bool? WithinCloudRadius = null);
