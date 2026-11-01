using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndermanQa.Desktop.Tools;

public sealed class ToolWorkspaceStore
{
	public sealed record GeneratedToolFile(string WorkspaceId, string RelativePath, string FullPath, string Sha256, string Target, string Profile);

	private sealed record Manifest(int ContractVersion, string WorkspaceId, string Target, string Profile, DateTimeOffset CreatedAt, string SourceResponseSha256, IReadOnlyList<ManifestFile> Files);

	private sealed record ManifestFile(string RelativePath, string Sha256, int Length);

	public const int ContractVersion = 1;

	private const int MaximumFiles = 20;

	private const int MaximumFileBytes = 131072;

	private const long MaximumTotalBytes = 1048576L;

	private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".cs", ".csproj", ".sln", ".json", ".md", ".txt", ".js", ".ts", ".html", ".css",
		".py", ".java", ".kt", ".lua", ".xml", ".gradle", ".properties", ".toml", ".yaml", ".yml"
	};

	private static readonly Regex FileBlock = new Regex("(?m)^###\\s+FILE:\\s*(?<path>[^\\r\\n]+)\\r?\\n```[^\\r\\n]*\\r?\\n(?<content>.*?)\\r?\\n```(?=\\s*(?:\\r?\\n|$))", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	private readonly string _root;

	private string RootWithSeparator
	{
		get
		{
			ReadOnlySpan<char> readOnlySpan = _root.TrimEnd(Path.DirectorySeparatorChar);
			char reference = Path.DirectorySeparatorChar;
			return string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		}
	}

	public ToolWorkspaceStore(string projectDataRoot)
	{
		_root = Path.GetFullPath(Path.Combine(projectDataRoot, "tool-workspaces"));
	}

	public IReadOnlyList<GeneratedToolFile> CreateFromResponse(string target, string profile, string response)
	{
		string target2 = target;
		string profile2 = profile;
		if (string.IsNullOrWhiteSpace(target2) || target2.Length > 240)
		{
			throw new ArgumentException("대상 앱·게임 이름은 1~240자여야 합니다.", "target");
		}
		string text = profile2;
		if ((!(text == "tool-design") && !(text == "self-evolution")) || 1 == 0)
		{
			throw new ArgumentException("지원하지 않는 Tool Studio 프로필입니다.", "profile");
		}
		if (string.IsNullOrWhiteSpace(response) || response.Length > 500000)
		{
			throw new ArgumentException("AI 답변이 비어 있거나 허용 크기를 넘었습니다.", "response");
		}
		Match[] array = FileBlock.Matches(response).Cast<Match>().ToArray();
		if (array.Length == 0)
		{
			throw new InvalidDataException("응답에 생성할 코드 파일이 없습니다. AI에게 파일별 초안을 요청한 뒤 저장하세요.");
		}
		if (array.Length > 20)
		{
			throw new InvalidDataException($"한 번에 만들 수 있는 파일은 {20}개까지입니다.");
		}
		List<(string, byte[], string)> list = new List<(string, byte[], string)>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		long num = 0L;
		Match[] array2 = array;
		foreach (Match match in array2)
		{
			string text2 = ValidateRelativePath(match.Groups["path"].Value.Trim());
			if (!hashSet.Add(text2))
			{
				throw new InvalidDataException("중복 파일 경로가 있습니다: " + text2);
			}
			byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(match.Groups["content"].Value.TrimEnd('\r', '\n'));
			if (bytes.Length == 0 || bytes.Length > 131072)
			{
				throw new InvalidDataException($"파일 크기가 비어 있거나 상한({131072:N0} bytes)을 넘습니다: {text2}");
			}
			num += bytes.Length;
			if (num > 1048576)
			{
				throw new InvalidDataException($"전체 파일 크기가 상한({1048576:N0} bytes)을 넘습니다.");
			}
			list.Add((text2, bytes, Convert.ToHexString(SHA256.HashData(bytes))));
		}
		Directory.CreateDirectory(_root);
		RejectReparsePoint(_root);
		string workspaceId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		string text3 = Path.Combine(_root, ".stage-" + workspaceId);
		string published = Path.Combine(_root, workspaceId);
		Directory.CreateDirectory(text3);
		try
		{
			foreach (var item in list)
			{
				string path = ResolveInside(text3, item.Item1);
				string? directoryName = Path.GetDirectoryName(path);
				Directory.CreateDirectory(directoryName);
				RejectReparsePoint(directoryName);
				using FileStream fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				fileStream.Write(item.Item2);
				fileStream.Flush(flushToDisk: true);
			}
			Manifest value = new Manifest(1, workspaceId, target2.Trim(), profile2, DateTimeOffset.UtcNow, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response))), list.Select<(string, byte[], string), ManifestFile>(((string RelativePath, byte[] Bytes, string Sha256) file) => new ManifestFile(file.RelativePath, file.Sha256, file.Bytes.Length)).ToArray());
			File.WriteAllText(Path.Combine(text3, "workspace.json"), JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			Directory.Move(text3, published);
		}
		catch
		{
			if (Directory.Exists(text3) && Path.GetFullPath(text3).StartsWith(RootWithSeparator, StringComparison.OrdinalIgnoreCase))
			{
				Directory.Delete(text3, recursive: true);
			}
			throw;
		}
		return list.Select<(string, byte[], string), GeneratedToolFile>(((string RelativePath, byte[] Bytes, string Sha256) file) => new GeneratedToolFile(workspaceId, file.RelativePath, ResolveInside(published, file.RelativePath), file.Sha256, target2.Trim(), profile2)).ToArray();
	}

	public IReadOnlyList<GeneratedToolFile> ListFiles()
	{
		if (!Directory.Exists(_root))
		{
			return Array.Empty<GeneratedToolFile>();
		}
		RejectReparsePoint(_root);
		List<GeneratedToolFile> list = new List<GeneratedToolFile>();
		foreach (string item in Directory.EnumerateDirectories(_root, "*", SearchOption.TopDirectoryOnly))
		{
			if (Path.GetFileName(item).StartsWith(".stage-", StringComparison.Ordinal))
			{
				continue;
			}
			RejectReparsePoint(item);
			string text = Path.Combine(item, "workspace.json");
			if (!File.Exists(text))
			{
				continue;
			}
			try
			{
				RejectReparsePoint(text);
				if (new FileInfo(text).Length > 65536)
				{
					continue;
				}
				Manifest manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(text), JsonOptions);
				if ((object)manifest == null || manifest.ContractVersion != 1 || manifest.Files.Count > 20 || !Path.GetFileName(item).Equals(manifest.WorkspaceId, StringComparison.Ordinal))
				{
					continue;
				}
				foreach (ManifestFile file in manifest.Files)
				{
					string text2 = ResolveInside(item, ValidateRelativePath(file.RelativePath));
					if (File.Exists(text2))
					{
						list.Add(new GeneratedToolFile(manifest.WorkspaceId, file.RelativePath, text2, file.Sha256, manifest.Target, manifest.Profile));
					}
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is InvalidDataException || ex is ArgumentException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
			}
		}
		return list.OrderBy<GeneratedToolFile, string>((GeneratedToolFile x) => x.WorkspaceId, StringComparer.Ordinal).ThenBy<GeneratedToolFile, string>((GeneratedToolFile x) => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public string Read(GeneratedToolFile file)
	{
		string fullPath = Path.GetFullPath(file.FullPath);
		if (!fullPath.StartsWith(RootWithSeparator, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("파일이 현재 프로젝트의 도구 작업 폴더 밖에 있습니다.");
		}
		RejectReparsePoint(fullPath);
		if (new FileInfo(fullPath).Length > 131072)
		{
			throw new InvalidDataException("미리보기 파일이 허용 크기를 초과했습니다.");
		}
		byte[] array = File.ReadAllBytes(fullPath);
		if (array.Length > 131072 || !Convert.ToHexString(SHA256.HashData(array)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("생성 이후 파일이 변경되었거나 상한을 초과했습니다. manifest와 실제 파일이 일치하지 않습니다.");
		}
		return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(array);
	}

	private string ValidateRelativePath(string relative)
	{
		if (string.IsNullOrWhiteSpace(relative) || relative.Length > 240 || relative.Contains(':') || Path.IsPathRooted(relative))
		{
			throw new InvalidDataException("파일 경로는 프로젝트 내 상대 경로여야 합니다.");
		}
		string[] array = relative.Replace('\\', '/').Split('/');
		if (array.Any(delegate(string part)
		{
			bool flag = string.IsNullOrWhiteSpace(part);
			if (!flag)
			{
				bool flag2 = ((part == "." || part == "..") ? true : false);
				flag = flag2;
			}
			return flag || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
		}))
		{
			throw new InvalidDataException("파일 경로에 빈 이름, 상위 이동 또는 Windows에서 허용되지 않는 이름이 있습니다.");
		}
		string extension = Path.GetExtension(array[^1]);
		if (!AllowedExtensions.Contains(extension))
		{
			throw new InvalidDataException("허용되지 않은 텍스트/소스 확장자입니다: " + extension);
		}
		return string.Join(Path.DirectorySeparatorChar, array);
	}

	private string ResolveInside(string root, string relative)
	{
		string fullPath = Path.GetFullPath(Path.Combine(root, relative));
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string value = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("파일 경로가 도구 작업 폴더를 벗어납니다.");
		}
		return fullPath;
	}

	private static void RejectReparsePoint(string path)
	{
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new IOException("심볼릭 링크/reparse point 경로는 Tool Studio에서 읽거나 쓰지 않습니다.");
		}
	}
}
