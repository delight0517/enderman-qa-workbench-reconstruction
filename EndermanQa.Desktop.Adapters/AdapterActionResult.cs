using System.Collections.Generic;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Adapters;

public sealed record AdapterActionResult(string StepId, string Action, bool Succeeded, string Detail, EntitySnapshot? Entity, IReadOnlyList<QaEvent> Events, bool VisualCaptureAvailable);
