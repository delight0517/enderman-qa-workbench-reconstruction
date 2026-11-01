using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.ProjectData;

public class MacroAndStorageView : UserControl, IComponentConnector
{
	private sealed record StorageRow(string Id, string Name, string RelativePath, int FileCount, long SizeBytes, bool Exists)
	{
		public string SizeText => FormatBytes(SizeBytes);
	}

	private string? _projectRoot;

	private string? _dataRoot;

	private string? _planPath;

	private ProjectQaMacroStore? _macroStore;

	private ProjectStorageManager? _storageManager;

	private CancellationTokenSource? _measureCancellation;

	private long _refreshGeneration;

	private bool _runActive;

	internal TextBlock ProjectLine;

	internal TextBox MacroNameBox;

	internal TextBox CaseIdsBox;

	internal ListBox MacroList;

	internal Button RunMacroButton;

	internal Button DeleteMacroButton;

	internal TextBlock MacroStatus;

	internal TextBlock StorageTotal;

	internal DataGrid StorageGrid;

	internal Button RecycleSelectedButton;

	internal TextBlock StorageStatus;

	private bool _contentLoaded;

	public Action<ProjectQaMacro>? MacroRunRequested { get; set; }

	public Func<bool>? ProjectDataMutationBlocked { get; set; }

	public MacroAndStorageView()
	{
		InitializeComponent();
	}

	public void SetNoProject()
	{
		_measureCancellation?.Cancel();
		_projectRoot = (_dataRoot = (_planPath = null));
		_macroStore = null;
		_storageManager = null;
		MacroList.ItemsSource = null;
		StorageGrid.ItemsSource = null;
		ProjectLine.Text = "QA 프로젝트를 선택하면 프로젝트별 매크로와 용량이 표시됩니다.";
		MacroStatus.Text = "프로젝트 미선택";
		StorageTotal.Text = "계산할 프로젝트가 없습니다.";
		StorageStatus.Text = string.Empty;
		UpdateMacroButtons();
	}

