using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Persistence;

public sealed class ManualQaRecordStore
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private readonly string _projectRoot;
	private readonly string _recordsRoot;
	private readonly string _evidenceRoot;
	private readonly string _draftPath;

	public ManualQaRecordStore(string root)
	{
		_projectRoot = Path.GetFullPath(root);
		_recordsRoot = Path.GetFullPath(Path.Combine(root, "manual-cases"));
		_evidenceRoot = Path.GetFullPath(Path.Combine(root, "evidence"));
		_draftPath = Path.GetFullPath(Path.Combine(root, "manual-draft.json"));
	}

	public ManualQaRecord? LoadDraft()
	{
		try
		{
			return File.Exists(_draftPath) ? JsonSerializer.Deserialize<ManualQaRecord>(File.ReadAllText(_draftPath)) : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public async Task SaveDraftAsync(ManualQaRecord draft, CancellationToken cancellationToken = default(CancellationToken))
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_draftPath));
		string temporary = _draftPath + ".tmp";
		await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(draft, JsonOptions), cancellationToken);
		File.Move(temporary, _draftPath, overwrite: true);
	}

	public void ClearDraft()
	{
		if (File.Exists(_draftPath))
		{
			File.Delete(_draftPath);
		}
	}

	public async Task<ManualQaRecord> SaveAsync(ManualQaRecord record, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(record.Title))
		{
			throw new ArgumentException("테스트 제목을 입력하세요.");
		}
		ValidateCaseId(record.CaseId);
		EnsureProjectChild(_recordsRoot);
		EnsureNotReparsePoint(_recordsRoot);
		Directory.CreateDirectory(_recordsRoot);
		string path = EnsureProjectChild(Path.Combine(_recordsRoot, record.CaseId + ".json"));
		string temporary = path + ".tmp";
		await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record, JsonOptions), cancellationToken);
		File.Move(temporary, path, overwrite: true);
		return record;
	}

	public async Task<ManualEvidence> AddEvidenceAsync(string caseId, string sourcePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidateCaseId(caseId);
		string text = Path.GetExtension(sourcePath).ToLowerInvariant();
		string text2;
		if (text != null)
		{
			int length = text.Length;
			if (length != 4)
			{
				if (length == 5)
				{
					switch (text[4])
					{
					case 'g':
						break;
					case 'p':
						goto IL_0173;
					case 'm':
						goto IL_0185;
					case 'n':
						goto IL_0194;
					default:
						goto IL_0206;
					}
					if (text == ".jpeg")
					{
						goto IL_01ac;
					}
				}
			}
			else
			{
				switch (text[1])
				{
				case 'p':
					break;
				case 'j':
					goto IL_00d8;
				case 'g':
					goto IL_00ed;
				case 'm':
					goto IL_0102;
				case 't':
					goto IL_0137;
				case 'l':
					goto IL_014c;
				default:
					goto IL_0206;
				}
				if (text == ".png")
				{
					text2 = "image/png";
					goto IL_0211;
				}
			}
		}
		goto IL_0206;
		IL_014c:
		if (!(text == ".log"))
		{
			goto IL_0206;
		}
		text2 = "text/plain";
		goto IL_0211;
		IL_0194:
		if (!(text == ".json"))
		{
			goto IL_0206;
		}
		text2 = "application/json";
		goto IL_0211;
		IL_0185:
		if (!(text == ".webm"))
		{
			goto IL_0206;
		}
		text2 = "video/webm";
		goto IL_0211;
		IL_0173:
		if (!(text == ".webp"))
		{
			goto IL_0206;
		}
		text2 = "image/webp";
		goto IL_0211;
		IL_00d8:
		if (text == ".jpg")
		{
			goto IL_01ac;
		}
		goto IL_0206;
		IL_00ed:
		if (!(text == ".gif"))
		{
			goto IL_0206;
		}
		text2 = "image/gif";
		goto IL_0211;
		IL_0206:
		throw new InvalidOperationException("PNG/JPG/WebP/GIF, MP4/MKV/WebM/MOV, TXT/JSON/LOG 파일만 첨부할 수 있습니다.");
		IL_0102:
		switch (text)
		{
		case ".mp4":
			break;
		case ".mkv":
			goto IL_01d0;
		case ".mov":
			goto IL_01e2;
		default:
			goto IL_0206;
		}
		text2 = "video/mp4";
		goto IL_0211;
		IL_0211:
		string mediaType = text2;
		FileInfo info = new FileInfo(sourcePath);
		if (!info.Exists)
		{
			throw new FileNotFoundException("선택한 증거 파일을 찾을 수 없습니다.", sourcePath);
		}
		if (info.Length > 536870912)
		{
			throw new InvalidOperationException("파일당 최대 크기는 512 MB입니다.");
		}
		EnsureProjectChild(_evidenceRoot);
		EnsureNotReparsePoint(_evidenceRoot);
		string text3 = EnsureProjectChild(Path.Combine(_evidenceRoot, caseId));
		EnsureNotReparsePoint(text3);
		Directory.CreateDirectory(text3);
		string storedName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{text}";
		string destination = EnsureProjectChild(Path.Combine(text3, storedName));
		string temporary = destination + ".partial";
		try
		{
			FileStream input2 = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true);
			try
			{
				FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true);
				try
				{
					await input2.CopyToAsync(output, cancellationToken);
				}
				finally
				{
					if (output != null)
					{
						await output.DisposeAsync();
					}
				}
			}
			finally
			{
				if (input2 != null)
				{
					await input2.DisposeAsync();
				}
			}
			long copiedLength = new FileInfo(temporary).Length;
			if (copiedLength != info.Length)
			{
				throw new IOException("Evidence file changed while it was being copied; no attachment was recorded.");
			}
			input2 = File.OpenRead(temporary);
			string hash;
			try
			{
				hash = Convert.ToHexString(await SHA256.HashDataAsync(input2, cancellationToken)).ToLowerInvariant();
			}
			finally
			{
				if (input2 != null)
				{
					await input2.DisposeAsync();
				}
			}
			File.Move(temporary, destination);
			return new ManualEvidence(Path.GetFileName(sourcePath), Path.Combine("evidence", caseId, storedName), mediaType, copiedLength, hash, DateTimeOffset.UtcNow);
		}
		catch
		{
			try
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
			catch (IOException)
			{
			}
			throw;
		}
		IL_01d0:
		text2 = "video/x-matroska";
		goto IL_0211;
		IL_01e2:
		text2 = "video/quicktime";
		goto IL_0211;
		IL_0137:
		if (!(text == ".txt"))
		{
			goto IL_0206;
		}
		text2 = "text/plain";
		goto IL_0211;
		IL_01ac:
		text2 = "image/jpeg";
		goto IL_0211;
	}

	public string ResolveEvidencePath(string caseId, ManualEvidence evidence)
	{
		ValidateCaseId(caseId);
		ArgumentNullException.ThrowIfNull(evidence, "evidence");
		string text = EnsureProjectChild(Path.Combine(_evidenceRoot, caseId));
		EnsureNotReparsePoint(_evidenceRoot);
		EnsureNotReparsePoint(text);
		string text2 = EnsureProjectChild(Path.Combine(_projectRoot, evidence.RelativePath));
		string text3;
		if (!text.EndsWith(Path.DirectorySeparatorChar))
		{
			ReadOnlySpan<char> readOnlySpan = text;
			char reference = Path.DirectorySeparatorChar;
			text3 = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		}
		else
		{
			text3 = text;
		}
		string value = text3;
		if (!text2.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Evidence reference does not belong to the matching case folder.");
		}
		if (!File.Exists(text2))
		{
			throw new FileNotFoundException("Referenced QA evidence is missing from the project folder.", text2);
		}
		if ((File.GetAttributes(text2) & FileAttributes.ReparsePoint) != 0)
		{
			throw new InvalidOperationException("QA evidence cannot be read through a symbolic link or reparse-point file.");
		}
		return text2;
	}

	public IReadOnlyList<ManualQaRecord> LoadAll()
	{
		if (!Directory.Exists(_recordsRoot))
		{
			return Array.Empty<ManualQaRecord>();
		}
		List<ManualQaRecord> list = new List<ManualQaRecord>();
		foreach (string item in Directory.EnumerateFiles(_recordsRoot, "*.json"))
		{
			try
			{
				ManualQaRecord manualQaRecord = JsonSerializer.Deserialize<ManualQaRecord>(File.ReadAllText(item));
				if ((object)manualQaRecord != null)
				{
					list.Add(manualQaRecord);
				}
			}
			catch (JsonException)
			{
			}
		}
		return list.OrderByDescending((ManualQaRecord r) => r.UpdatedAt).ToArray();
	}

	private void ValidateCaseId(string caseId)
	{
		bool flag = string.IsNullOrWhiteSpace(caseId) || caseId.Length > 128;
		if (!flag)
		{
			bool flag2 = ((caseId == "." || caseId == "..") ? true : false);
			flag = flag2;
		}
		if (flag || caseId.Any(delegate(char character)
		{
			bool flag3 = char.IsAsciiLetterOrDigit(character);
			if (!flag3)
			{
				bool flag4 = ((character == '-' || character == '.' || character == '_') ? true : false);
				flag3 = flag4;
			}
			return !flag3;
		}))
		{
			throw new ArgumentException("Case ID must be a 1–128 character stable ID using only ASCII letters, digits, dot, underscore, or hyphen.", "caseId");
		}
	}

	private string EnsureProjectChild(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string text;
		if (!_projectRoot.EndsWith(Path.DirectorySeparatorChar))
		{
			ReadOnlySpan<char> readOnlySpan = _projectRoot;
			char reference = Path.DirectorySeparatorChar;
			text = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		}
		else
		{
			text = _projectRoot;
		}
		string value = text;
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("QA record and evidence files must remain inside the selected project data folder.");
		}
		return fullPath;
	}

	private static void EnsureNotReparsePoint(string path)
	{
		if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new InvalidOperationException("QA evidence cannot be written through a symbolic link or reparse-point folder.");
		}
	}
}
