namespace EndermanQa.Desktop.ProjectData;

public sealed record ProjectStorageCategory(string Id, string Name, string RelativePath, int FileCount, long SizeBytes, bool Exists);
