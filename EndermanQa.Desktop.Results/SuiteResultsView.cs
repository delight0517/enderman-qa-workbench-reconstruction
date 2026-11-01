using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Input;
using EndermanQa.Desktop.Integrations;
using EndermanQa.Desktop.Persistence;

namespace EndermanQa.Desktop.Results;

public class SuiteResultsView : UserControl, INotifyPropertyChanged, IComponentConnector
{
	private const string SearchPrompt = "ID, 케이스명, 판정 검색";

	private readonly SuiteResultsIndex _index = new SuiteResultsIndex();

	private readonly ObservableCollection<SuiteCaseRow> _cases = new ObservableCollection<SuiteCaseRow>();

	private readonly ICollectionView? _visibleCases;

	private readonly SuiteResultsLayoutModuleV1 _presentationModule;

	private CancellationTokenSource? _detailWork;

	private CancellationTokenSource? _refreshWork;

	private string? _projectRoot;

	private string? _projectDataRoot;

	private string? _pendingSelectedCaseId;

	private bool _disposed;

	public SuiteResultSummaryCounts CurrentSummaryCounts => SuiteResultSummaryBuilder.Build(_cases);

	public event EventHandler? SummaryUpdated;

	public event EventHandler<SuiteEvidenceChangedEventArgs>? SelectedEvidenceChanged;

	private bool _refreshing;

	private bool _exporting;

	private bool _detailWorkBusy;

	private WorkbookViewerWindow? _workbookViewer;

	private SuiteCaseRow? _selectedCase;

	private double _journalHeight;

	private double _journalMinHeight;

	private double _journalMaxHeight;

	internal TextBlock SummaryText;

	internal Button OpenWorkbookButton;

	internal Button ExportSuiteButton;

	internal Button OpenSuiteFolderButton;

	internal Button RefreshButton;

	internal TextBox SearchBox;

	internal ComboBox StatusFilter;

	internal DataGrid CasesGrid;

	internal TextBlock DetailTitle;

	internal TextBlock DetailStatus;

	internal TextBlock ExpectedText;

	internal TextBlock ActualText;

	internal TextBlock CoverageText;

	internal TextBlock RepeatabilityText;

	internal DataGrid AttemptsGrid;

	internal TextBox JournalText;

	internal TextBlock EvidenceText;

	internal TextBlock NoticeText;

	private bool _contentLoaded;

	public ICollectionView? VisibleCases => _visibleCases;

