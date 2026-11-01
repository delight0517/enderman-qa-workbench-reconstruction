using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndermanQa.Desktop.Scenarios;

public sealed class GameMacroRunReceiptStore
{
	private sealed record HashPayload(int ContractVersion, string RunId, string MacroId, string MacroName, string MacroContentSha256, GameAutomationTarget Target, string AdapterId, int AdapterContractVersion, string CapabilitySha256, GameTargetVerification? TargetVerification, int Attempt, int RequestedRepeatCount, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, GameMacroRunResult Result);

	private sealed record EvidenceHashPayload(int ContractVersion, string RunId, string MacroId, string MacroName, string MacroContentSha256, GameAutomationTarget Target, string AdapterId, int AdapterContractVersion, string CapabilitySha256, GameTargetVerification? TargetVerification, int Attempt, int RequestedRepeatCount, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, GameMacroRunResult Result, IReadOnlyList<GameMacroEvidenceReceipt> EvidenceManifest);

	private sealed record EvidenceWarningHashPayload(int ContractVersion, string RunId, string MacroId, string MacroName, string MacroContentSha256, GameAutomationTarget Target, string AdapterId, int AdapterContractVersion, string CapabilitySha256, GameTargetVerification? TargetVerification, int Attempt, int RequestedRepeatCount, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, GameMacroRunResult Result, IReadOnlyList<GameMacroEvidenceReceipt> EvidenceManifest, string EvidenceIntegrityIssue);

	private sealed record EvidenceBuildResult(IReadOnlyList<GameMacroEvidenceReceipt> Manifest, GameMacroRunResult SanitizedResult, string? IntegrityIssue);

	public const int CurrentContractVersion = 1;

	private const long MaxReceiptBytes = 4194304L;

	private const int MaxEvidenceFiles = 32;

	private const long MaxEvidenceFileBytes = 67108864L;

	private const long MaxEvidenceTotalBytes = 134217728L;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	private static readonly JsonSerializerOptions CanonicalOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	

	private string ReceiptRoot => Path.Combine(_projectRoot, "macro-runs");

	private readonly string _projectRoot;

	public GameMacroRunReceiptStore(string projectDataRoot)
	{
		_projectRoot = Path.GetFullPath(projectDataRoot);
	}

