using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace EndermanQa.Desktop.Scenarios;

public sealed class ProjectQaMacroStore
{
	public const int ContractVersion = 1;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	

	private string MacroRoot => Path.Combine(_dataRoot, "macros");

	private readonly string _dataRoot;

	public ProjectQaMacroStore(string projectDataRoot)
	{
		_dataRoot = Path.GetFullPath(projectDataRoot);
	}

	public IReadOnlyList<ProjectQaMacro> List(string planPath)
	{
		if (!Directory.Exists(MacroRoot))
		{
			return Array.Empty<ProjectQaMacro>();
		}
		RejectReparse(MacroRoot);
		List<ProjectQaMacro> list = new List<ProjectQaMacro>();
		foreach (string item in Directory.EnumerateFiles(MacroRoot, "*.json", System.IO.SearchOption.TopDirectoryOnly))
		{
			RejectReparse(item);
			list.Add(Load(Path.GetFileNameWithoutExtension(item), planPath));
		}
		return list.OrderBy<ProjectQaMacro, string>((ProjectQaMacro x) => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
	}

	public ProjectQaMacro Save(string name, IEnumerable<int> caseIds, string planPath)
	{
		string name2 = NormalizeName(name);
		int[] ids = caseIds.Distinct().Order().ToArray();
		int num = ids.Length;
		bool flag = ((num < 1 || num > 100) ? true : false);
		if (flag || ids.Any((int x) => (x < 1 || x > 100) ? true : false))
		{
			throw new InvalidDataException("매크로는 1~100개의 중복 없는 케이스 번호를 포함해야 합니다.");
		}
		ExpectedActionPlanCase[] array = (from x in new ExpectedActionPlanStore().Load(planPath)
			where ids.Contains(x.SourceId)
			select x).ToArray();
		if (array.Length != ids.Length)
		{
			throw new InvalidDataException("선택한 케이스가 현재 계획 파일에 모두 존재하지 않습니다.");
		}
		Directory.CreateDirectory(MacroRoot);
		RejectReparse(MacroRoot);
		ProjectQaMacro projectQaMacro = new ProjectQaMacro(1, Guid.NewGuid().ToString("N"), name2, DateTimeOffset.UtcNow, HashFile(planPath), ids, array);
		WriteNew(PathFor(projectQaMacro.Id), projectQaMacro);
		return projectQaMacro;
	}

	public ProjectQaMacro Load(string id, string planPath)
	{
		if (!Guid.TryParseExact(id, "N", out var _))
		{
			throw new InvalidDataException("매크로 ID 형식이 올바르지 않습니다.");
		}
		string path = PathFor(id);
		RejectReparse(MacroRoot);
		RejectReparse(path);
		ProjectQaMacro projectQaMacro = JsonSerializer.Deserialize<ProjectQaMacro>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("매크로 파일이 비어 있거나 손상되었습니다.");
		if (projectQaMacro.ContractVersion != 1 || projectQaMacro.Id != id || projectQaMacro.Cases.Count != projectQaMacro.CaseIds.Count || projectQaMacro.CaseIds.Distinct().Count() != projectQaMacro.CaseIds.Count || projectQaMacro.CaseIds.Any((int x) => (x < 1 || x > 100) ? true : false))
		{
			throw new InvalidDataException("지원하지 않는 매크로 계약 또는 케이스 목록입니다.");
		}
		if (!HashFile(planPath).Equals(projectQaMacro.PlanSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("케이스 계획이 저장 당시와 달라 매크로 실행을 거부했습니다. 현재 계획으로 새 매크로를 저장하세요.");
		}
		Dictionary<int, ExpectedActionPlanCase> dictionary = new ExpectedActionPlanStore().Load(planPath).ToDictionary((ExpectedActionPlanCase x) => x.SourceId);
		JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		for (int i = 0; i < projectQaMacro.Cases.Count; i++)
		{
			ExpectedActionPlanCase expectedActionPlanCase = projectQaMacro.Cases[i];
			if (expectedActionPlanCase.SourceId != projectQaMacro.CaseIds[i] || !dictionary.TryGetValue(expectedActionPlanCase.SourceId, out var value) || JsonSerializer.Serialize(expectedActionPlanCase, options) != JsonSerializer.Serialize(value, options))
			{
				throw new InvalidDataException($"매크로의 #{expectedActionPlanCase.SourceId:000} 계획이 현재 프로젝트의 공식 계획과 일치하지 않습니다.");
			}
		}
		return projectQaMacro;
	}

	public void Delete(string id)
	{
		if (!Guid.TryParseExact(id, "N", out var _))
		{
			throw new InvalidDataException("매크로 ID 형식이 올바르지 않습니다.");
		}
		RejectReparse(MacroRoot);
		string text = PathFor(id);
		RejectReparse(text);
		if (File.Exists(text))
		{
			FileSystem.DeleteFile(text, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
		}
	}

	public void AppendRunReceipt(ProjectQaMacro macro, string suitePath, string status)
	{
		Directory.CreateDirectory(MacroRoot);
		RejectReparse(MacroRoot);
		string text = JsonSerializer.Serialize(new
		{
			contractVersion = 1,
			macroId = macro.Id,
			Name = macro.Name,
			PlanSha256 = macro.PlanSha256,
			caseIds = macro.CaseIds,
			suitePath = Path.GetFullPath(suitePath),
			status = status,
			runAt = DateTimeOffset.UtcNow
		});
		File.AppendAllText(Path.Combine(MacroRoot, "run-history.jsonl"), text + Environment.NewLine);
	}

	private string PathFor(string id)
	{
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(MacroRoot).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string value = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		string fullPath = Path.GetFullPath(Path.Combine(MacroRoot, id + ".json"));
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new UnauthorizedAccessException("매크로 경로가 프로젝트 경계를 벗어났습니다.");
		}
		return fullPath;
	}

	private static string NormalizeName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		int length = text.Length;
		bool flag = ((length < 1 || length > 80) ? true : false);
		if (flag || text.Any(char.IsControl))
		{
			throw new ArgumentException("매크로 이름은 1~80자여야 합니다.");
		}
		return text;
	}

	private static string HashFile(string path)
	{
		using FileStream source = File.OpenRead(Path.GetFullPath(path));
		return Convert.ToHexString(SHA256.HashData(source));
	}

	private static void WriteNew(string path, ProjectQaMacro macro)
	{
		string text = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				JsonSerializer.Serialize(fileStream, macro, JsonOptions);
				fileStream.Flush(flushToDisk: true);
			}
			File.Move(text, path, overwrite: false);
		}
		catch
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
			throw;
		}
	}

	private static void RejectReparse(string path)
	{
		if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new UnauthorizedAccessException("매크로 저장소의 심볼릭 링크/재분석 지점은 허용되지 않습니다.");
		}
	}
}
