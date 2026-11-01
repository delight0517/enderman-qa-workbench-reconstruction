using System.Collections.Generic;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Adapters;

public sealed record ScenarioRunResult(RunStatus Status, EntitySnapshot? Before, EntitySnapshot? After, IReadOnlyList<AdapterActionResult> Actions, Verdict Verdict, string Limitation);
