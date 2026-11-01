using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EndermanQa.Desktop.Persistence;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Integrations;

public sealed class JiraQaCandidateService
{
	private readonly string _root;

	public JiraQaCandidateService(string dataRoot)
	{
		_root = Path.GetFullPath(dataRoot);
	}

	public IReadOnlyList<JiraQaCandidate> LoadLatestSuiteCandidates(IReadOnlyCollection<JiraSubmissionEntry> submitted)
	{
		string path = Path.Combine(_root, "suite-runs");
		if (!Directory.Exists(path))
		{
			return Array.Empty<JiraQaCandidate>();
		}
		foreach (DirectoryInfo item in from x in Directory.GetDirectories(path)
			select new DirectoryInfo(x) into x
			orderby x.CreationTimeUtc descending
			select x)
		{
			string path2 = Path.Combine(item.FullName, "results.json");
			if (!File.Exists(path2))
			{
				continue;
			}
			SuiteCaseReceipt[] array;
			try
			{
				array = JsonSerializer.Deserialize<SuiteCaseReceipt[]>(File.ReadAllText(path2), new JsonSerializerOptions(JsonSerializerDefaults.Web));
			}
			catch (JsonException)
			{
				continue;
			}
			if (array == null)
			{
				continue;
			}
			List<JiraQaCandidate> list = new List<JiraQaCandidate>();
			foreach (SuiteCaseReceipt item2 in array.Where(delegate(SuiteCaseReceipt x)
			{
				string verdict = x.Verdict;
				bool flag = ((verdict == "Fail" || verdict == "Inconclusive") ? true : false);
				bool flag2 = flag;
				if (!flag2)
				{
					string status = x.Status;
					bool flag3 = ((status == "reproduced-anomaly" || status == "anomaly-unconfirmed") ? true : false);
					flag2 = flag3;
				}
				return flag2;
			}))
			{
				string text = item2.FirstRunId ?? item2.RepeatRunIds.LastOrDefault() ?? string.Empty;
				string id = $"suite:{item.Name}:{item2.CaseId}:{text}";
				if (!submitted.Any((JiraSubmissionEntry x) => x.SubmissionId == id || x.SubmissionId.EndsWith(":" + id, StringComparison.Ordinal)))
				{
					string[] array2 = (from x in Directory.GetFiles(item.FullName, $"case-{item2.SourceId:000}-attempt-*.jsonl")
						orderby x
						select x).ToArray().Where(File.Exists).Take(8)
						.ToArray();
					string description = $"QA classification: {((item2.Status == "reproduced-anomaly") ? "reproduced anomaly" : "unconfirmed QA anomaly / investigation; not yet a confirmed game defect")}\nSuite ID: {item.Name}\nCase: {item2.CaseId} — {item2.Title}\nExpected / coverage: {item2.Coverage}\nVerdict: {item2.Verdict}\nRepeatability: {item2.Repeatability ?? "not repeated"}\nFirst run: {item2.FirstRunId ?? "n/a"}\nRepeat runs: {string.Join(", ", item2.RepeatRunIds)}\nCapture: {item2.CaptureStatus}\nLimitation: {item2.Limitation ?? "none recorded"}\nLocal evidence logs: {string.Join("; ", array2.Select(Path.GetFileName))}";
					list.Add(new JiraQaCandidate(id, item2.CaseId, text, "[Enderman QA " + item2.Verdict + "] " + item2.Title, "Task", description, array2, "suite " + item.Name + " · " + item2.Status));
				}
			}
			if (list.Count > 0)
			{
				return list;
			}
		}
		return Array.Empty<JiraQaCandidate>();
	}

	public static string ManualSubmissionId(string projectKey, ManualQaRecord record)
	{
		return "manual:" + projectKey + ":" + record.CaseId;
	}
}