	public SuiteCaseRow? SelectedCase
	{
		get
		{
			return _selectedCase;
		}
		private set
		{
			if ((object)_selectedCase != value)
			{
				_selectedCase = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedCase"));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public SuiteResultsView()
	{
		InitializeComponent();
		_journalHeight = JournalText.Height;
		_journalMinHeight = JournalText.MinHeight;
		_journalMaxHeight = JournalText.MaxHeight;
		_presentationModule = new SuiteResultsLayoutModuleV1(this);
		_presentationModule.Initialize();
		SuiteResultsPresentation.Apply(this);
		SummaryText.TextWrapping = TextWrapping.Wrap;
		base.DataContext = this;
		_visibleCases = CollectionViewSource.GetDefaultView(_cases);
		if (_visibleCases != null)
		{
			_visibleCases.Filter = FilterCase;
		}
		EnsureFollowUpFilter();
		UpdateFollowUpFilterCount();
		SetSearchPromptIfEmpty();
	}

	internal void AttachExportButtons(Button excelButton, Button jiraButton)
	{
		// Legacy single-file and Jira CSV actions stay in their original manual QA panels.
		// The 100-case suite has one portfolio bundle action instead.
		_presentationModule.ConfigureExportButtons(excelButton, jiraButton, includeLegacyExports: false);
	}

	internal bool HasPendingPresentationWork => _refreshing || _exporting || _detailWorkBusy;

	internal SuiteResultsLayoutModuleV1 PresentationModule => _presentationModule;

	internal void StartPresentationProfileModule(string appDataRoot)
	{
		ThrowIfDisposed();
		_presentationModule.StartWatching(appDataRoot);
	}

	public async Task LoadProjectAsync(string projectRoot, string projectDataRoot, CancellationToken cancellationToken = default(CancellationToken))
	{
		ThrowIfDisposed();
		ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot, "projectRoot");
		ArgumentException.ThrowIfNullOrWhiteSpace(projectDataRoot, "projectDataRoot");
		_projectRoot = Path.GetFullPath(projectRoot);
		_projectDataRoot = Path.GetFullPath(projectDataRoot);
		await RefreshAsync(cancellationToken);
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ThrowIfDisposed();
		if (_projectRoot == null)
		{
			NoticeText.Text = "프로젝트 결과 폴더가 선택되지 않았습니다.";
		}
		else
		{
			if (_refreshing)
			{
				return;
			}
			_refreshing = true;
			RefreshButton.IsEnabled = false;
			string selection = SelectedCase?.CaseId ?? _pendingSelectedCaseId;
			_refreshWork?.Cancel();
			_refreshWork?.Dispose();
			_refreshWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			CancellationTokenSource work = _refreshWork;
			try
			{
				SuiteResultsSnapshot suiteResultsSnapshot = await _index.RefreshAsync(_projectRoot, _projectDataRoot ?? _projectRoot, work.Token);
				if (_disposed || work.IsCancellationRequested || _refreshWork != work)
				{
					return;
				}
				_cases.Clear();
				IReadOnlyDictionary<int, string> followUpGuidance = SuiteFollowUpGuidance.Read(_projectRoot, _projectDataRoot ?? _projectRoot);
				foreach (SuiteCaseRow @case in suiteResultsSnapshot.Cases)
				{
					_cases.Add(followUpGuidance.TryGetValue(@case.SourceId, out string? guidance)
						? @case with { FollowUpGuidance = guidance }
						: @case);
				}
				UpdateFollowUpFilterCount();
				SummaryText.Text = suiteResultsSnapshot.Summary + ((suiteResultsSnapshot.UpdatedReceiptFiles > 0) ? $" · 이번에 색인한 결과 파일 {suiteResultsSnapshot.UpdatedReceiptFiles}개" : " · 변경된 실행 결과 없음");
				SummaryUpdated?.Invoke(this, EventArgs.Empty);
				NoticeText.Text = suiteResultsSnapshot.Notice ?? "";
				_visibleCases?.Refresh();
				SuiteCaseRow? suiteCaseRow2 = _cases.FirstOrDefault((SuiteCaseRow row) => row.CaseId.Equals(selection, StringComparison.OrdinalIgnoreCase));
				if (suiteCaseRow2 != null && _visibleCases?.Contains(suiteCaseRow2) == true)
				{
					_pendingSelectedCaseId = null;
					CasesGrid.SelectedItem = suiteCaseRow2;
				}
				else
				{
					_pendingSelectedCaseId = null;
					CasesGrid.SelectedItem = _visibleCases?.Cast<SuiteCaseRow>().FirstOrDefault();
				}
				if (CasesGrid.SelectedItem is SuiteCaseRow row2)
				{
					await SelectCaseAsync(row2, work.Token);
				}
				else
				{
					ClearSelectionDetails();
				}
			}
			catch (OperationCanceledException) when (work.IsCancellationRequested)
			{
				if (!_disposed)
				{
					NoticeText.Text = "결과 새로고침이 취소되었습니다.";
				}
			}
			catch (Exception ex2)
			{
				if (!_disposed)
				{
					NoticeText.Text = "결과를 표시하지 못했습니다: " + ex2.Message;
				}
			}
			finally
			{
				_refreshing = false;
				if (!_disposed)
				{
					RefreshButton.IsEnabled = true;
				}
				if (_refreshWork == work)
				{
					_refreshWork.Dispose();
					_refreshWork = null;
				}
			}
		}
	}

	public SuiteResultsViewState SnapshotState()
	{
		return new SuiteResultsViewState(SelectedCase?.CaseId, IsSearchPrompt(SearchBox.Text) ? "" : SearchBox.Text, (StatusFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all");
	}

	public void RestoreState(SuiteResultsViewState state)
	{
		SuiteResultsViewState state2 = state;
		ThrowIfDisposed();
		SearchBox.Text = state2.SearchText ?? "";
		SetSearchPromptIfEmpty();
		StatusFilter.SelectedItem = StatusFilter.Items.OfType<ComboBoxItem>().FirstOrDefault((ComboBoxItem item) => string.Equals(item.Tag?.ToString(), state2.StatusFilter, StringComparison.OrdinalIgnoreCase)) ?? StatusFilter.Items[0];
		_visibleCases?.Refresh();
		if (_cases.Count == 0 && !string.IsNullOrWhiteSpace(state2.SelectedCaseId))
		{
			_pendingSelectedCaseId = state2.SelectedCaseId;
			return;
		}
		SuiteCaseRow? suiteCaseRow = _cases.FirstOrDefault((SuiteCaseRow item) => item.CaseId.Equals(state2.SelectedCaseId, StringComparison.OrdinalIgnoreCase));
		_pendingSelectedCaseId = null;
		CasesGrid.SelectedItem = suiteCaseRow != null && _visibleCases?.Contains(suiteCaseRow) == true
			? suiteCaseRow
			: _visibleCases?.Cast<SuiteCaseRow>().FirstOrDefault();
		if (CasesGrid.SelectedItem is not SuiteCaseRow)
		{
			ClearSelectionDetails();
		}
	}

	public void DisposeModule()
	{
		if (!_disposed)
		{
			_presentationModule.Dispose();
			_disposed = true;
			_detailWork?.Cancel();
			_detailWork?.Dispose();
			_detailWork = null;
			_refreshWork?.Cancel();
			_workbookViewer?.Close();
			_workbookViewer = null;
			CasesGrid.SelectedItem = null;
			AttemptsGrid.SelectedItem = null;
			_cases.Clear();
			SelectedCase = null;
		}
	}

	private bool FilterCase(object candidate)
	{
		if (!(candidate is SuiteCaseRow suiteCaseRow))
		{
			return false;
		}
		string text = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
		if (!text.Equals("all", StringComparison.OrdinalIgnoreCase) && text switch
		{
			"pass" => suiteCaseRow.Verdict.Equals("Pass", StringComparison.OrdinalIgnoreCase) ? 1 : 0, 
			"fail" => suiteCaseRow.Verdict.Equals("Fail", StringComparison.OrdinalIgnoreCase) ? 1 : 0, 
			"uncertain" => suiteCaseRow.Verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase) ? 1 : 0, 
			"follow-up" => (suiteCaseRow.Verdict.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase) || !IsExecuted(suiteCaseRow.Status)) ? 1 : 0,
			"not-run" => (!IsExecuted(suiteCaseRow.Status)) ? 1 : 0, 
			_ => 1, 
		} == 0)
		{
			return false;
		}
		string value = SearchBox?.Text?.Trim();
		if (IsSearchPrompt(value)) value = "";
		if (!string.IsNullOrEmpty(value) && !suiteCaseRow.IdDisplay.Contains(value, StringComparison.OrdinalIgnoreCase) && !suiteCaseRow.CaseId.Contains(value, StringComparison.OrdinalIgnoreCase) && !suiteCaseRow.Title.Contains(value, StringComparison.OrdinalIgnoreCase) && !suiteCaseRow.StatusDisplay.Contains(value, StringComparison.OrdinalIgnoreCase) && !suiteCaseRow.VerdictDisplay.Contains(value, StringComparison.OrdinalIgnoreCase) && !suiteCaseRow.Coverage.Contains(value, StringComparison.OrdinalIgnoreCase))
		{
			return suiteCaseRow.ActualObservation.Contains(value, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private void EnsureFollowUpFilter()
	{
		if (StatusFilter.Items.OfType<ComboBoxItem>().Any(item => string.Equals(item.Tag?.ToString(), "follow-up", StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}
		var item = new ComboBoxItem { Content = "추가 확인", Tag = "follow-up", ToolTip = "미확정 및 미실행 케이스만 표시" };
		StatusFilter.Items.Insert(Math.Min(4, StatusFilter.Items.Count), item);
	}

	private void UpdateFollowUpFilterCount()
	{
		var item = StatusFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(candidate => string.Equals(candidate.Tag?.ToString(), "follow-up", StringComparison.OrdinalIgnoreCase));
		if (item != null)
		{
			var counts = SuiteResultSummaryBuilder.Build(_cases);
			item.Content = $"추가 확인 · {counts.Inconclusive + counts.NotRun}";
		}
	}

	private static bool IsExecuted(string status)
	{
		if (!status.StartsWith("executed-", StringComparison.OrdinalIgnoreCase) && !status.StartsWith("failure-", StringComparison.OrdinalIgnoreCase))
		{
			return status.Equals("anomaly-unconfirmed", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		SearchBox.SetResourceReference(Control.ForegroundProperty, IsSearchPrompt(SearchBox.Text) ? "Theme.TextMuted" : "Theme.TextPrimary");
		RefreshFilterAndSelection();
	}

	private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (IsSearchPrompt(SearchBox.Text)) SearchBox.Clear();
	}

	private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		SetSearchPromptIfEmpty();
	}

	private void SetSearchPromptIfEmpty()
	{
		if (string.IsNullOrWhiteSpace(SearchBox.Text) && !SearchBox.IsKeyboardFocusWithin)
			SearchBox.Text = SearchPrompt;
	}

	private static bool IsSearchPrompt(string? value) => string.Equals(value?.Trim(), SearchPrompt, StringComparison.Ordinal);

	private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		RefreshFilterAndSelection();
	}

	private void RefreshFilterAndSelection()
	{
		_visibleCases?.Refresh();
		if (_visibleCases == null)
		{
			return;
		}
		if (CasesGrid.SelectedItem is SuiteCaseRow selected && _visibleCases.Contains(selected))
		{
			return;
		}
		CasesGrid.SelectedItem = _visibleCases.Cast<SuiteCaseRow>().FirstOrDefault();
		if (CasesGrid.SelectedItem is not SuiteCaseRow)
		{
			ClearSelectionDetails();
		}
	}

	private void ClearSelectionDetails()
	{
		_detailWork?.Cancel();
		_detailWork?.Dispose();
		_detailWork = null;
		_detailWorkBusy = false;
		SelectedCase = null;
		DetailTitle.Text = "검색 결과가 없습니다";
		DetailStatus.Text = "";
		ExpectedText.Text = "";
		ActualText.Text = "검색 조건에 맞는 QA 케이스가 없습니다.";
		CoverageText.Text = "";
		RepeatabilityText.Text = "";
		EvidenceText.Text = "";
		JournalText.Text = "";
		AttemptsGrid.ItemsSource = null;
		AttemptsGrid.Visibility = Visibility.Collapsed;
	}

	private async void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await RefreshAsync();
		}
		catch (Exception ex)
		{
			NoticeText.Text = "결과 새로고침 오류: " + ex.Message;
		}
	}

	private async void ExportSuiteButton_Click(object sender, RoutedEventArgs e)
	{
		await ExportPortfolioAsync();
	}

	public async Task ExportPortfolioAsync()
	{
		if (_projectRoot == null || _cases.Count == 0)
		{
			NoticeText.Text = "먼저 프로젝트 결과를 불러오세요.";
			return;
		}
		ExportSuiteButton.IsEnabled = false;
		_exporting = true;
		try
		{
			List<SuiteQaExportRow> rows = new List<SuiteQaExportRow>(_cases.Count);
			foreach (SuiteCaseRow row in _cases.OrderBy((SuiteCaseRow item) => item.SourceId))
			{
				IEnumerable<string> values = (from attempt in await _index.ReadAttemptsAsync(row)
					select attempt.JournalPath into path
					where !string.IsNullOrWhiteSpace(path)
					select path).Distinct<string>(StringComparer.OrdinalIgnoreCase);
				rows.Add(new SuiteQaExportRow(row.IdDisplay, row.CaseId, row.Title, row.Status, row.Verdict, row.Preconditions, row.ExpectedActions, row.ExpectedObservation, row.PassCriteria, row.ActualObservation, row.Coverage, row.Attempts.ToString(), row.Repeatability, row.Limitation, row.CaptureStatus, row.SourceCandidate, row.SuiteId, string.Join(Environment.NewLine, values), string.Join(Environment.NewLine, ResolveCaseAttachments(row).Select(item => item.Path)), row.FollowUpGuidance));
			}
			var result = await Task.Run(() => QaPortfolioExportService.Export(_projectRoot, rows));
			NoticeText.Text = $"QA 포트폴리오 완료 · {result.CaseCount}개 케이스 · 증거 {result.IncludedEvidenceCount}개 · 안내: {result.GuidanceSource}\n폴더: {result.FolderPath}\nZIP: {result.ZipPath}";
		}
		catch (Exception ex)
		{
			NoticeText.Text = "QA 포트폴리오 내보내기 실패: " + ex.Message;
		}
		finally
		{
			_exporting = false;
			if (!_disposed)
			{
				ExportSuiteButton.IsEnabled = true;
			}
		}
	}

	private IReadOnlyList<SuiteEvidenceAttachment> ResolveCaseAttachments(SuiteCaseRow row)
	{
		var attachments = new List<SuiteEvidenceAttachment>(ResolveCaptureAttachments(row.CaptureStatus));
		if (string.IsNullOrWhiteSpace(_projectDataRoot)) return attachments;
		try
		{
			var recordsRoot = Path.Combine(_projectDataRoot, "manual-cases");
			if (Directory.Exists(_projectDataRoot) && (File.GetAttributes(_projectDataRoot) & FileAttributes.ReparsePoint) != 0) return attachments;
			if (Directory.Exists(recordsRoot) && ((File.GetAttributes(recordsRoot) & FileAttributes.ReparsePoint) != 0
				|| Directory.EnumerateFiles(recordsRoot, "*.json").Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))) return attachments;
			var store = new ManualQaRecordStore(_projectDataRoot);
			foreach (var record in store.LoadAll().Where(record => string.Equals(record.CaseId, row.CaseId, StringComparison.OrdinalIgnoreCase)))
			{
				foreach (var evidence in record.Evidence ?? Array.Empty<ManualEvidence>())
				{
					if (!evidence.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
						&& !evidence.MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) continue;
					try
					{
						var path = store.ResolveEvidencePath(record.CaseId, evidence);
						if (IsPreviewMedia(path)) attachments.Add(new SuiteEvidenceAttachment(path, evidence.Sha256, evidence.FileName));
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { }
				}
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
		return attachments.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
	}

	private IReadOnlyList<SuiteEvidenceAttachment> ResolveCaptureAttachments(string captureStatus)
	{
		if (string.IsNullOrWhiteSpace(_projectDataRoot) || string.IsNullOrWhiteSpace(captureStatus))
		{
			return Array.Empty<SuiteEvidenceAttachment>();
		}
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(_projectDataRoot).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string text = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		List<SuiteEvidenceAttachment> list = new List<SuiteEvidenceAttachment>();
		int num = 0;
		while (num < captureStatus.Length)
		{
			int num2 = captureStatus.IndexOf("native-framebuffer:", num, StringComparison.OrdinalIgnoreCase);
			if (num2 < 0)
			{
				break;
			}
			int num3 = num2 + "native-framebuffer:".Length;
			int i;
			for (i = num3; i < captureStatus.Length && !char.IsWhiteSpace(captureStatus[i]) && captureStatus[i] != ';'; i++)
			{
			}
			num = i;
			if (i == num3)
			{
				continue;
			}
			int num4 = num3;
			string path = captureStatus.Substring(num4, i - num4).Replace('/', Path.DirectorySeparatorChar);
			string fullPath = Path.GetFullPath(Path.Combine(text, path));
			if (fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
			{
				ReadOnlySpan<char> readOnlySpan2 = Path.GetFullPath(Path.Combine(text, "evidence", "client-captures")).TrimEnd(Path.DirectorySeparatorChar);
				reference = Path.DirectorySeparatorChar;
				string value = string.Concat(readOnlySpan2, new ReadOnlySpan<char>(ref reference));
				if (fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath) && IsPreviewMedia(fullPath) && !ContainsReparsePoint(_projectDataRoot, fullPath))
				{
					int segmentEnd = captureStatus.IndexOf(';', num2);
					if (segmentEnd < 0) segmentEnd = captureStatus.Length;
					string segment = captureStatus.Substring(num2, segmentEnd - num2);
					string digest = ExtractSha256(segment);
					list.Add(new SuiteEvidenceAttachment(fullPath, digest));
				}
			}
		}
		return list.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
	}

	private static bool IsPreviewMedia(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".mp4" or ".wmv" or ".avi" or ".mov" or ".mkv" or ".webm";

	private static string ExtractSha256(string value)
	{
		int index = value.IndexOf("sha256=", StringComparison.OrdinalIgnoreCase);
		if (index < 0) return "";
		string token = value.Substring(index + 7).Trim();
		int end = token.IndexOfAny(new[] { ' ', '\t', '\r', '\n', ';' });
		token = end >= 0 ? token[..end] : token;
		return token.Length == 64 && token.All(Uri.IsHexDigit) ? token : "";
	}

	private static bool ContainsReparsePoint(string root, string path)
	{
		try
		{
			if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return true;
			string relative = Path.GetRelativePath(root, path);
			string current = root;
			foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
			{
				current = Path.Combine(current, part);
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
			}
			return false;
		}
		catch (IOException) { return true; }
		catch (UnauthorizedAccessException) { return true; }
	}

	private void OpenWorkbookButton_Click(object sender, RoutedEventArgs e)
	{
		if (!_disposed)
		{
			ShowWorkbookViewer();
		}
	}

	private void ShowWorkbookViewer(string? workbookPath = null)
	{
		if (_workbookViewer == null)
		{
			_workbookViewer = new WorkbookViewerWindow(workbookPath, _projectRoot)
			{
				Owner = Window.GetWindow(this)
			};
			_workbookViewer.Closed += delegate
			{
				_workbookViewer = null;
			};
		}
		else if (!string.IsNullOrWhiteSpace(workbookPath))
		{
			_workbookViewer.OpenWorkbookFile(workbookPath);
		}
		if (!_workbookViewer.IsVisible)
		{
			_workbookViewer.Show();
		}
		_workbookViewer.Activate();
	}

	private void OpenSuiteFolderButton_Click(object sender, RoutedEventArgs e)
	{
		if (_projectRoot == null)
		{
			return;
		}
		string text = Path.Combine(_projectRoot, "suite-runs");
		if (!Directory.Exists(text) && _projectDataRoot != null)
		{
			text = Path.Combine(_projectDataRoot, "suite-runs");
		}
		string text2 = (Directory.Exists(text) ? text : _projectRoot);
		try
		{
			Process.Start(new ProcessStartInfo(text2)
			{
				UseShellExecute = true
			});
			NoticeText.Text = "QA 실행 파일 폴더를 열었습니다: " + text2;
		}
		catch (Exception ex)
		{
			NoticeText.Text = "결과 폴더를 열지 못했습니다: " + ex.Message;
		}
	}

	private async void CasesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(CasesGrid.SelectedItem is SuiteCaseRow row))
		{
			return;
		}
		try
		{
			await SelectCaseAsync(row, CancellationToken.None);
		}
		catch (Exception ex)
		{
			NoticeText.Text = "케이스 상세 기록을 읽지 못했습니다: " + ex.Message;
		}
	}

	private async Task SelectCaseAsync(SuiteCaseRow row, CancellationToken cancellationToken)
	{
		if (_disposed)
		{
			return;
		}
		SelectedCase = row;
		SelectedEvidenceChanged?.Invoke(this, new SuiteEvidenceChangedEventArgs(ResolveCaseAttachments(row)));
		DetailTitle.Text = row.IdDisplay + " · " + row.Title;
		string decisionSummary = row.StatusDisplay.Contains(row.VerdictDisplay, StringComparison.OrdinalIgnoreCase)
			? row.StatusDisplay
			: $"{row.StatusDisplay} / {row.VerdictDisplay}";
		DetailStatus.Text = $"{decisionSummary} · {row.Status} · 시도 {row.Attempts}회";
		ExpectedText.Text = JoinNonEmpty((row.Preconditions.Length > 0) ? ("사전 조건: " + row.Preconditions) : "", (row.ExpectedActions.Length > 0) ? ("행동: " + row.ExpectedActions) : "", (row.ExpectedObservation.Length > 0) ? ("예상: " + row.ExpectedObservation) : "", (row.PassCriteria.Length > 0) ? ("통과 기준: " + row.PassCriteria) : "");
		ActualText.Text = ((row.ActualObservation.Length > 0) ? row.ActualObservation : "실행 관측이 저장되어 있지 않습니다.");
		CoverageText.Text = JoinNonEmpty((row.Coverage.Length > 0) ? row.Coverage : "실행 범위 정보가 없습니다.", (row.Limitation.Length > 0) ? ("제한: " + row.Limitation) : "", (row.FollowUpGuidance.Length > 0) ? row.FollowUpGuidance : "", (row.SourceCandidate.Length > 0) ? ("후보/기준: " + row.SourceCandidate) : "", (row.Signature.Length > 0) ? ("반복 서명: " + row.Signature) : "");
		RepeatabilityText.Text = ((row.Repeatability.Length > 0) ? ("반복성: " + row.Repeatability) : "반복 실행 정보가 없습니다.");
		EvidenceText.Text = EvidenceSummary(row);
		JournalText.Text = "왼쪽에서 케이스를 고르면 실행 원본 목록을 불러옵니다.";
		_detailWork?.Cancel();
		_detailWork?.Dispose();
		_detailWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationTokenSource work = _detailWork;
		_detailWorkBusy = true;
		try
		{
			IReadOnlyList<SuiteAttempt> readOnlyList = await _index.ReadAttemptsAsync(row, work.Token);
			if (!work.IsCancellationRequested && _detailWork == work && !(SelectedCase?.CaseId != row.CaseId))
			{
				AttemptsGrid.ItemsSource = readOnlyList;
				if (readOnlyList.Count == 0)
				{
					AttemptsGrid.Visibility = Visibility.Collapsed;
					JournalText.Height = 64;
					JournalText.MinHeight = 64;
					JournalText.MaxHeight = 64;
					JournalText.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
					JournalText.Text = ((row.Status.StartsWith("planned", StringComparison.OrdinalIgnoreCase) || row.Status.StartsWith("not-run", StringComparison.OrdinalIgnoreCase)) ? "아직 실행 journal이 없습니다. 이 케이스는 실행하지 않았습니다." : "이 케이스에 연결된 실행 시도 journal을 찾을 수 없습니다.");
				}
				else
				{
					AttemptsGrid.Visibility = Visibility.Visible;
					JournalText.Height = _journalHeight;
					JournalText.MinHeight = _journalMinHeight;
					JournalText.MaxHeight = _journalMaxHeight;
					JournalText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
					AttemptsGrid.SelectedItem = readOnlyList[readOnlyList.Count - 1];
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			if (_detailWork == work) _detailWorkBusy = false;
		}
	}

	private async void AttemptsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!(AttemptsGrid.SelectedItem is SuiteAttempt attempt) || _disposed)
		{
			return;
		}
		_detailWork?.Cancel();
		_detailWork?.Dispose();
		_detailWork = new CancellationTokenSource();
		CancellationTokenSource work = _detailWork;
		_detailWorkBusy = true;
		JournalText.Text = "실행 원본을 읽는 중…";
		try
		{
			string text = await _index.ReadJournalAsync(attempt, work.Token);
			if (!work.IsCancellationRequested && _detailWork == work)
			{
				JournalText.Text = text;
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			JournalText.Text = "실행 원본을 표시할 수 없습니다: " + ex2.Message;
		}
		finally
		{
			if (_detailWork == work) _detailWorkBusy = false;
		}
	}

	private static string EvidenceSummary(SuiteCaseRow row)
	{
		string value = ((row.CaptureStatus.Length == 0) ? "미실행 또는 저장된 캡처 상태 없음" : row.CaptureStatus);
		string value2 = ((row.JournalPath.Length == 0) ? "journal 경로 없음" : row.JournalPath);
		return $"스크린샷/영상: {value}{Environment.NewLine}원본 경로: {value2}";
	}

	private static string JoinNonEmpty(params string[] lines)
	{
		return string.Join(Environment.NewLine, lines.Where((string line) => !string.IsNullOrWhiteSpace(line)));
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("SuiteResultsView");
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "results/suiteresultsview.baml");
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			SummaryText = (TextBlock)target;
			break;
		case 2:
			OpenWorkbookButton = (Button)target;
			OpenWorkbookButton.Click += OpenWorkbookButton_Click;
			break;
		case 3:
			ExportSuiteButton = (Button)target;
			ExportSuiteButton.Click += ExportSuiteButton_Click;
			break;
		case 4:
			OpenSuiteFolderButton = (Button)target;
			OpenSuiteFolderButton.Click += OpenSuiteFolderButton_Click;
			break;
		case 5:
			RefreshButton = (Button)target;
			RefreshButton.Click += RefreshButton_Click;
			break;
		case 6:
			SearchBox = (TextBox)target;
			SearchBox.TextChanged += SearchBox_TextChanged;
			SearchBox.GotKeyboardFocus += SearchBox_GotKeyboardFocus;
			SearchBox.LostKeyboardFocus += SearchBox_LostKeyboardFocus;
			break;
		case 7:
			StatusFilter = (ComboBox)target;
			StatusFilter.SelectionChanged += StatusFilter_SelectionChanged;
			break;
		case 8:
			CasesGrid = (DataGrid)target;
			CasesGrid.SelectionChanged += CasesGrid_SelectionChanged;
			break;
		case 9:
			DetailTitle = (TextBlock)target;
			break;
		case 10:
			DetailStatus = (TextBlock)target;
			break;
		case 11:
			ExpectedText = (TextBlock)target;
			break;
		case 12:
			ActualText = (TextBlock)target;
			break;
		case 13:
			CoverageText = (TextBlock)target;
			break;
		case 14:
			RepeatabilityText = (TextBlock)target;
			break;
		case 15:
			AttemptsGrid = (DataGrid)target;
			AttemptsGrid.SelectionChanged += AttemptsGrid_SelectionChanged;
			break;
		case 16:
			JournalText = (TextBox)target;
			break;
		case 17:
			EvidenceText = (TextBlock)target;
			break;
		case 18:
			NoticeText = (TextBlock)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}

