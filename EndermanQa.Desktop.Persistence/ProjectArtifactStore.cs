using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EndermanQa.Desktop.Persistence;

public sealed class ProjectArtifactStore
{
	public sealed record Artifact(string Id, string Title, string FilePath, DateTimeOffset CreatedAt, string Sha256);

	public const int ContractVersion = 1;

	

	private string RootWithSeparator
	{
		get
		{
			ReadOnlySpan<char> readOnlySpan = _root.TrimEnd(Path.DirectorySeparatorChar);
			char reference = Path.DirectorySeparatorChar;
			return string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		}
	}

	private readonly string _root;

	public ProjectArtifactStore(string projectDataRoot)
	{
		_root = Path.GetFullPath(Path.Combine(projectDataRoot, "generated"));
	}

	public Artifact CreateAiArtifact(string title, string prompt, string response, string? model, string? gameTarget = null, string? gameVersion = null, string workProfile = "qa")
	{
		if (string.IsNullOrWhiteSpace(title) || title.Length > 160)
		{
			throw new ArgumentException("산출물 제목은 1~160자여야 합니다.", "title");
		}
		if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(response))
		{
			throw new ArgumentException("요청과 AI 답변이 있어야 저장할 수 있습니다.");
		}
		Directory.CreateDirectory(_root);
		string text = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		string fullPath = Path.GetFullPath(Path.Combine(_root, text + ".md"));
		if (!fullPath.StartsWith(RootWithSeparator, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("산출물 경로가 프로젝트 데이터 경계를 벗어났습니다.");
		}
		bool flag;
		switch (workProfile)
		{
		case "qa":
		case "tool-design":
		case "self-evolution":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentException("지원하지 않는 산출물 작업 유형입니다.", "workProfile");
		}
		string s = $"# {title.Trim()}\n\n- Created (UTC): {DateTimeOffset.UtcNow:O}\n- Work profile: {workProfile}\n- Target application/game: {gameTarget ?? "unspecified"}\n- Target version/environment: {gameVersion ?? "unspecified"}\n- AI model: {model ?? "unknown"}\n- Artifact contract: v{1}\n\n## 요청\n\n{prompt.Trim()}\n\n## 응답 / 초안\n\n{response.Trim()}\n";
		byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(s);
		string text2 = Path.Combine(_root, text + ".tmp");
		try
		{
			using (FileStream fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(bytes);
				fileStream.Flush(flushToDisk: true);
			}
			File.Move(text2, fullPath, overwrite: false);
		}
		finally
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
		}
		return new Artifact(text, title.Trim(), fullPath, File.GetCreationTimeUtc(fullPath), Convert.ToHexString(SHA256.HashData(bytes)));
	}

	public IReadOnlyList<Artifact> List()
	{
		if (!Directory.Exists(_root))
		{
			return Array.Empty<Artifact>();
		}
		return (from x in Directory.EnumerateFiles(_root, "*.md", SearchOption.TopDirectoryOnly).Select(delegate(string path)
			{
				string fullPath = Path.GetFullPath(path);
				FileInfo fileInfo = new FileInfo(fullPath);
				byte[] source = File.ReadAllBytes(fullPath);
				string text = File.ReadLines(fullPath).FirstOrDefault()?.TrimStart('#', ' ').Trim();
				return new Artifact(Path.GetFileNameWithoutExtension(fullPath), string.IsNullOrWhiteSpace(text) ? fileInfo.Name : text, fullPath, fileInfo.CreationTimeUtc, Convert.ToHexString(SHA256.HashData(source)));
			})
			orderby x.CreatedAt descending
			select x).ToArray();
	}

	public string Read(Artifact artifact)
	{
		string fullPath = Path.GetFullPath(artifact.FilePath);
		if (!fullPath.StartsWith(RootWithSeparator, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("선택한 파일이 현재 프로젝트의 산출물 폴더에 없습니다.");
		}
		return File.ReadAllText(fullPath, Encoding.UTF8);
	}
}
