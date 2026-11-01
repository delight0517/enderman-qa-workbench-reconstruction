using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Results;

internal sealed class SuiteResultsIndex
{
	private sealed record Stamp(DateTime LastWriteUtc, long Length);

	private sealed record CachedSuite(Stamp Stamp, IReadOnlyList<CachedReceipt> Receipts);

	private sealed record CachedReceipt(SuiteResultReceipt Receipt, DateTime SortTimeUtc);

	private const long MaxCatalogBytes = 8388608L;

	private const long MaxResultsBytes = 16777216L;

	private const long MaxJournalBytes = 2097152L;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private string? _root;

	private string? _dataRoot;

	private Stamp? _catalogStamp;

	private Dictionary<int, SuiteCaseRow> _catalog = new Dictionary<int, SuiteCaseRow>();

	private Stamp? _baselineStamp;

	private Dictionary<int, SuiteCaseRow> _baseline = new Dictionary<int, SuiteCaseRow>();

	private string? _baselineDirectory;

	private readonly Dictionary<string, CachedSuite> _suites = new Dictionary<string, CachedSuite>(StringComparer.OrdinalIgnoreCase);

	public async Task<SuiteResultsSnapshot> RefreshAsync(string projectRoot, string projectDataRoot, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot, "projectRoot");
		ArgumentException.ThrowIfNullOrWhiteSpace(projectDataRoot, "projectDataRoot");
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			string root = Path.GetFullPath(projectRoot);
			string dataRoot = Path.GetFullPath(projectDataRoot);
			if (!Directory.Exists(root))
			{
				return new SuiteResultsSnapshot(Array.Empty<SuiteCaseRow>(), 0, "프로젝트 결과 폴더를 찾을 수 없습니다.", root);
			}
			if (!Within(root, dataRoot))
			{
				throw new InvalidDataException("프로젝트 데이터 경로가 프로젝트 폴더 밖에 있습니다.");
			}
			if (!string.Equals(_root, root, StringComparison.OrdinalIgnoreCase) || !string.Equals(_dataRoot, dataRoot, StringComparison.OrdinalIgnoreCase))
			{
				Reset(root, dataRoot);
			}
			return await Task.Run(() => RefreshCore(root, dataRoot, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<IReadOnlyList<SuiteAttempt>> ReadAttemptsAsync(SuiteCaseRow row, CancellationToken cancellationToken = default(CancellationToken))
	{
		SuiteCaseRow row2 = row;
		string root = _root ?? throw new InvalidOperationException("프로젝트를 먼저 불러오세요.");
		string dataRoot = _dataRoot ?? root;
		return await Task.Run(() => ReadAttempts(root, dataRoot, row2, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<string> ReadJournalAsync(SuiteAttempt attempt, CancellationToken cancellationToken = default(CancellationToken))
	{
		SuiteAttempt attempt2 = attempt;
		string root = _root ?? throw new InvalidOperationException("프로젝트를 먼저 불러오세요.");
		return await Task.Run(() => FormatJournal(root, attempt2, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private void Reset(string root, string dataRoot)
	{
		_root = root;
		_dataRoot = dataRoot;
		_catalogStamp = null;
		_catalog = new Dictionary<int, SuiteCaseRow>();
		_baselineStamp = null;
		_baseline = new Dictionary<int, SuiteCaseRow>();
		_baselineDirectory = null;
		_suites.Clear();
	}

	private SuiteResultsSnapshot RefreshCore(string root, string dataRoot, CancellationToken token)
	{
		List<string> list = new List<string>();
		RefreshCatalog(root, dataRoot, list, token);
		RefreshBaseline(root, list, token);
		int updatedReceiptFiles = RefreshSuiteResults(root, dataRoot, list, token);
		Dictionary<int, SuiteCaseRow> dictionary = new Dictionary<int, SuiteCaseRow>(_catalog);
		foreach (KeyValuePair<int, SuiteCaseRow> item in _baseline)
		{
			item.Deconstruct(out var key, out var value);
			int key2 = key;
			SuiteCaseRow incoming = value;
			dictionary[key2] = Merge(dictionary.GetValueOrDefault(key2), incoming);
		}
		foreach (CachedReceipt item2 in from item in _suites.Values.SelectMany((CachedSuite item) => item.Receipts)
			group item by item.Receipt.SourceId into @group
			select @group.OrderByDescending((CachedReceipt item) => item.SortTimeUtc).ThenByDescending((CachedReceipt item) => item.Receipt.SuiteDirectory, StringComparer.OrdinalIgnoreCase).First())
		{
			SuiteResultReceipt receipt = item2.Receipt;
			SuiteCaseRow valueOrDefault = dictionary.GetValueOrDefault(receipt.SourceId);
			dictionary[receipt.SourceId] = new SuiteCaseRow(receipt.SourceId, Pick(receipt.CaseId, valueOrDefault?.CaseId), Pick(receipt.Title, valueOrDefault?.Title), Pick(receipt.Status, valueOrDefault?.Status ?? "planned-not-run"), receipt.Verdict, Pick(receipt.Coverage, valueOrDefault?.Coverage), Pick(receipt.ActualObservation, valueOrDefault?.ActualObservation), Math.Max(valueOrDefault?.Attempts ?? 0, 1 + receipt.RepeatRunIds.Count), Pick(receipt.Repeatability, valueOrDefault?.Repeatability), Pick(receipt.Signature, valueOrDefault?.Signature), Pick(receipt.Limitation, valueOrDefault?.Limitation), Pick(receipt.CaptureStatus, valueOrDefault?.CaptureStatus), Pick(receipt.JournalPath, valueOrDefault?.JournalPath), receipt.SuiteId, Pick(receipt.SourceCandidate, valueOrDefault?.SourceCandidate), valueOrDefault?.Preconditions ?? "", valueOrDefault?.ExpectedActions ?? "", valueOrDefault?.ExpectedObservation ?? "", valueOrDefault?.PassCriteria ?? "", valueOrDefault?.Capability ?? "", valueOrDefault?.PlanStatus ?? "");
		}
		SuiteCaseRow[] array = dictionary.Values.OrderBy((SuiteCaseRow row) => row.SourceId).ToArray();
		string summary = SuiteResultSummaryBuilder.Build(array).ToDisplayText();
		return new SuiteResultsSnapshot(array, updatedReceiptFiles, summary, (list.Count == 0) ? null : string.Join("\n", list.Distinct()));
	}

	private void RefreshCatalog(string root, string dataRoot, List<string> notices, CancellationToken token)
	{
		string path = Path.Combine(root, "case-catalog-100.json");
		if (!File.Exists(path))
		{
			path = Path.Combine(dataRoot, "case-catalog-100.json");
		}
		if (!File.Exists(path))
		{
			notices.Add("케이스 카탈로그가 없습니다. 저장된 요약 결과만 표시합니다.");
			return;
		}
		Stamp stamp = GetStamp(path);
		if (stamp == _catalogStamp)
		{
			return;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(ReadBounded(path, 8388608L, token));
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
			{
				throw new InvalidDataException("케이스 카탈로그 형식이 배열이 아닙니다.");
			}
			Dictionary<int, SuiteCaseRow> dictionary = new Dictionary<int, SuiteCaseRow>();
			foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
			{
				int num = Int(item, "sourceId", "SourceId", "Case #");
				if (num > 0)
				{
					dictionary[num] = new SuiteCaseRow(num, Str(item, "caseId", "CaseId", "Test Case ID"), Str(item, "title", "Title"), Or(item, "planned-not-run", "status", "Status", "Execution status", "planStatus", "PlanStatus"), Str(item, "verdict", "Verdict"), Str(item, "coverage", "Coverage"), Str(item, "actualObservation", "Actual observation"), Int(item, "attempts", "Attempts"), Str(item, "repeatability", "Repeatability"), Str(item, "signature", "Signature"), Str(item, "limitation", "Limitation"), Str(item, "captureStatus", "Capture status", "CaptureStatus"), Str(item, "journalPath", "Evidence journal", "JournalPath"), Str(item, "suiteId", "SourceSuiteRunId"), Str(item, "sourceCandidate", "Source candidate/run", "SourceCandidate"), Str(item, "preconditions", "Preconditions"), ArrayText(item, "expectedActions", "Expected actions"), Str(item, "expectedObservation", "Expected observation"), Str(item, "passCriteria", "Pass criteria"), Str(item, "capability", "Capability"), Or(item, "planned-not-run", "planStatus", "PlanStatus"));
				}
			}
			_catalog = dictionary;
			_catalogStamp = stamp;
		}
		catch (Exception ex) when (IsReadError(ex))
		{
			notices.Add("케이스 카탈로그 읽기 오류: " + ex.Message);
			_catalogStamp = stamp;
		}
	}

	private void RefreshBaseline(string root, List<string> notices, CancellationToken token)
	{
		string path = Path.Combine(root, "reports");
		if (!Directory.Exists(path))
		{
			return;
		}
		string text = null;
		DateTime dateTime = DateTime.MinValue;
		try
		{
			foreach (string item in Directory.EnumerateFiles(path, "results.json", SearchOption.AllDirectories))
			{
				token.ThrowIfCancellationRequested();
				if (Within(root, item))
				{
					FileInfo fileInfo = new FileInfo(item);
					long length = fileInfo.Length;
					bool flag = ((length <= 0 || length > 16777216) ? true : false);
					if (!flag && !(fileInfo.LastWriteTimeUtc <= dateTime))
					{
						dateTime = fileInfo.LastWriteTimeUtc;
						text = item;
					}
				}
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			notices.Add("이전 결과 파일 일부를 확인할 수 없습니다.");
		}
		if (text == null)
		{
			return;
		}
		Stamp stamp = GetStamp(text);
		if (stamp == _baselineStamp)
		{
			return;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(ReadBounded(text, 16777216L, token));
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			Dictionary<int, SuiteCaseRow> dictionary = new Dictionary<int, SuiteCaseRow>();
			foreach (JsonElement item2 in jsonDocument.RootElement.EnumerateArray())
			{
				int num = Int(item2, "SourceId", "sourceId", "Case #");
				if (num > 0)
				{
					SuiteCaseRow valueOrDefault = _catalog.GetValueOrDefault(num);
					dictionary[num] = new SuiteCaseRow(num, Pick(Str(item2, "CaseId", "Test Case ID"), valueOrDefault?.CaseId), Pick(Str(item2, "Title"), valueOrDefault?.Title), Or(item2, "planned-not-run", "Status", "Execution status"), Str(item2, "Verdict"), Str(item2, "Coverage"), Str(item2, "ActualObservation", "Actual observation"), Int(item2, "Attempts"), Str(item2, "Repeatability"), Str(item2, "Signature"), Str(item2, "Limitation"), Str(item2, "CaptureStatus", "Capture status"), Str(item2, "JournalPath", "Evidence journal"), Str(item2, "SourceSuiteRunId"), Str(item2, "SourceCandidate", "Source candidate/run"), valueOrDefault?.Preconditions ?? "", valueOrDefault?.ExpectedActions ?? Str(item2, "Expected actions"), valueOrDefault?.ExpectedObservation ?? Str(item2, "Expected observation"), valueOrDefault?.PassCriteria ?? Str(item2, "Pass criteria"), valueOrDefault?.Capability ?? Str(item2, "Capability"), valueOrDefault?.PlanStatus ?? "");
				}
			}
			_baseline = dictionary;
			_baselineDirectory = Path.GetDirectoryName(text);
			_baselineStamp = stamp;
		}
		catch (Exception ex2) when (IsReadError(ex2))
		{
			notices.Add("이전 결과 요약 읽기 오류: " + ex2.Message);
			_baselineStamp = stamp;
		}
	}

	private int RefreshSuiteResults(string root, string dataRoot, List<string> notices, CancellationToken token)
	{
		string[] array = new string[2]
		{
			Path.Combine(root, "suite-runs"),
			Path.Combine(dataRoot, "suite-runs")
		}.Where(Directory.Exists).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (array.Length == 0)
		{
			return 0;
		}
		HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int num = 0;
		try
		{
			foreach (string directory in array.SelectMany(Directory.EnumerateDirectories).Distinct<string>(StringComparer.OrdinalIgnoreCase))
			{
				token.ThrowIfCancellationRequested();
				string path = Path.Combine(directory, "results.json");
				if (!File.Exists(path) || !Within(root, path))
				{
					continue;
				}
				found.Add(directory);
				Stamp stamp = GetStamp(path);
				if (_suites.TryGetValue(directory, out CachedSuite value) && value.Stamp == stamp)
				{
					continue;
				}
				try
				{
					using JsonDocument jsonDocument = JsonDocument.Parse(ReadBounded(path, 16777216L, token));
					if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
					{
						throw new InvalidDataException("실행 결과가 배열 형식이 아닙니다.");
					}
					CachedReceipt[] receipts = (from item in jsonDocument.RootElement.EnumerateArray()
						select ParseReceipt(item, Path.GetFileName(directory), directory, stamp.LastWriteUtc) into item
						where item.Receipt.SourceId > 0
						select item).ToArray();
					_suites[directory] = new CachedSuite(stamp, receipts);
					num++;
				}
				catch (Exception ex) when (IsReadError(ex))
				{
					notices.Add("실행 결과 파일 읽기 오류 (" + Path.GetFileName(directory) + "): " + ex.Message);
					_suites[directory] = new CachedSuite(stamp, Array.Empty<CachedReceipt>());
				}
			}
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			notices.Add("실행 결과 폴더의 일부를 확인할 수 없습니다.");
		}
		string[] array2 = _suites.Keys.Where((string key) => !found.Contains(key)).ToArray();
		foreach (string key2 in array2)
		{
			_suites.Remove(key2);
		}
		return num;
	}

	private static CachedReceipt ParseReceipt(JsonElement item, string suiteId, string suiteDirectory, DateTime lastWriteUtc)
	{
		DateTimeOffset result;
		DateTimeOffset? dateTimeOffset = (DateTimeOffset.TryParse(Str(item, "recordedAt", "RecordedAt"), out result) ? new DateTimeOffset?(result) : null);
		return new CachedReceipt(new SuiteResultReceipt(Int(item, "SourceId", "sourceId", "Case #"), Str(item, "CaseId", "caseId", "Test Case ID"), Str(item, "Title", "title"), Str(item, "Status", "status", "Execution status"), Str(item, "Verdict", "verdict"), Str(item, "Coverage", "coverage"), Str(item, "ActualObservation", "actualObservation", "Actual observation"), NullableStr(item, "FirstRunId", "firstRunId"), StringList(item, "RepeatRunIds", "repeatRunIds"), Str(item, "Repeatability", "repeatability"), Str(item, "Signature", "signature"), Str(item, "Limitation", "limitation"), Str(item, "CaptureStatus", "captureStatus", "Capture status"), Str(item, "JournalPath", "journalPath", "Evidence journal"), Str(item, "SourceCandidate", "sourceCandidate", "Source candidate/run"), dateTimeOffset, suiteId, suiteDirectory), dateTimeOffset?.UtcDateTime ?? lastWriteUtc);
	}

	private IReadOnlyList<SuiteAttempt> ReadAttempts(string root, string dataRoot, SuiteCaseRow row, CancellationToken token)
	{
		List<SuiteAttempt> list = new List<SuiteAttempt>();
		foreach (string item in (string.IsNullOrWhiteSpace(row.SuiteId) ? ((IEnumerable<string>)Array.Empty<string>()) : ((IEnumerable<string>)new string[2]
		{
			Path.Combine(root, "suite-runs", row.SuiteId),
			Path.Combine(dataRoot, "suite-runs", row.SuiteId)
		})).Where(Directory.Exists).Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			string searchPattern = $"case-{row.SourceId:000}-attempt-*.jsonl";
			foreach (string item2 in Directory.EnumerateFiles(item, searchPattern).OrderBy(AttemptNumber))
			{
				token.ThrowIfCancellationRequested();
				if (Within(root, item2))
				{
					SuiteAttempt suiteAttempt = ParseAttempt(item2, row.SourceId);
					if ((object)suiteAttempt != null)
					{
						list.Add(suiteAttempt);
					}
				}
			}
		}
		if (list.Count == 0 && !string.IsNullOrWhiteSpace(row.JournalPath))
		{
			string text = ResolveEvidence(root, row.JournalPath, Path.Combine(root, "suite-runs", row.SuiteId ?? ""), Path.Combine(dataRoot, "suite-runs", row.SuiteId ?? ""), _baselineDirectory);
			if (text != null && File.Exists(text))
			{
				SuiteAttempt suiteAttempt2 = ParseAttempt(text, row.SourceId);
				if ((object)suiteAttempt2 != null)
				{
					list.Add(suiteAttempt2);
				}
			}
		}
		return list.OrderBy((SuiteAttempt item) => item.AttemptNumber).ToArray();
	}

	private static SuiteAttempt? ParseAttempt(string file, int sourceId)
	{
		int num = AttemptNumber(file);
		string runId = $"case-{sourceId:000}-{num}";
		string summary = "실행 원본";
		try
		{
			if (new FileInfo(file).Length > 2097152)
			{
				return null;
			}
			using StreamReader streamReader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
			for (int i = 0; i < 30; i++)
			{
				string text = streamReader.ReadLine();
				if (text == null)
				{
					break;
				}
				if (Encoding.UTF8.GetByteCount(text) > 262144)
				{
					continue;
				}
				using JsonDocument jsonDocument = JsonDocument.Parse(text);
				string text2 = Str(jsonDocument.RootElement, "runId", "RunId");
				if (!string.IsNullOrWhiteSpace(text2))
				{
					runId = text2;
				}
				if (TryProp(jsonDocument.RootElement, out var value, "action"))
				{
					string text3 = Str(value, "StepId", "stepId");
					if (text3 != null && text3.Length > 0)
					{
						summary = text3;
					}
				}
			}
			return new SuiteAttempt(runId, num, file, summary);
		}
		catch (Exception ex) when (IsReadError(ex) || ex is JsonException)
		{
			return null;
		}
	}

	private static string FormatJournal(string root, SuiteAttempt attempt, CancellationToken token)
	{
		if (!Within(root, attempt.JournalPath))
		{
			return "보안상 프로젝트 폴더 밖의 증거 파일은 열지 않았습니다.";
		}
		if (!File.Exists(attempt.JournalPath))
		{
			return "연결된 journal 파일을 찾을 수 없습니다.";
		}
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			using FileStream fileStream = new FileStream(attempt.JournalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			bool flag = fileStream.Length > 2097152;
			byte[] array = new byte[(int)Math.Min(fileStream.Length, 2097152L)];
			int i;
			int num;
			for (i = 0; i < array.Length; i += num)
			{
				token.ThrowIfCancellationRequested();
				num = fileStream.Read(array, i, array.Length - i);
				if (num == 0)
				{
					break;
				}
			}
			using StringReader stringReader = new StringReader(Encoding.UTF8.GetString(array, 0, i));
			if (flag)
			{
				stringBuilder.AppendLine("journal가 2 MB보다 커 앞부분만 표시합니다.");
			}
			for (int j = 0; j < 500; j++)
			{
				if (stringBuilder.Length >= 120000)
				{
					break;
				}
				string text = stringReader.ReadLine();
				if (text == null)
				{
					break;
				}
				token.ThrowIfCancellationRequested();
				if (Encoding.UTF8.GetByteCount(text) > 262144)
				{
					stringBuilder.AppendLine("[긴 이벤트 생략]");
					continue;
				}
				try
				{
					using JsonDocument jsonDocument = JsonDocument.Parse(text);
					FormatEvent(stringBuilder, jsonDocument.RootElement);
				}
				catch (JsonException)
				{
					stringBuilder.AppendLine("[완성되지 않은 journal 행 생략]");
				}
			}
			if (flag || stringReader.Peek() >= 0)
			{
				stringBuilder.AppendLine("… 안전 표시 한도에서 나머지 이벤트를 생략했습니다.");
			}
		}
		catch (Exception ex2) when (IsReadError(ex2))
		{
			return "증거를 읽을 수 없습니다: " + ex2.Message;
		}
		if (stringBuilder.Length != 0)
		{
			return stringBuilder.ToString();
		}
		return "표시할 journal 이벤트가 없습니다.";
	}

	private static void FormatEvent(StringBuilder output, JsonElement item)
	{
		string text = Str(item, "kind", "Kind");
		if (text == "case-plan")
		{
			output.AppendLine("계획 · " + Str(item, "timestamp", "Timestamp"));
			return;
		}
		if (text == "action" && TryProp(item, out var value, "action"))
		{
			string value2 = Str(value, "StepId", "stepId");
			string value3 = Str(value, "Action", "action");
			string text2 = Str(value, "Succeeded", "succeeded");
			StringBuilder stringBuilder = output;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(6, 3, stringBuilder);
			handler.AppendFormatted(value2);
			handler.AppendLiteral(" · ");
			handler.AppendFormatted(value3);
			handler.AppendLiteral(" · ");
			handler.AppendFormatted(text2.Equals("True", StringComparison.OrdinalIgnoreCase) ? "성공" : "확인 필요");
			stringBuilder2.AppendLine(ref handler);
			string text3 = Str(value, "Detail", "detail");
			if (text3.Length > 0)
			{
				output.AppendLine("  " + text3);
			}
			if (TryProp(value, out var value4, "Entity"))
			{
				FormatEntity(output, value4, "  ");
			}
			return;
		}
		bool flag = ((text == "before" || text == "after") ? true : false);
		if (flag && TryProp(item, out var value5, "entity"))
		{
			FormatEntity(output, value5, (text == "before") ? "시작 · " : "종료 · ");
		}
		else if (text == "attempt-verdict")
		{
			StringBuilder stringBuilder = output;
			StringBuilder stringBuilder3 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(18, 2, stringBuilder);
			handler.AppendLiteral("판정 · ");
			handler.AppendFormatted(Str(item, "Verdict", "verdict"));
			handler.AppendLiteral(" · signature=");
			handler.AppendFormatted(Str(item, "signature", "Signature"));
			stringBuilder3.AppendLine(ref handler);
			string text4 = Str(item, "Limitation", "limitation");
			if (text4.Length > 0)
			{
				output.AppendLine("  제한: " + text4);
			}
		}
	}

	private static void FormatEntity(StringBuilder output, JsonElement entity, string prefix)
	{
		string value = Or(entity, "?", "RunEntityId", "runEntityId", "Uuid", "uuid");
		StringBuilder stringBuilder = output;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(22, 5, stringBuilder);
		handler.AppendFormatted(prefix);
		handler.AppendLiteral("엔더맨 ");
		handler.AppendFormatted(value);
		handler.AppendLiteral(" · ");
		handler.AppendFormatted(Or(entity, "unknown", "LifeState", "lifeState"));
		handler.AppendLiteral(" · HP ");
		handler.AppendFormatted(Or(entity, "?", "Health", "health"));
		handler.AppendLiteral(" · cause=");
		handler.AppendFormatted(Or(entity, "none", "DamageCause", "damageCause"));
		stringBuilder2.AppendLine(ref handler);
		JsonElement value3;
		if (TryProp(entity, out var value2, "Position", "position"))
		{
			stringBuilder = output;
			StringBuilder stringBuilder3 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 4, stringBuilder);
			handler.AppendLiteral("  위치=(");
			handler.AppendFormatted(Or(value2, "?", "X", "x"));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(Or(value2, "?", "Y", "y"));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(Or(value2, "?", "Z", "z"));
			handler.AppendLiteral(") ");
			handler.AppendFormatted(Str(value2, "Dimension", "dimension"));
			stringBuilder3.AppendLine(ref handler);
		}
		else if (TryProp(entity, out value3, "X", "x"))
		{
			stringBuilder = output;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 4, stringBuilder);
			handler.AppendLiteral("  위치=(");
			handler.AppendFormatted(Or(entity, "?", "X", "x"));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(Or(entity, "?", "Y", "y"));
			handler.AppendLiteral(", ");
			handler.AppendFormatted(Or(entity, "?", "Z", "z"));
			handler.AppendLiteral(") ");
			handler.AppendFormatted(Str(entity, "Dimension", "dimension"));
			stringBuilder4.AppendLine(ref handler);
		}
		if (!TryProp(entity, out var value4, "DamageEvents") || value4.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item in value4.EnumerateArray())
		{
			stringBuilder = output;
			StringBuilder stringBuilder5 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(21, 3, stringBuilder);
			handler.AppendLiteral("  피해 · ");
			handler.AppendFormatted(Str(item, "Cause", "cause"));
			handler.AppendLiteral(" · ");
			handler.AppendFormatted(Or(item, "?", "DamageTaken", "damageTaken"));
			handler.AppendLiteral(" HP · tick ");
			handler.AppendFormatted(Or(item, "?", "GameTick", "gameTick"));
			stringBuilder5.AppendLine(ref handler);
		}
	}

	private static string? ResolveEvidence(string root, string value, params string?[] bases)
	{
		try
		{
			if (Path.IsPathRooted(value))
			{
				string fullPath = Path.GetFullPath(value);
				return Within(root, fullPath) ? fullPath : null;
			}
			foreach (string item in bases.Where((string item) => !string.IsNullOrWhiteSpace(item)))
			{
				string fullPath2 = Path.GetFullPath(Path.Combine(item, value));
				if (Within(root, fullPath2) && File.Exists(fullPath2))
				{
					return fullPath2;
				}
			}
			return null;
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is IOException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static SuiteCaseRow Merge(SuiteCaseRow? old, SuiteCaseRow incoming)
	{
		if ((object)old == null)
		{
			return incoming;
		}
		return incoming with
		{
			CaseId = Pick(old.CaseId, incoming.CaseId),
			Title = Pick(old.Title, incoming.Title),
			Preconditions = Pick(old.Preconditions, incoming.Preconditions),
			ExpectedActions = Pick(old.ExpectedActions, incoming.ExpectedActions),
			ExpectedObservation = Pick(old.ExpectedObservation, incoming.ExpectedObservation),
			PassCriteria = Pick(old.PassCriteria, incoming.PassCriteria),
			Capability = Pick(old.Capability, incoming.Capability),
			Status = (string.Equals(old.Status, "planned-not-run", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(incoming.Status)) ? incoming.Status : Pick(old.Status, incoming.Status),
			Verdict = Pick(old.Verdict, incoming.Verdict),
			Coverage = Pick(old.Coverage, incoming.Coverage),
			ActualObservation = Pick(old.ActualObservation, incoming.ActualObservation),
			Attempts = Math.Max(old.Attempts, incoming.Attempts),
			Repeatability = Pick(old.Repeatability, incoming.Repeatability),
			Signature = Pick(old.Signature, incoming.Signature),
			Limitation = Pick(old.Limitation, incoming.Limitation),
			CaptureStatus = Pick(old.CaptureStatus, incoming.CaptureStatus),
			JournalPath = Pick(old.JournalPath, incoming.JournalPath),
			SuiteId = Pick(old.SuiteId, incoming.SuiteId),
			SourceCandidate = Pick(old.SourceCandidate, incoming.SourceCandidate),
			PlanStatus = Pick(old.PlanStatus, incoming.PlanStatus)
		};
	}

	private static string ReadBounded(string path, long limit, CancellationToken token)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (fileStream.Length > limit)
		{
			throw new InvalidDataException($"Файл больше лимита {limit / 1048576} MB.");
		}
		using MemoryStream memoryStream = new MemoryStream((int)fileStream.Length);
		byte[] array = new byte[32768];
		while (true)
		{
			token.ThrowIfCancellationRequested();
			int num = fileStream.Read(array, 0, array.Length);
			if (num == 0)
			{
				break;
			}
			memoryStream.Write(array, 0, num);
		}
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private static Stamp GetStamp(string path)
	{
		FileInfo fileInfo = new FileInfo(path);
		return new Stamp(fileInfo.LastWriteTimeUtc, fileInfo.Length);
	}

	private static bool Within(string root, string path)
	{
		try
		{
			ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			char reference = Path.DirectorySeparatorChar;
			string value = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
			return Path.GetFullPath(path).StartsWith(value, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is IOException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			return false;
		}
	}

	private static bool TryProp(JsonElement root, out JsonElement value, params string[] names)
	{
		if (root.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in root.EnumerateObject())
			{
				if (names.Any((string name) => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
				{
					value = property.Value;
					return true;
				}
			}
		}
		value = default(JsonElement);
		return false;
	}

	private static string Str(JsonElement root, params string[] names)
	{
		if (!TryProp(root, out var value, names))
		{
			return "";
		}
		switch (value.ValueKind)
		{
		case JsonValueKind.String:
			return value.GetString() ?? "";
		case JsonValueKind.Number:
		case JsonValueKind.True:
		case JsonValueKind.False:
			return value.ToString();
		default:
			return "";
		}
	}

	private static string Or(JsonElement root, string fallback, params string[] names)
	{
		return Pick(Str(root, names), fallback);
	}

	private static string Pick(string? first, string? second)
	{
		string? text;
		if (string.IsNullOrWhiteSpace(first))
		{
			text = second;
			if (text == null)
			{
				return "";
			}
		}
		else
		{
			text = first;
		}
		return text;
	}

	private static int Int(JsonElement root, params string[] names)
	{
		if (!TryProp(root, out var value, names) || !value.TryGetInt32(out var value2))
		{
			return 0;
		}
		return value2;
	}

	private static string ArrayText(JsonElement root, params string[] names)
	{
		if (!TryProp(root, out var value, names) || value.ValueKind != JsonValueKind.Array)
		{
			return Str(root, names);
		}
		return string.Join(" → ", from item in value.EnumerateArray()
			select (item.ValueKind != JsonValueKind.String) ? null : item.GetString() into item
			where !string.IsNullOrWhiteSpace(item)
			select item);
	}

	private static IReadOnlyList<string> StringList(JsonElement root, params string[] names)
	{
		if (!TryProp(root, out var value, names) || value.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<string>();
		}
		return (from item in value.EnumerateArray()
			select (item.ValueKind != JsonValueKind.String) ? "" : (item.GetString() ?? "") into item
			where item.Length > 0
			select item).ToArray();
	}

	private static string? NullableStr(JsonElement root, params string[] names)
	{
		if (!TryProp(root, out var value, names) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static bool IsReadError(Exception ex)
	{
		if (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidDataException || ex is ArgumentException)
		{
			return true;
		}
		return false;
	}

	private static bool IsExecuted(string status)
	{
		if (!status.StartsWith("executed-", StringComparison.OrdinalIgnoreCase) && !status.StartsWith("failure-", StringComparison.OrdinalIgnoreCase))
		{
			return status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool Is(string value, string expected)
	{
		return value.Equals(expected, StringComparison.OrdinalIgnoreCase);
	}

	private static int AttemptNumber(string path)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		int num = fileNameWithoutExtension.LastIndexOf("attempt-", StringComparison.OrdinalIgnoreCase);
		if (num >= 0)
		{
			string text = fileNameWithoutExtension;
			int num2 = num + 8;
			if (int.TryParse(text.Substring(num2, text.Length - num2), out var result))
			{
				return result;
			}
		}
		return 1;
	}
}