	public void SetProject(string projectRoot, string dataRoot, string? planPath, string displayName)
	{
		string text = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar);
		string text2 = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar);
		if (!text2.Equals(text, StringComparison.OrdinalIgnoreCase))
		{
			ReadOnlySpan<char> readOnlySpan = text;
			char reference = Path.DirectorySeparatorChar;
			if (!text2.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase))
			{
				throw new UnauthorizedAccessException("프로젝트 데이터 경로가 프로젝트 루트 밖에 있습니다.");
			}
		}
		_projectRoot = text;
		_dataRoot = text2;
		_planPath = (string.IsNullOrWhiteSpace(planPath) ? null : Path.GetFullPath(planPath));
		_macroStore = new ProjectQaMacroStore(text2);
		_storageManager = new ProjectStorageManager(text);
		ProjectLine.Text = displayName + " · " + text;
		MacroStatus.Text = ((_planPath == null) ? "게임 전용 케이스 계획이 없습니다. 게임별 매크로 작업대에서 등록된 어댑터를 선택하세요." : "매크로를 불러오는 중");
		StorageTotal.Text = "프로젝트 용량 계산 중";
		RefreshMacrosAsync();
		RefreshStorageAsync();
	}

	public void SetRunActive(bool active, string? status = null)
	{
		_runActive = active;
		DeleteMacroButton.IsEnabled = !active && MacroList.SelectedItem is ProjectQaMacro;
		RecycleSelectedButton.IsEnabled = !active;
		if (!string.IsNullOrWhiteSpace(status))
		{
			MacroStatus.Text = status;
		}
	}

	private async void RefreshMacros_Click(object sender, RoutedEventArgs e)
	{
		await RefreshMacrosAsync();
	}

	private async Task RefreshMacrosAsync()
	{
		if (_macroStore == null || _planPath == null)
		{
			return;
		}
		try
		{
			IReadOnlyList<ProjectQaMacro> readOnlyList = await Task.Run(() => _macroStore.List(_planPath));
			MacroList.ItemsSource = readOnlyList;
			MacroStatus.Text = $"{readOnlyList.Count}개 · 재실행은 AI 요청 없이 저장된 계획 사용";
			UpdateMacroButtons();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is JsonException) ? 1 : 0) != 0)
		{
			MacroList.ItemsSource = null;
			MacroStatus.Text = "매크로 읽기 오류: " + ex.Message;
			UpdateMacroButtons();
		}
	}

	private async void SaveMacro_Click(object sender, RoutedEventArgs e)
	{
		if (_macroStore == null || _planPath == null)
		{
			return;
		}
		try
		{
			int[] ids = ParseCaseIds(CaseIdsBox.Text);
			ProjectQaMacro macro = await Task.Run(() => _macroStore.Save(MacroNameBox.Text, ids, _planPath));
			CaseIdsBox.Clear();
			await RefreshMacrosAsync();
			MacroList.SelectedItem = ((IEnumerable<ProjectQaMacro>)MacroList.ItemsSource)?.FirstOrDefault((ProjectQaMacro x) => x.Id == macro.Id);
			MacroStatus.Text = $"저장됨 · #{string.Join(", #", macro.CaseIds.Select((int x) => x.ToString("000")))} · SHA-256 {macro.PlanSha256.Substring(0, 12)}…";
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is JsonException) ? 1 : 0) != 0)
		{
			MacroStatus.Text = "저장 실패: " + ex.Message;
		}
	}

	private void RunMacro_Click(object sender, RoutedEventArgs e)
	{
		if (!(MacroList.SelectedItem is ProjectQaMacro projectQaMacro) || _macroStore == null || _planPath == null)
		{
			return;
		}
		try
		{
			ProjectQaMacro obj = _macroStore.Load(projectQaMacro.Id, _planPath);
			if (MacroRunRequested == null)
			{
				throw new InvalidOperationException("QA 실행 연결이 아직 준비되지 않았습니다.");
			}
			MacroRunRequested(obj);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is InvalidOperationException || ex is JsonException) ? 1 : 0) != 0)
		{
			MacroStatus.Text = "실행할 수 없음: " + ex.Message;
		}
	}

	private async void DeleteMacro_Click(object sender, RoutedEventArgs e)
	{
		object selectedItem = MacroList.SelectedItem;
		ProjectQaMacro selected = selectedItem as ProjectQaMacro;
		if ((object)selected == null || _macroStore == null || MessageBox.Show("‘" + selected.Name + "’ 매크로 파일을 Windows 휴지통으로 이동할까요? QA 실행 결과는 삭제하지 않습니다.", "매크로 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		try
		{
			await Task.Run(delegate
			{
				_macroStore.Delete(selected.Id);
			});
			await RefreshMacrosAsync();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) ? 1 : 0) != 0)
		{
			MacroStatus.Text = "삭제 실패: " + ex.Message;
		}
	}

	private void MacroList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateMacroButtons();
	}

	private void UpdateMacroButtons()
	{
		bool flag = MacroList.SelectedItem is ProjectQaMacro;
		RunMacroButton.IsEnabled = flag;
		DeleteMacroButton.IsEnabled = flag && !_runActive;
		RunMacroButton.IsEnabled = flag && !_runActive;
	}

	private async void RefreshStorage_Click(object sender, RoutedEventArgs e)
	{
		await RefreshStorageAsync();
	}

	private async Task RefreshStorageAsync()
	{
		if (_storageManager == null)
		{
			return;
		}
		if (_runActive || (ProjectDataMutationBlocked?.Invoke() ?? false))
		{
			StorageStatus.Text = "QA·AI·Jira 작업 중에는 프로젝트 폴더를 이동할 수 없습니다.";
			return;
		}
		long generation = Interlocked.Increment(ref _refreshGeneration);
		_measureCancellation?.Cancel();
		_measureCancellation?.Dispose();
		_measureCancellation = new CancellationTokenSource();
		CancellationToken token = _measureCancellation.Token;
		StorageTotal.Text = "프로젝트 폴더 용량 계산 중…";
		try
		{
			ProjectStorageReport projectStorageReport = await Task.Run(delegate
			{
				token.ThrowIfCancellationRequested();
				return _storageManager.Measure();
			}, token);
			if (generation == _refreshGeneration)
			{
				StorageGrid.ItemsSource = projectStorageReport.Categories.Select((ProjectStorageCategory x) => new StorageRow(x.Id, x.Name, x.RelativePath, x.FileCount, x.SizeBytes, x.Exists)).ToArray();
				StorageTotal.Text = $"프로젝트 전체 {FormatBytes(projectStorageReport.TotalBytes)} · 파일 {projectStorageReport.TotalFiles:N0}개" + (projectStorageReport.Complete ? " · 용량 계산 완료" : " · 일부 경로 접근 불가");
				StorageStatus.Text = ((projectStorageReport.Warnings.Count == 0) ? "핵심 설정·케이스 원본·초안은 선택 목록에 포함되지 않습니다." : $"읽지 못한 경로 {projectStorageReport.Warnings.Count}개 · 합계는 최소값일 수 있습니다.");
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is InvalidDataException) ? 1 : 0) != 0)
		{
			if (generation == _refreshGeneration)
			{
				StorageStatus.Text = "용량 계산 실패: " + ex2.Message;
			}
		}
	}

	private async void RecycleSelected_Click(object sender, RoutedEventArgs e)
	{
		if (_storageManager == null)
		{
			return;
		}
		StorageRow[] selected = (from StorageRow x in StorageGrid.SelectedItems
			where x.Exists
			select x).ToArray();
		if (selected.Length == 0)
		{
			StorageStatus.Text = "휴지통으로 이동할 폴더를 먼저 선택하세요.";
			return;
		}
		long bytes = 0L;
		StorageRow[] array = selected;
		foreach (StorageRow row in array)
		{
			ProjectStorageCategory projectStorageCategory = (await Task.Run(() => _storageManager.Measure())).Categories.FirstOrDefault((ProjectStorageCategory x) => x.Id == row.Id);
			if ((object)projectStorageCategory != null)
			{
				bytes = checked(bytes + projectStorageCategory.SizeBytes);
			}
		}
		string value = string.Join(Environment.NewLine, selected.Select((StorageRow x) => $"• {x.Name} — {FormatBytes(x.SizeBytes)} ({x.RelativePath})"));
		if (MessageBox.Show($"선택한 폴더 {selected.Length}개, 현재 합계 {FormatBytes(bytes)}를 Windows 휴지통으로 이동할까요?\n\n{value}\n\n프로젝트 설정, 케이스 원본, 초안은 목록에 없으며 변경되지 않습니다.", "프로젝트 데이터 이동 확인", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		int moved = 0;
		try
		{
			StorageRow[] array2 = selected;
			foreach (StorageRow storageRow in array2)
			{
				_storageManager.MoveToRecycleBin(storageRow.Id);
				moved++;
			}
			StorageStatus.Text = $"{moved}개 폴더를 휴지통으로 이동했습니다. 파일은 아직 영구 삭제되지 않았습니다.";
			await RefreshStorageAsync();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is Win32Exception) ? 1 : 0) != 0)
		{
			StorageStatus.Text = $"{moved}/{selected.Length}개 이동 · 중단: {ex.Message}";
			await RefreshStorageAsync();
		}
	}

	private static int[] ParseCaseIds(string value)
	{
		string[] array = (value ?? string.Empty).Split(new char[6] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length == 0 || array.Any((string x) => !int.TryParse(x, out var _)))
		{
			throw new InvalidDataException("케이스 번호를 14, 60 형식으로 입력하세요.");
		}
		int[] array2 = array.Select(int.Parse).ToArray();
		if (array2.Distinct().Count() != array2.Length)
		{
			throw new InvalidDataException("중복 케이스 번호가 있습니다.");
		}
		return array2;
	}

	private static string FormatBytes(long bytes)
	{
		if (!((double)bytes >= 1073741824.0))
		{
			if ((double)bytes >= 1048576.0)
			{
				return $"{(double)bytes / 1048576.0:N1} MiB";
			}
			return $"{(double)bytes / 1024.0:N1} KiB";
		}
		return $"{(double)bytes / 1073741824.0:N2} GiB";
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "projectdata/macroandstorageview.baml");
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
			ProjectLine = (TextBlock)target;
			break;
		case 2:
			MacroNameBox = (TextBox)target;
			break;
		case 3:
			CaseIdsBox = (TextBox)target;
			break;
		case 4:
			((Button)target).Click += SaveMacro_Click;
			break;
		case 5:
			((Button)target).Click += RefreshMacros_Click;
			break;
		case 6:
			MacroList = (ListBox)target;
			MacroList.SelectionChanged += MacroList_SelectionChanged;
			break;
		case 7:
			RunMacroButton = (Button)target;
			RunMacroButton.Click += RunMacro_Click;
			break;
		case 8:
			DeleteMacroButton = (Button)target;
			DeleteMacroButton.Click += DeleteMacro_Click;
			break;
		case 9:
			MacroStatus = (TextBlock)target;
			break;
		case 10:
			StorageTotal = (TextBlock)target;
			break;
		case 11:
			((Button)target).Click += RefreshStorage_Click;
			break;
		case 12:
			StorageGrid = (DataGrid)target;
			break;
		case 13:
			RecycleSelectedButton = (Button)target;
			RecycleSelectedButton.Click += RecycleSelected_Click;
			break;
		case 14:
			StorageStatus = (TextBlock)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
