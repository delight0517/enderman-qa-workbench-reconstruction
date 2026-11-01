namespace EndermanQa.Desktop.Adapters;

public sealed record FabricClientEntityObservation(int EntityId, string EntityUuid, string EntityType, bool Alive, float Health, double X, double Y, double Z, string Dimension);