	public GameMacroRunReceipt Save(GameAutomationMacro macro, GameToolDescriptor descriptor, int attempt, int requestedRepeatCount, DateTimeOffset startedAt, DateTimeOffset completedAt, GameMacroRunResult result, GameTargetVerification? targetVerification = null)
	{
		ArgumentNullException.ThrowIfNull(macro, "macro");
		ArgumentNullException.ThrowIfNull(descriptor, "descriptor");
		ArgumentNullException.ThrowIfNull(result, "result");
		bool flag = ((attempt < 1 || attempt > 10) ? true : false);
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3 = ((requestedRepeatCount < 1 || requestedRepeatCount > 10) ? true : false);
			flag2 = flag3;
		}
		if (flag2 || attempt > requestedRepeatCount)
		{
			throw new ArgumentOutOfRangeException("attempt", "Attempt must be within the requested repeat count.");
		}
		if (completedAt < startedAt)
		{
			throw new ArgumentException("Completion time precedes start time.", "completedAt");
		}
		if (macro.AdapterId != descriptor.AdapterId || macro.Target.GameId != descriptor.GameId || macro.CapabilitySha256 != descriptor.CapabilitySha256)
		{
			throw new InvalidDataException("Run receipt descriptor does not match the saved macro identity.");
		}
		ValidateRunResult(result);
		EvidenceBuildResult evidenceBuildResult = BuildEvidenceManifest(result);
		GameMacroRunReceipt gameMacroRunReceipt = new GameMacroRunReceipt(1, Guid.NewGuid().ToString("N"), macro.Id, macro.Name, macro.ContentSha256, macro.Target, descriptor.AdapterId, descriptor.ContractVersion, descriptor.CapabilitySha256, targetVerification, attempt, requestedRepeatCount, startedAt, completedAt, evidenceBuildResult.SanitizedResult, string.Empty)
		{
			EvidenceManifest = evidenceBuildResult.Manifest,
			EvidenceIntegrityIssue = evidenceBuildResult.IntegrityIssue
		};
		gameMacroRunReceipt = gameMacroRunReceipt with
		{
			ReceiptSha256 = ComputeHash(gameMacroRunReceipt)
		};
		RejectReparseAncestors(_projectRoot);
		Directory.CreateDirectory(ReceiptRoot);
		RejectReparseAncestors(ReceiptRoot);
		WriteNew(PathFor(gameMacroRunReceipt.RunId), gameMacroRunReceipt);
		return gameMacroRunReceipt;
	}

	public GameMacroRunReceipt Load(string runId)
	{
		if (!Guid.TryParseExact(runId, "N", out var _))
		{
			throw new InvalidDataException("Run receipt ID is invalid.");
		}
		RejectReparseAncestors(ReceiptRoot);
		string text = PathFor(runId);
		RejectReparse(text);
		if (new FileInfo(text).Length > 4194304)
		{
			throw new InvalidDataException("Run receipt exceeds the 4 MiB safety limit.");
		}
		GameMacroRunReceipt gameMacroRunReceipt = JsonSerializer.Deserialize<GameMacroRunReceipt>(File.ReadAllText(text), JsonOptions) ?? throw new InvalidDataException("Run receipt is empty or malformed.");
		if (gameMacroRunReceipt.ContractVersion != 1 || !string.Equals(gameMacroRunReceipt.RunId, runId, StringComparison.Ordinal) || !string.Equals(gameMacroRunReceipt.ReceiptSha256, ComputeHash(gameMacroRunReceipt), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Run receipt contract or SHA-256 integrity check failed.");
		}
		try
		{
			VerifyEvidenceManifest(gameMacroRunReceipt.EvidenceManifest);
			return gameMacroRunReceipt;
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) ? 1 : 0) != 0)
		{
			string text2 = (string.IsNullOrWhiteSpace(gameMacroRunReceipt.EvidenceIntegrityIssue) ? ex.Message : (gameMacroRunReceipt.EvidenceIntegrityIssue + " | " + ex.Message));
			return gameMacroRunReceipt with
			{
				EvidenceIntegrityIssue = ((text2.Length <= 4096) ? text2 : text2.Substring(0, 4096))
			};
		}
	}

	public IReadOnlyList<GameMacroRunReceipt> ListRecent(int limit = 100)
	{
		if ((limit < 1 || limit > 500) ? true : false)
		{
			throw new ArgumentOutOfRangeException("limit");
		}
		if (!Directory.Exists(ReceiptRoot))
		{
			return Array.Empty<GameMacroRunReceipt>();
		}
		RejectReparseAncestors(ReceiptRoot);
		List<GameMacroRunReceipt> list = new List<GameMacroRunReceipt>();
		foreach (string item in Directory.EnumerateFiles(ReceiptRoot, "*.json", SearchOption.TopDirectoryOnly))
		{
			RejectReparse(item);
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item);
			list.Add(Load(fileNameWithoutExtension));
		}
		return list.OrderByDescending((GameMacroRunReceipt item) => item.CompletedAt).ThenBy<GameMacroRunReceipt, string>((GameMacroRunReceipt item) => item.RunId, StringComparer.Ordinal).Take(limit)
			.ToArray();
	}

	private string PathFor(string id)
	{
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(ReceiptRoot).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string value = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		string fullPath = Path.GetFullPath(Path.Combine(ReceiptRoot, id + ".json"));
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new UnauthorizedAccessException("Run receipt path escapes the project.");
		}
		return fullPath;
	}

	private static string ComputeHash(GameMacroRunReceipt receipt)
	{
		if (!string.IsNullOrWhiteSpace(receipt.EvidenceIntegrityIssue))
		{
			EvidenceWarningHashPayload value = new EvidenceWarningHashPayload(receipt.ContractVersion, receipt.RunId, receipt.MacroId, receipt.MacroName, receipt.MacroContentSha256, receipt.Target, receipt.AdapterId, receipt.AdapterContractVersion, receipt.CapabilitySha256, receipt.TargetVerification, receipt.Attempt, receipt.RequestedRepeatCount, receipt.StartedAt, receipt.CompletedAt, receipt.Result, receipt.EvidenceManifest ?? Array.Empty<GameMacroEvidenceReceipt>(), receipt.EvidenceIntegrityIssue);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, CanonicalOptions))));
		}
		IReadOnlyList<GameMacroEvidenceReceipt> evidenceManifest = receipt.EvidenceManifest;
		if (evidenceManifest != null && evidenceManifest.Count > 0)
		{
			EvidenceHashPayload value2 = new EvidenceHashPayload(receipt.ContractVersion, receipt.RunId, receipt.MacroId, receipt.MacroName, receipt.MacroContentSha256, receipt.Target, receipt.AdapterId, receipt.AdapterContractVersion, receipt.CapabilitySha256, receipt.TargetVerification, receipt.Attempt, receipt.RequestedRepeatCount, receipt.StartedAt, receipt.CompletedAt, receipt.Result, receipt.EvidenceManifest);
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value2, CanonicalOptions))));
		}
		HashPayload value3 = new HashPayload(receipt.ContractVersion, receipt.RunId, receipt.MacroId, receipt.MacroName, receipt.MacroContentSha256, receipt.Target, receipt.AdapterId, receipt.AdapterContractVersion, receipt.CapabilitySha256, receipt.TargetVerification, receipt.Attempt, receipt.RequestedRepeatCount, receipt.StartedAt, receipt.CompletedAt, receipt.Result);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value3, CanonicalOptions))));
	}

	private EvidenceBuildResult BuildEvidenceManifest(GameMacroRunResult result)
	{
		string[] array = result.Steps.SelectMany((GameMacroStepResult step) => step.EvidenceReferences ?? Array.Empty<string>()).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		List<GameMacroEvidenceReceipt> list = new List<GameMacroEvidenceReceipt>(Math.Min(array.Length, 32));
		Dictionary<string, string> safeReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		List<string> list2 = new List<string>();
		long num = 0L;
		string[] array2 = array;
		foreach (string text in array2)
		{
			try
			{
				if (list.Count >= 32)
				{
					throw new InvalidDataException($"Only the first {32} evidence files can be verified in one receipt.");
				}
				string text2 = NormalizeEvidencePath(text);
				string path = ResolveEvidencePath(text2);
				RejectReparseAncestors(_projectRoot);
				RejectReparseAncestors(path);
				if (!File.Exists(path))
				{
					throw new FileNotFoundException("Evidence file is missing.");
				}
				GameMacroEvidenceReceipt gameMacroEvidenceReceipt = InspectEvidenceFile(path, text2);
				if (checked(num + gameMacroEvidenceReceipt.LengthBytes) > 134217728)
				{
					throw new InvalidDataException("Per-receipt evidence exceeds the 128 MiB limit.");
				}
				num += gameMacroEvidenceReceipt.LengthBytes;
				list.Add(gameMacroEvidenceReceipt);
				safeReferences[text] = text2;
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
			{
				string text3 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 16);
				safeReferences[text] = "[unverified evidence " + text3 + "]";
				if (list2.Count < 32)
				{
					list2.Add("Evidence " + text3 + ": " + ex.Message);
				}
			}
		}
		GameMacroStepResult[] steps = result.Steps.Select((GameMacroStepResult step) => step with
		{
			EvidenceReferences = step.EvidenceReferences.Select((string reference) => safeReferences.GetValueOrDefault(reference, "[unverified evidence " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference))).Substring(0, 16) + "]")).ToArray()
		}).ToArray();
		GameMacroRunResult sanitizedResult = result with
		{
			Steps = steps
		};
		string text4 = ((list2.Count == 0) ? null : string.Join(" | ", list2));
		if (text4 != null && text4.Length > 4096)
		{
			text4 = text4.Substring(0, 4096);
		}
		return new EvidenceBuildResult(list, sanitizedResult, text4);
	}

	private static void ValidateRunResult(GameMacroRunResult result)
	{
		if (!Enum.IsDefined(result.Status) || !Enum.IsDefined(result.Verdict) || result.Steps == null || result.Steps.Count > 100 || result.Assertions == null || result.Assertions.Count > 100 || result.Limitation == null || result.Limitation.Length > 4096)
		{
			throw new InvalidDataException("Macro result has invalid status or exceeds the receipt result bounds.");
		}
		foreach (GameMacroStepResult step in result.Steps)
		{
			if ((object)step == null || string.IsNullOrWhiteSpace(step.StepId) || step.StepId.Length > 80 || step.Detail == null || step.Detail.Length > 4096 || step.Observations == null || step.Observations.Count > 100 || step.EvidenceReferences == null || step.EvidenceReferences.Count > 32 || step.EvidenceReferences.Any((string path) => string.IsNullOrWhiteSpace(path) || path.Length > 512))
			{
				throw new InvalidDataException("Macro step result is malformed or exceeds the receipt bounds.");
			}
			if (step.Observations.Any<KeyValuePair<string, string>>((KeyValuePair<string, string> pair) => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 || pair.Value == null || pair.Value.Length > 4096))
			{
				throw new InvalidDataException("Macro observations exceed the receipt bounds.");
			}
		}
		if (result.Assertions.Any((GameMacroAssertionResult assertion) => (object)assertion == null || string.IsNullOrWhiteSpace(assertion.AssertionId) || assertion.AssertionId.Length > 80 || assertion.Detail == null || assertion.Detail.Length > 4096))
		{
			throw new InvalidDataException("Macro assertion result is malformed or exceeds the receipt bounds.");
		}
	}

	private void VerifyEvidenceManifest(IReadOnlyList<GameMacroEvidenceReceipt>? manifest)
	{
		if (manifest == null || manifest.Count == 0)
		{
			return;
		}
		if (manifest.Count > 32)
		{
			throw new InvalidDataException("Run receipt evidence count exceeds the safety limit.");
		}
		long num = 0L;
		foreach (GameMacroEvidenceReceipt item in manifest)
		{
			string relativePath = NormalizeEvidencePath(item.RelativePath);
			string path = ResolveEvidencePath(relativePath);
			RejectReparseAncestors(path);
			if (!File.Exists(path))
			{
				throw new InvalidDataException("A referenced evidence file is missing.");
			}
			GameMacroEvidenceReceipt gameMacroEvidenceReceipt = InspectEvidenceFile(path, relativePath);
			if (gameMacroEvidenceReceipt.LengthBytes != item.LengthBytes)
			{
				throw new InvalidDataException("A referenced evidence file length changed.");
			}
			num = checked(num + gameMacroEvidenceReceipt.LengthBytes);
			if (num > 134217728)
			{
				throw new InvalidDataException("Run receipt evidence exceeds the total safety limit.");
			}
			if (!string.Equals(gameMacroEvidenceReceipt.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase) || !string.Equals(gameMacroEvidenceReceipt.MediaType, item.MediaType, StringComparison.Ordinal))
			{
				throw new InvalidDataException("A referenced evidence file hash or media type changed.");
			}
		}
	}

	private static GameMacroEvidenceReceipt InspectEvidenceFile(string path, string relativePath)
	{
		RejectReparseAncestors(path);
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
		long length = fileStream.Length;
		if ((length <= 0 || length > 67108864) ? true : false)
		{
			throw new InvalidDataException("Macro evidence file is empty or exceeds 64 MiB.");
		}
		Span<byte> buffer = stackalloc byte[12];
		string text = DetectMediaType(relativePath, buffer[..fileStream.Read(buffer)]);
		if (text.StartsWith("text/", StringComparison.Ordinal) || text == "application/json")
		{
			fileStream.Position = 0L;
			ValidateUtf8(fileStream);
		}
		fileStream.Position = 0L;
		string sha = Convert.ToHexString(SHA256.HashData(fileStream));
		if (fileStream.Length != length)
		{
			throw new InvalidDataException("Macro evidence file changed while it was being checked.");
		}
		return new GameMacroEvidenceReceipt(relativePath, sha, length, text);
	}

	private static void ValidateUtf8(Stream stream)
	{
		Decoder decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetDecoder();
		byte[] array = new byte[32768];
		char[] chars = new char[32768];
		try
		{
			int byteCount;
			while ((byteCount = stream.Read(array, 0, array.Length)) > 0)
			{
				decoder.GetChars(array, 0, byteCount, chars, 0, flush: false);
			}
			decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
		}
		catch (DecoderFallbackException innerException)
		{
			throw new InvalidDataException("Text evidence must be valid UTF-8.", innerException);
		}
	}

	private string ResolveEvidencePath(string relativePath)
	{
		ReadOnlySpan<char> readOnlySpan = _projectRoot.TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string value = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		string fullPath = Path.GetFullPath(Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new UnauthorizedAccessException("Macro evidence path escapes the project root.");
		}
		return fullPath;
	}

	private static string NormalizeEvidencePath(string? reference)
	{
		if (string.IsNullOrWhiteSpace(reference) || reference.Length > 512 || Path.IsPathRooted(reference) || reference.Contains(':') || reference.Contains('\\'))
		{
			throw new InvalidDataException("Evidence references must be short project-relative paths using forward slashes.");
		}
		string[] array = reference.Split('/');
		if (array.Any(delegate(string segment)
		{
			bool flag = segment.Length == 0;
			if (!flag)
			{
				bool flag2 = ((segment == "." || segment == "..") ? true : false);
				flag = flag2;
			}
			return flag || segment.Any(char.IsControl);
		}))
		{
			throw new InvalidDataException("Evidence reference contains an invalid path segment.");
		}
		return string.Join('/', array);
	}

	private static string DetectMediaType(string relativePath, ReadOnlySpan<byte> bytes)
	{
		if (bytes.StartsWith(new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 }))
		{
			return "image/png";
		}
		if (bytes.Length >= 3 && bytes[0] == byte.MaxValue && bytes[1] == 216 && bytes[2] == byte.MaxValue)
		{
			return "image/jpeg";
		}
		if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes.Slice(0, 4)) == "RIFF" && Encoding.ASCII.GetString(bytes.Slice(8, 4)) == "WEBP")
		{
			return "image/webp";
		}
		bool flag = bytes.Length >= 6;
		if (flag)
		{
			string @string = Encoding.ASCII.GetString(bytes.Slice(0, 6));
			bool flag2 = ((@string == "GIF87a" || @string == "GIF89a") ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return "image/gif";
		}
		if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes.Slice(4, 4)) == "ftyp")
		{
			return "video/mp4";
		}
		return (string)((Path.GetExtension(relativePath).ToLowerInvariant() switch
		{
			".txt" => "text/plain", 
			".json" => "application/json", 
			".log" => "text/plain", 
			".csv" => "text/csv", 
			".md" => "text/markdown", 
			_ => null, 
		}) ?? throw new InvalidDataException("Evidence type is unsupported; use PNG, JPEG, WebP, GIF, MP4 or UTF-8 text/log/JSON/CSV/Markdown."));
	}

	private static void WriteNew(string path, GameMacroRunReceipt receipt)
	{
		string text = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				JsonSerializer.Serialize(fileStream, receipt, JsonOptions);
				if (fileStream.Length > 4194304)
				{
					throw new InvalidDataException("Run receipt exceeds the 4 MiB safety limit.");
				}
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
			throw new UnauthorizedAccessException("Run receipt storage cannot be a symbolic link/reparse point.");
		}
	}

	private static void RejectReparseAncestors(string path)
	{
		string text = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(text))
		{
			RejectReparse(text);
			string directoryName = Path.GetDirectoryName(text);
			if (!string.IsNullOrEmpty(directoryName) && !string.Equals(directoryName, text, StringComparison.OrdinalIgnoreCase))
			{
				text = directoryName;
				continue;
			}
			break;
		}
	}
}
