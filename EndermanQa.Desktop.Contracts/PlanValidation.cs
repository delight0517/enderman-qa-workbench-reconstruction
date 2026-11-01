using System.Collections.Generic;

namespace EndermanQa.Desktop.Contracts;

public sealed record PlanValidation(bool IsValid, IReadOnlyList<string> Errors);
