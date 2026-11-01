namespace EndermanQa.Desktop.Adapters;

public sealed record FabricPositionSample(long GameTick, double X, double Y, double Z, string Dimension, string CaptureSource, double? CloudHorizontalDistance = null, double? CloudRadius = null, bool? WithinCloudRadius = null);
