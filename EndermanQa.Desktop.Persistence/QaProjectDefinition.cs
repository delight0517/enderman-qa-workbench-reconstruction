using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Persistence;

public sealed record QaProjectDefinition(string ProjectId, string Name, string RootPath, string DataRoot, DateTimeOffset CreatedAt, string? JiraSite = null, string? JiraProjectName = null, string? JiraProjectKey = null, IReadOnlyList<string>? JiraIssueTypes = null, string? GameTarget = "Minecraft Java Edition", string? Description = null, string? ThumbnailRelativePath = null, string? GameVersionMemo = null, string? GameEngineId = null, string? GameProjectRoot = null);
