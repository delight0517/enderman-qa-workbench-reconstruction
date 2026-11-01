using System.Collections.Generic;

namespace EndermanQa.Desktop.Adapters;

public sealed record FabricEndermanObservation(string Uuid, int EntityId, string EntityType, string LifeState, float Health, double X, double Y, double Z, string Dimension, long GameTick, string ObservedAt, string? DamageCause, int? FrozenTicks = null, int? FireTicks = null, IReadOnlyList<FabricDamageFact>? DamageEvents = null, IReadOnlyList<FabricPositionSample>? PositionTrace = null);
