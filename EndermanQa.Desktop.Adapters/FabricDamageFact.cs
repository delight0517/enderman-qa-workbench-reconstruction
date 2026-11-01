namespace EndermanQa.Desktop.Adapters;

public sealed record FabricDamageFact(string Cause, long GameTick, float BaseDamage, float DamageTaken, float HealthAfter, bool Blocked, string ObservedAt, string? AttackerUuid = null);
