using System;
using EndermanQa.Desktop.Adapters;

namespace EndermanQa.Desktop.Scenarios;

public sealed record Tc014RelocationSmokeReceipt(string Format, string State, string Coverage, string InstanceId, int ServerProcessId, string? TargetId, string? EntityUuid, FabricActionResponse? Relocation, bool FixtureReleased, string? FailureType, string? FailureStage, DateTimeOffset StartedAt, DateTimeOffset CompletedAt);
