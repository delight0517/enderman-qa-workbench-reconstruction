using System.Collections.Generic;

namespace EndermanQa.Desktop.Scenarios;

public sealed record GameMacroStep(string Id, string Action, IReadOnlyDictionary<string, string> Arguments);
