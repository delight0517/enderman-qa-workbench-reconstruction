using System.Collections.Generic;

namespace EndermanQa.Desktop.Results;

public sealed record WorkbookSheetData(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, bool Truncated);
