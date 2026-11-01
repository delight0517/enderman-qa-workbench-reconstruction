using System.Collections.Generic;

namespace EndermanQa.Desktop.Results;

public sealed record SuiteCaseDetail(SuiteCaseRow Case, IReadOnlyList<SuiteAttempt> Attempts);
