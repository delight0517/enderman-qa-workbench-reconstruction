using System.Collections.Generic;

namespace EndermanQa.Desktop.Scenarios;

public sealed record GameToolDescriptor(string AdapterId, int ContractVersion, string GameId, string DisplayName, string Engine, string Availability, bool RequiresOwnedProcess, bool RequiresUnmirroredDisplay, IReadOnlySet<string> Actions, IReadOnlySet<string> ObservationKeys)
{
	public string CapabilitySha256 => GameMacroValidation.ComputeCapabilityHash(this);
}
