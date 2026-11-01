using System.Collections.Generic;

namespace EndermanQa.Desktop.ProjectData;

public sealed record ProjectStorageReport(string ProjectRoot, long TotalBytes, int TotalFiles, bool Complete, IReadOnlyList<ProjectStorageCategory> Categories, IReadOnlyList<string> Warnings);
