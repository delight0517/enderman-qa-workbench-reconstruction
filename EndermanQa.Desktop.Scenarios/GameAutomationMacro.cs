using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Scenarios;

public sealed record GameAutomationMacro(int ContractVersion, string Id, string Name, DateTimeOffset CreatedAt, GameAutomationTarget Target, string AdapterId, int AdapterContractVersion, string CapabilitySha256, IReadOnlyList<GameMacroStep> Steps, IReadOnlyList<GameMacroAssertion> Assertions, string ContentSha256);
