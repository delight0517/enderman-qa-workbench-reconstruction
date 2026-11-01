using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Scenarios;

public sealed record ProjectQaMacro(int ContractVersion, string Id, string Name, DateTimeOffset CreatedAt, string PlanSha256, IReadOnlyList<int> CaseIds, IReadOnlyList<ExpectedActionPlanCase> Cases);
