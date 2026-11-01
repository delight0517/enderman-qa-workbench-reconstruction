using System.Collections.Generic;

namespace EndermanQa.Desktop.Adapters;

public sealed record FabricClientArrowSessionSnapshot(int ApiVersion, string Schema, string SessionId, string OwnerUuid, string TargetUuid, string State, long StartedTick, long LastSampleTick, long? EndedTick, bool Overflow, int ProjectileCount, IReadOnlyList<FabricClientArrowObservation> Projectiles);
