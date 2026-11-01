namespace EndermanQa.Desktop.Adapters;

public sealed record FabricDeathArtifactObservation(string NearestDeathEntityUuid, string ArtifactUuid, string ArtifactType, string ItemId, int ItemCount, int ExperienceValue, double X, double Y, double Z, long GameTick, string CorrelationMethod);
