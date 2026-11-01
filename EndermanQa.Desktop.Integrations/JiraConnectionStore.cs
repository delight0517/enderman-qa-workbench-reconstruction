using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace EndermanQa.Desktop.Integrations;

public sealed class JiraConnectionStore
{
	private static readonly byte[] Entropy = "EndermanQaWorkbench:JiraSettings:v1"u8.ToArray();

	private readonly string _settingsPath;
	private readonly string _ledgerPath;

	public JiraConnectionStore(string settingsRoot, string? submissionRoot = null)
	{
		_settingsPath = Path.Combine(settingsRoot, "integrations", "jira.connection.dpapi");
		_ledgerPath = Path.Combine(submissionRoot ?? settingsRoot, "integrations", "jira.submissions.json");
	}

	public JiraConnectionSettings? Load()
	{
		if (!File.Exists(_settingsPath))
		{
			return null;
		}
		try
		{
			byte[] array = ProtectedData.Unprotect(File.ReadAllBytes(_settingsPath), Entropy, DataProtectionScope.CurrentUser);
			try
			{
				return JsonSerializer.Deserialize<JiraConnectionSettings>(array);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(array);
			}
		}
		catch (CryptographicException)
		{
			return null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public void Save(JiraConnectionSettings settings)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
		byte[] array = JsonSerializer.SerializeToUtf8Bytes(settings);
		try
		{
			byte[] array2 = ProtectedData.Protect(array, Entropy, DataProtectionScope.CurrentUser);
			string text = _settingsPath + ".tmp";
			File.WriteAllBytes(text, array2);
			File.Move(text, _settingsPath, overwrite: true);
			CryptographicOperations.ZeroMemory(array2);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array);
		}
	}

	public void Clear()
	{
		if (File.Exists(_settingsPath))
		{
			File.Delete(_settingsPath);
		}
	}

	public IReadOnlyList<JiraSubmissionEntry> LoadLedger()
	{
		try
		{
			return File.Exists(_ledgerPath) ? (JsonSerializer.Deserialize<List<JiraSubmissionEntry>>(File.ReadAllText(_ledgerPath)) ?? new List<JiraSubmissionEntry>()) : new List<JiraSubmissionEntry>();
		}
		catch (JsonException)
		{
			return Array.Empty<JiraSubmissionEntry>();
		}
	}

	public bool TryRecordSubmission(string submissionId, string caseId, string runId, string issueKey)
	{
		string submissionId2 = submissionId;
		List<JiraSubmissionEntry> list = LoadLedger().ToList();
		if (list.Any((JiraSubmissionEntry x) => x.SubmissionId == submissionId2))
		{
			return false;
		}
		list.Add(new JiraSubmissionEntry(submissionId2, caseId, runId, issueKey, DateTimeOffset.UtcNow.ToString("O")));
		Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath));
		string text = _ledgerPath + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(list, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
		File.Move(text, _ledgerPath, overwrite: true);
		return true;
	}
}
