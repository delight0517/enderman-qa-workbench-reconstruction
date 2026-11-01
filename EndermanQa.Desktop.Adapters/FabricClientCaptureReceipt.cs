namespace EndermanQa.Desktop.Adapters;

public sealed record FabricClientCaptureReceipt(int ApiVersion, string RequestId, string State, string? FileName, int? FramebufferWidth, int? FramebufferHeight, long? Bytes, string? Sha256, string? Error, string? CreatedAt);
