namespace EndermanQa.Desktop.Adapters;

public sealed record FabricSweepTargetObservation(string Role, string Uuid, float BeforeHealth, float AfterHealth, int DamageEventDelta, int PlayerDamageEventDelta, string AttackerUuid, bool AttackerMatches, long GameTick, long AttackTick, double Distance, bool Alive);
