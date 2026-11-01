namespace EndermanQa.Desktop.Integrations;

public sealed record RouterDiscoveryStatus(bool Connected, bool ApiCompatible, int? ApiVersion, string PackageVersion, string Endpoint, string ProviderState, string Message);
