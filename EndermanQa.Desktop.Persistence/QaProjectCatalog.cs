using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndermanQa.Desktop.Persistence;

public sealed class QaProjectCatalog
{
	private readonly string _appDataRoot;

	private string ProjectsRoot => Path.Combine(_appDataRoot, "projects");

	private string ActivePath => Path.Combine(_appDataRoot, "active-project.json");

	public QaProjectCatalog(string appDataRoot)
	{
		_appDataRoot = Path.GetFullPath(appDataRoot);
	}

	public IReadOnlyList<QaProjectDefinition> ListProjects()
	{
		if (!Directory.Exists(ProjectsRoot))
		{
			return Array.Empty<QaProjectDefinition>();
		}
		List<QaProjectDefinition> list = new List<QaProjectDefinition>();
		string[] files = Directory.GetFiles(ProjectsRoot, "project.json", SearchOption.AllDirectories);
		foreach (string path in files)
		{
			try
			{
				QaProjectDefinition qaProjectDefinition = JsonSerializer.Deserialize<QaProjectDefinition>(File.ReadAllText(path));
				if ((object)qaProjectDefinition != null && Directory.Exists(qaProjectDefinition.RootPath))
				{
					list.Add(qaProjectDefinition);
				}
			}
			catch (JsonException)
			{
			}
		}
		return list.OrderBy<QaProjectDefinition, string>((QaProjectDefinition x) => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
	}

	public QaProjectDefinition? GetActiveProject()
	{
		try
		{
			if (!File.Exists(ActivePath))
			{
				return null;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(ActivePath));
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement value;
			string activeId = (rootElement.TryGetProperty("projectId", out value) ? value.GetString() : (rootElement.TryGetProperty("ProjectId", out value) ? value.GetString() : null));
			return ListProjects().FirstOrDefault((QaProjectDefinition x) => x.ProjectId == activeId);
		}
		catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is KeyNotFoundException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	public void Activate(string projectId)
	{
		string projectId2 = projectId;
		QaProjectDefinition qaProjectDefinition = ListProjects().FirstOrDefault((QaProjectDefinition x) => x.ProjectId == projectId2) ?? throw new InvalidOperationException("선택한 QA 프로젝트가 존재하지 않습니다.");
		Directory.CreateDirectory(_appDataRoot);
		string text = ActivePath + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(new
		{
			projectId = qaProjectDefinition.ProjectId,
			activatedAt = DateTimeOffset.UtcNow
		}));
		File.Move(text, ActivePath, overwrite: true);
	}

	public QaProjectDefinition UpdateProjectPresentation(string projectId, string? gameTarget = null, string? description = null, string? thumbnailRelativePath = null, string? gameEngineId = null, string? gameProjectRoot = null)
	{
		string projectId2 = projectId;
		QaProjectDefinition qaProjectDefinition = ListProjects().FirstOrDefault((QaProjectDefinition x) => x.ProjectId == projectId2) ?? throw new InvalidOperationException("프로젝트를 찾을 수 없습니다.");
		QaProjectDefinition qaProjectDefinition2 = qaProjectDefinition with
		{
			GameTarget = (string.IsNullOrWhiteSpace(gameTarget) ? qaProjectDefinition.GameTarget : gameTarget.Trim().Substring(0, Math.Min(gameTarget.Trim().Length, 160))),
			Description = ((description == null) ? qaProjectDefinition.Description : description.Trim().Substring(0, Math.Min(description.Trim().Length, 1000))),
			ThumbnailRelativePath = (thumbnailRelativePath ?? qaProjectDefinition.ThumbnailRelativePath),
			GameEngineId = ((gameEngineId == null) ? qaProjectDefinition.GameEngineId : NormalizeEngineId(gameEngineId)),
			GameProjectRoot = ((gameProjectRoot == null) ? qaProjectDefinition.GameProjectRoot : NormalizeGameProjectRoot(gameProjectRoot))
		};
		SaveProjectDefinition(qaProjectDefinition, qaProjectDefinition2);
		return qaProjectDefinition2;
	}

	private static string? NormalizeGameProjectRoot(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string fullPath = Path.GetFullPath(value.Trim());
		if (!Directory.Exists(fullPath))
		{
			throw new DirectoryNotFoundException("게임 프로젝트 폴더가 존재하지 않습니다.");
		}
		return fullPath;
	}

	public QaProjectDefinition UpdateProjectGameContext(string projectId, string? gameTarget, string? gameVersionMemo)
	{
		string projectId2 = projectId;
		QaProjectDefinition qaProjectDefinition = ListProjects().FirstOrDefault((QaProjectDefinition x) => x.ProjectId == projectId2) ?? throw new InvalidOperationException("프로젝트를 찾을 수 없습니다.");
		string text = (string.IsNullOrWhiteSpace(gameTarget) ? qaProjectDefinition.GameTarget : gameTarget.Trim());
		string text2 = (gameVersionMemo ?? string.Empty).Trim();
		QaProjectDefinition qaProjectDefinition2 = qaProjectDefinition with
		{
			GameTarget = text?.Substring(0, Math.Min(text.Length, 160)),
			GameVersionMemo = text2.Substring(0, Math.Min(text2.Length, 160))
		};
		SaveProjectDefinition(qaProjectDefinition, qaProjectDefinition2);
		return qaProjectDefinition2;
	}

	public QaProjectDefinition UpdateProjectEngine(string projectId, string? engineId)
	{
		string projectId2 = projectId;
		QaProjectDefinition? obj = ListProjects().FirstOrDefault((QaProjectDefinition x) => x.ProjectId == projectId2) ?? throw new InvalidOperationException("프로젝트를 찾을 수 없습니다.");
		QaProjectDefinition qaProjectDefinition = obj with
		{
			GameEngineId = NormalizeEngineId(engineId)
		};
		SaveProjectDefinition(obj, qaProjectDefinition);
		return qaProjectDefinition;
	}

	public static string NormalizeEngineId(string? value)
	{
		string text = (value ?? string.Empty).Trim().ToLowerInvariant();
		if (text != null)
		{
			int length = text.Length;
			if (length <= 6)
			{
				if (length != 5)
				{
					if (length == 6)
					{
						char c = text[0];
						if (c != 'c')
						{
							if (c == 'u' && text == "unreal")
							{
								goto IL_00f2;
							}
						}
						else if (text == "custom")
						{
							return "custom";
						}
					}
				}
				else
				{
					switch (text[0])
					{
					case 'u':
						if (!(text == "unity"))
						{
							break;
						}
						return "unity";
					case 'g':
						if (!(text == "godot"))
						{
							break;
						}
						return "godot";
					}
				}
			}
			else if (length != 13)
			{
				if (length != 14)
				{
					if (length == 22 && text == "minecraft java edition")
					{
						goto IL_0102;
					}
				}
				else if (text == "minecraft java")
				{
					goto IL_0102;
				}
			}
			else if (text == "unreal engine")
			{
				goto IL_00f2;
			}
		}
		return "unknown";
		IL_00f2:
		return "unreal";
		IL_0102:
		return "minecraft-java";
	}

	public static string GetProjectEngineId(QaProjectDefinition? project)
	{
		if ((object)project == null)
		{
			return "unknown";
		}
		if (string.IsNullOrWhiteSpace(project.GameEngineId))
		{
			string? gameTarget = project.GameTarget;
			if (gameTarget != null && gameTarget.Contains("Minecraft Java", StringComparison.OrdinalIgnoreCase))
			{
				return "minecraft-java";
			}
		}
		return NormalizeEngineId(project.GameEngineId);
	}

	private static void SaveProjectDefinition(QaProjectDefinition project, QaProjectDefinition updated)
	{
		string text = Path.Combine(project.RootPath, "project.json");
		string text2 = text + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				JsonSerializer.Serialize(fileStream, updated, new JsonSerializerOptions
				{
					WriteIndented = true
				});
				fileStream.Flush(flushToDisk: true);
			}
			File.Move(text2, text, overwrite: true);
		}
		catch
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
			throw;
		}
	}

	public QaProjectDefinition CreateProject(string name, string? caseCatalogSource, string? jiraSite = null, string? jiraProjectName = null, string? jiraProjectKey = null, IReadOnlyList<string>? jiraIssueTypes = null, string? gameTarget = "Minecraft Java Edition", string? description = null, string? gameEngineId = null, string? gameProjectRoot = null)
	{
		string name2 = name;
		if (string.IsNullOrWhiteSpace(name2))
		{
			throw new ArgumentException("프로젝트 이름을 입력하세요.");
		}
		if (!string.IsNullOrWhiteSpace(caseCatalogSource))
		{
			if (!File.Exists(caseCatalogSource))
			{
				throw new FileNotFoundException("선택한 테스트 케이스 원본을 찾을 수 없습니다.", caseCatalogSource);
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(caseCatalogSource));
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array || jsonDocument.RootElement.GetArrayLength() != 100)
			{
				throw new InvalidDataException("Enderman 100 TC 템플릿은 정확히 100개 케이스가 필요합니다.");
			}
		}
		QaProjectDefinition qaProjectDefinition = ListProjects().FirstOrDefault((QaProjectDefinition x) => x.Name.Equals(name2.Trim(), StringComparison.CurrentCultureIgnoreCase));
		if ((object)qaProjectDefinition != null)
		{
			bool flag = File.Exists(Path.Combine(qaProjectDefinition.RootPath, "case-catalog-100.json")) || File.Exists(Path.Combine(qaProjectDefinition.DataRoot, "case-catalog-100.json"));
			bool flag2 = !string.IsNullOrWhiteSpace(caseCatalogSource);
			string b = (string.IsNullOrWhiteSpace(gameTarget) ? "Minecraft Java Edition" : gameTarget.Trim());
			string text = NormalizeEngineId(gameEngineId);
			string b2 = ((gameProjectRoot == null) ? null : NormalizeGameProjectRoot(gameProjectRoot));
			if (!string.Equals(qaProjectDefinition.GameTarget, b, StringComparison.OrdinalIgnoreCase) || flag != flag2 || NormalizeEngineId(qaProjectDefinition.GameEngineId) != text || !string.Equals(qaProjectDefinition.GameProjectRoot, b2, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"‘{qaProjectDefinition.Name}’ 이름은 이미 {qaProjectDefinition.GameTarget} / {(flag ? "Enderman 100 TC" : "일반 QA")} 프로젝트에서 사용 중입니다. 다른 이름을 사용하거나 기존 프로젝트를 직접 여세요.");
			}
			Activate(qaProjectDefinition.ProjectId);
			return qaProjectDefinition;
		}
		string text2 = Regex.Replace(name2.Trim().ToLowerInvariant(), "[^\\p{L}\\p{Nd}]+", "-").Trim('-');
		if (string.IsNullOrWhiteSpace(text2))
		{
			text2 = "qa-project-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		}
		string text3 = Path.Combine(ProjectsRoot, text2);
		if (Directory.Exists(text3))
		{
			text3 = Path.Combine(ProjectsRoot, text2 + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
		}
		string text4 = Path.Combine(text3, "data");
		Directory.CreateDirectory(text4);
		QaProjectDefinition qaProjectDefinition2 = new QaProjectDefinition(Path.GetFileName(text3), name2.Trim(), text3, text4, DateTimeOffset.UtcNow, jiraSite, jiraProjectName, jiraProjectKey, jiraIssueTypes, string.IsNullOrWhiteSpace(gameTarget) ? "Minecraft Java Edition" : gameTarget.Trim(), description?.Trim(), null, null, NormalizeEngineId(gameEngineId), (gameProjectRoot == null) ? null : NormalizeGameProjectRoot(gameProjectRoot));
		File.WriteAllText(Path.Combine(text3, "project.json"), JsonSerializer.Serialize(qaProjectDefinition2, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
		if (!string.IsNullOrWhiteSpace(caseCatalogSource))
		{
			File.Copy(caseCatalogSource, Path.Combine(text3, "case-catalog-100.json"));
			File.Copy(caseCatalogSource, Path.Combine(text4, "case-catalog-100.json"));
		}
		string value = (string.IsNullOrWhiteSpace(caseCatalogSource) ? "Generic game QA workspace. No Enderman cases or game-specific tests were seeded. Add manual cases or install a verified adapter for the selected game." : "Enderman death-behavior test project with its 100-case catalog.");
		File.WriteAllText(Path.Combine(text3, "QA-TARGET.md"), $"# {qaProjectDefinition2.Name}\n\n{value}\n\n- Game/app target: {qaProjectDefinition2.GameTarget}.\n- Engine/tool family: {qaProjectDefinition2.GameEngineId}.\n- Game project source (read-only registration): {qaProjectDefinition2.GameProjectRoot ?? "not registered"}.\n- Description: {qaProjectDefinition2.Description ?? "(not set)"}.\n- Choose a disposable project-owned instance before runtime QA.\n- Jira destination: {jiraSite ?? "not configured"} / {jiraProjectName ?? ""} ({jiraProjectKey ?? ""}).\n- Jira issue types: {string.Join(", ", jiraIssueTypes ?? Array.Empty<string>())}.\n- Jira access metadata is configuration only and has not been verified. Credentials are not included here.\n- Project records, run evidence, and Jira submissions are created only inside this project's `data/` folder.\n");
		File.Copy(Path.Combine(AppContext.BaseDirectory, "QA-EXECUTION-POLICY.json"), Path.Combine(text3, "QA-EXECUTION-POLICY.json"));
		new ProjectLibraryStore().InitializeProjectFolders(qaProjectDefinition2);
		Activate(qaProjectDefinition2.ProjectId);
		return qaProjectDefinition2;
	}
}
