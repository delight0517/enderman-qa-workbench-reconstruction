using System.Collections.Generic;

namespace EndermanQa.Desktop.Contracts;

public sealed record AdapterCapabilities(string AdapterId, string ContractVersion, IReadOnlySet<string> Actions, bool AuthoritativeEntityState, bool Screenshot, bool Video);
