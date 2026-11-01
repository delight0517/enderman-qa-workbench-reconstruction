using System.Collections.Generic;

namespace EndermanQa.Desktop.Contracts;

public sealed record PlannedStep(int Order, string StepId, string Action, IReadOnlyDictionary<string, string> Arguments);
