namespace EndermanQa.Desktop.Adapters;

public sealed record FabricDeathFact(string EntityUuid, string Cause, long GameTick, string ObservedAt, string? AttackerUuid);
