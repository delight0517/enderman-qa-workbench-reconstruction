using System;
using System.Text.Json.Serialization;

namespace EndermanQa.Desktop.Scenarios;

public sealed record GameTargetVerification(bool IsVerified, string GameId, string TargetId, string EvidenceKind, DateTimeOffset VerifiedAt, bool IsTaskOwned, bool IsUnmirrored, int? ProcessId, string EvidenceSha256)
{
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ProcessImagePath { get; init; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ProcessImageSha256 { get; init; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public DateTimeOffset? ProcessStartedAtUtc { get; init; }
}
