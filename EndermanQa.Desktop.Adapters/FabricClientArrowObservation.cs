using System.Collections.Generic;

namespace EndermanQa.Desktop.Adapters;

public sealed record FabricClientArrowObservation(string ProjectileUuid, string OwnerUuid, string ProjectileType, long FirstSeenTick, long LastSeenTick, bool SampleOverflow, IReadOnlyList<FabricClientArrowSample> Observations);
