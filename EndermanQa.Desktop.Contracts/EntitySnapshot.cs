using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Contracts;

public sealed record EntitySnapshot(string RunEntityId, string? NativeId, string EntityType, EntityLifeState LifeState, double? Health, double? MaxHealth, string? DamageCause, long? GameTick, DateTimeOffset ObservedAt, string Provenance, WorldPositionSnapshot? Position = null, int? FrozenTicks = null, int? FireTicks = null, IReadOnlyList<DamageEventSnapshot>? DamageEvents = null, string? LastAttackerUuid = null, IReadOnlyList<PositionTraceSnapshot>? PositionTrace = null);
