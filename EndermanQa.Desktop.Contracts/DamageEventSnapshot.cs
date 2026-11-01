using System;

namespace EndermanQa.Desktop.Contracts;

public sealed record DamageEventSnapshot(string Cause, long GameTick, double BaseDamage, double DamageTaken, double HealthAfter, bool Blocked, DateTimeOffset ObservedAt, string? AttackerUuid = null);
