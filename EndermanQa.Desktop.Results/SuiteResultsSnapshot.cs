using System.Collections.Generic;

namespace EndermanQa.Desktop.Results;

public sealed record SuiteResultsSnapshot(IReadOnlyList<SuiteCaseRow> Cases, int UpdatedReceiptFiles, string Summary, string? Notice);
