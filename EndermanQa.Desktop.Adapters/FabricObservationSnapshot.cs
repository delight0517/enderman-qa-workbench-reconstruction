using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Adapters;

public sealed record FabricObservationSnapshot(FabricBridgeStatus Status, IReadOnlyList<FabricEndermanObservation> Endermen, IReadOnlyList<FabricDeathFact> Deaths, DateTimeOffset CollectedAt, string Limitation);
