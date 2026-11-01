using System;
using System.IO;
using System.Text.Json;

namespace EndermanQa.Desktop.Integrations;

public sealed class OnlyChatRunStore
{
	public sealed record RunRecord(int ContractVersion, string ProjectId, string RunId, string? SessionId, string Objective, string WorkspacePath, string ProfileId, string ProfileName, string AccessPolicy, DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, string Status, string? Progress);

	private readonly string _root;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	public const int ContractVersion = 1;

	public OnlyChatRunStore(string projectDataRoot)
	{
		_root = Path.GetFullPath(Path.Combine(projectDataRoot, "onlychat-runs"));
	}

	public void Save(OnlyChatManagerClient.RunReceipt receipt, string projectId, string objective, string profileId, string profileName, string accessPolicy)
	{
		Guid result;
		bool flag = !Guid.TryParse(receipt.RunId, out result) || objective.Length > 12000 || profileId.Length > 120 || profileName.Length > 120;
		if (!flag)
		{
			bool flag2 = ((accessPolicy == "read-only" || accessPolicy == "workspace-write") ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			throw new InvalidDataException("Only Chat 실행 기록의 필드가 올바르지 않습니다.");
		}
		Directory.CreateDirectory(_root);
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		WriteAtomic(Path.Combine(_root, receipt.RunId + ".json"), new RunRecord(1, projectId, receipt.RunId, receipt.SessionId, objective, Path.GetFullPath(receipt.WorkspacePath), profileId, profileName, accessPolicy, receipt.StartedAt, utcNow, receipt.Status, null));
	}

	public void UpdateStatus(string runId, string status, string? progress)
	{
		if (!Guid.TryParse(runId, out var _) || status.Length > 80 || (progress?.Length ?? 0) > 2000)
		{
			return;
		}
		string text = Path.Combine(_root, runId + ".json");
		if (File.Exists(text))
		{
			if ((File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0 || new FileInfo(text).Length > 131072)
			{
				throw new InvalidDataException("Only Chat 실행 기록 파일이 유효하지 않습니다.");
			}
			RunRecord runRecord = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(text), JsonOptions);
			if ((object)runRecord == null || runRecord.ContractVersion != 1 || runRecord.RunId != runId)
			{
				throw new InvalidDataException("Only Chat 실행 기록 schema가 다릅니다.");
			}
			WriteAtomic(text, runRecord with
			{
				UpdatedAt = DateTimeOffset.UtcNow,
				Status = status,
				Progress = progress
			});
		}
	}

	private static void WriteAtomic(string path, RunRecord record)
	{
		string text = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				JsonSerializer.Serialize(fileStream, record, JsonOptions);
				fileStream.Flush(flushToDisk: true);
			}
			File.Move(text, path, overwrite: true);
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
}
