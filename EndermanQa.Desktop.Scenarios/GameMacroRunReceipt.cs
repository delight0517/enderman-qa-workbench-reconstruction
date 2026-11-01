using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Scenarios;

public sealed record GameMacroRunReceipt(int ContractVersion, string RunId, string MacroId, string MacroName, string MacroContentSha256, GameAutomationTarget Target, string AdapterId, int AdapterContractVersion, string CapabilitySha256, GameTargetVerification? TargetVerification, int Attempt, int RequestedRepeatCount, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, GameMacroRunResult Result, string ReceiptSha256)
{
	public IReadOnlyList<GameMacroEvidenceReceipt> EvidenceManifest { get; init; } = Array.Empty<GameMacroEvidenceReceipt>();


	public string? EvidenceIntegrityIssue { get; init; }
}
