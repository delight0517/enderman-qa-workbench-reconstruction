using System.Collections.Generic;

namespace EndermanQa.Desktop.Integrations;

public sealed record AiScenarioDraft(string Preconditions, IReadOnlyList<string> Steps, string Expected, string ProviderModel);
