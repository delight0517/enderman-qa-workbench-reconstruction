using System.Collections.Generic;

namespace EndermanQa.Desktop.Contracts;

public sealed record ScenarioStep(string Id, string Action, IReadOnlyDictionary<string, string> Arguments);
