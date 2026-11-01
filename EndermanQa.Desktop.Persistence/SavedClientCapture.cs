using System;

namespace EndermanQa.Desktop.Persistence;

public sealed record SavedClientCapture(string RunId, string RelativePngPath, string RelativeSidecarPath, long Bytes, string Sha256, int Width, int Height, string GameVersion, string Dimension, long GameTick, long ProcessId, string InstanceId, string TargetId, DateTimeOffset CapturedAt);
