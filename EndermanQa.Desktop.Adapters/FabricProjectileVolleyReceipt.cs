using System.Collections.Generic;

namespace EndermanQa.Desktop.Adapters;

public sealed record FabricProjectileVolleyReceipt(int ApiVersion, string FixtureContract, string Action, string RequestId, string EntityUuid, string ProjectileType, int ProjectileCount, IReadOnlyList<string> ProjectileUuids, IReadOnlyList<FabricPositionCoordinates> LaunchPositions, IReadOnlyList<FabricPositionCoordinates> InitialVelocities, float TargetHealthAtLaunch, long ServerTick, bool EntityAdded, string LatencyCoverage);
