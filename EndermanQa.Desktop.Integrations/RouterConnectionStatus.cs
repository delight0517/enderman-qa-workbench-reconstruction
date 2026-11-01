using System.Collections.Generic;

namespace EndermanQa.Desktop.Integrations;

public sealed record RouterConnectionStatus(bool Connected, int? ApiVersion, string ProviderState, string EngineMode, bool InferenceAvailable, string Message, IReadOnlyList<string> RedactedProfiles);
