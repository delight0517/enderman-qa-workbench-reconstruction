using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using EndermanQa.Desktop.Adapters;
using EndermanQa.Desktop.Contracts;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.GameMacros;

public class GameMacroWorkbenchView : UserControl, IComponentConnector
{
	private sealed record RunHistoryItem(string Label, GameMacroRunReceipt Receipt);

	private readonly GameMacroAdapterRegistry _registry = new GameMacroAdapterRegistry();

	private readonly Dictionary<string, IGameMacroAdapterProvider> _providers = new Dictionary<string, IGameMacroAdapterProvider>(StringComparer.Ordinal);

	private string? _projectRoot;

	private string _projectGameTarget = "미지정";

	private string _projectEngineId = "unknown";

	private GameAutomationMacro? _savedMacro;

	private CancellationTokenSource? _runCancellation;

	internal ComboBox AdapterPicker;

	internal TextBlock AdapterInfo;

	internal TextBlock EngineToolGuideText;

	internal TextBox MacroNameBox;

	internal TextBox TargetIdBox;

	internal TextBox StepsBox;

	internal TextBox AssertionsBox;

	internal TextBox RepeatCountBox;

	internal Button PreviewButton;

	internal Button SaveButton;

	internal ComboBox SavedMacroPicker;

	internal Button LoadMacroButton;

	internal Button RunButton;

	internal Button StopButton;

	internal TextBlock StatusText;

	internal TextBox OutputBox;

	internal ListBox RunHistoryList;

	internal TextBox RunHistoryDetailsBox;

	private bool _contentLoaded;

	public bool IsRunning => _runCancellation != null;

	private GameToolDescriptor? SelectedDescriptor => AdapterPicker.SelectedItem as GameToolDescriptor;

	public string GetProjectCapabilityContext()
	{
		GameToolDescriptor[] array = (from descriptor in _providers.Values.Select((IGameMacroAdapterProvider provider) => provider.Descriptor).Where(IsCompatibleWithProject).ToArray()
			where !descriptor.Engine.Equals("fixture-only", StringComparison.OrdinalIgnoreCase) && descriptor.Availability.Equals("ready", StringComparison.OrdinalIgnoreCase)
			select descriptor).ToArray();
		if (array.Length == 0)
		{
			return "실제 게임 매크로 어댑터: 없음. 합성 fixture만 등록되어 있으며 실제 게임 입력/관측을 할 수 없음.";
		}
		return "호환 어댑터: " + string.Join("; ", array.Select((GameToolDescriptor descriptor) => $"{descriptor.AdapterId} ({descriptor.Engine}), actions=[{string.Join(",", descriptor.Actions.Order<string>(StringComparer.Ordinal))}], observations=[{string.Join(",", descriptor.ObservationKeys.Order<string>(StringComparer.Ordinal))}]"));
	}

	public GameMacroWorkbenchView()
	{
		InitializeComponent();
		RegisterProvider(new SyntheticResourceProvider());
		Refresh();
	}

	public void SetProjectRoot(string projectDataRoot, string? gameTarget = null, string? engineId = null)
	{
		if (_runCancellation != null)
		{
			throw new InvalidOperationException("프로젝트 변경은 매크로 실행이 끝난 뒤에 할 수 있습니다.");
		}
		if (string.IsNullOrWhiteSpace(projectDataRoot))
		{
			throw new ArgumentException("Project data root is required.", "projectDataRoot");
		}
		_projectRoot = Path.GetFullPath(projectDataRoot);
		_projectGameTarget = (string.IsNullOrWhiteSpace(gameTarget) ? "미지정" : gameTarget.Trim());
		_projectEngineId = (string.IsNullOrWhiteSpace(engineId) ? "unknown" : engineId.Trim());
		GameEngineTestToolGuide gameEngineTestToolGuide = GameEngineTestToolGuides.For(_projectEngineId);
		EngineToolGuideText.Text = $"권장 테스트 도구: {gameEngineTestToolGuide.PrimaryTool}\n적합 범위: {gameEngineTestToolGuide.Coverage}\n경계: {gameEngineTestToolGuide.Boundary}" + (string.IsNullOrWhiteSpace(gameEngineTestToolGuide.DocumentationUrl) ? string.Empty : ("\n공식 문서: " + gameEngineTestToolGuide.DocumentationUrl));
		Directory.CreateDirectory(_projectRoot);
		_savedMacro = null;
		Refresh();
		RefreshSavedMacros();
		RefreshRunHistory();
		RunButton.IsEnabled = false;
		StatusText.Text = $"프로젝트 대상: {_projectGameTarget} · 엔진/도구: {_projectEngineId}. Minecraft 실제 매크로는 앱 소유 격리 서버가 실행 중일 때만 연결됩니다. 클라이언트 입력/화면 캡처는 별도 가상 화면 lease가 필요합니다.";
	}

	public void SetSuggestedTargetId(string? targetId)
	{
		if (_runCancellation == null && !string.IsNullOrWhiteSpace(targetId) && targetId.Length <= 160 && !targetId.Any(char.IsControl))
		{
			TargetIdBox.Text = targetId;
		}
	}

	public void RegisterProvider(IGameMacroAdapterProvider provider)
	{
		if (_runCancellation != null)
		{
			throw new InvalidOperationException("어댑터 변경은 매크로 실행이 끝난 뒤에 할 수 있습니다.");
		}
		ArgumentNullException.ThrowIfNull(provider, "provider");
		_registry.Register(provider);
		_providers.Add(provider.Descriptor.AdapterId, provider);
		Refresh();
		RefreshSavedMacros();
	}

	public void Refresh()
	{
		if (AdapterPicker != null)
		{
			string selectedId = (AdapterPicker.SelectedItem as GameToolDescriptor)?.AdapterId;
			AdapterPicker.ItemsSource = (from provider in _providers.Values
				select provider.Descriptor into descriptor
				where IsCompatibleWithProject(descriptor)
				select descriptor).OrderBy<GameToolDescriptor, string>((GameToolDescriptor descriptor) => descriptor.GameId, StringComparer.Ordinal).ThenBy<GameToolDescriptor, string>((GameToolDescriptor descriptor) => descriptor.DisplayName, StringComparer.Ordinal).ToArray();
			AdapterPicker.SelectedItem = AdapterPicker.Items.Cast<GameToolDescriptor>().FirstOrDefault((GameToolDescriptor descriptor) => descriptor.AdapterId == selectedId) ?? AdapterPicker.Items.Cast<GameToolDescriptor>().FirstOrDefault();
			UpdateAdapterInfo();
		}
	}

	private bool IsCompatibleWithProject(GameToolDescriptor descriptor)
	{
		return GameMacroProjectCompatibility.IsCompatible(descriptor, _projectGameTarget, _projectEngineId);
	}

	private void AdapterPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateAdapterInfo();
	}

	private void UpdateAdapterInfo()
	{
		if (AdapterInfo != null)
		{
			GameToolDescriptor selectedDescriptor = SelectedDescriptor;
			AdapterInfo.Text = (((object)selectedDescriptor == null) ? "등록된 어댑터가 없습니다. host가 IGameMacroAdapterProvider를 등록해야 합니다." : $"프로젝트 게임: {_projectGameTarget} · 프로젝트 엔진: {_projectEngineId}\n선택 어댑터: {selectedDescriptor.DisplayName} · 게임 ID: {selectedDescriptor.GameId} · 엔진: {selectedDescriptor.Engine} · 상태: {selectedDescriptor.Availability} · 계약 v{selectedDescriptor.ContractVersion}\n호환 판정: {((!IsCompatibleWithProject(selectedDescriptor)) ? "프로젝트와 불일치 · 사용 불가" : ((selectedDescriptor.Engine == "fixture-only") ? "합성 전용(실게임 미조작)" : "게임 ID와 엔진 일치"))}\n동작: {string.Join(", ", selectedDescriptor.Actions.Order<string>(StringComparer.Ordinal))}\n관측: {string.Join(", ", selectedDescriptor.ObservationKeys.Order<string>(StringComparer.Ordinal))}\n기능 SHA-256: {selectedDescriptor.CapabilitySha256}");
			Button loadMacroButton = LoadMacroButton;
			int isEnabled;
			if ((object)selectedDescriptor != null && IsCompatibleWithProject(selectedDescriptor))
			{
				ComboBox savedMacroPicker = SavedMacroPicker;
				isEnabled = ((savedMacroPicker != null && savedMacroPicker.Items.Count > 0) ? 1 : 0);
			}
			else
			{
				isEnabled = 0;
			}
			loadMacroButton.IsEnabled = (byte)isEnabled != 0;
			SaveButton.IsEnabled = !IsRunning && (object)selectedDescriptor != null && IsCompatibleWithProject(selectedDescriptor);
			PreviewButton.IsEnabled = SaveButton.IsEnabled;
		}
	}

	private void Preview_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			GameToolDescriptor selectedDescriptor = SelectedDescriptor;
			if ((object)selectedDescriptor == null || !IsCompatibleWithProject(selectedDescriptor))
			{
				throw new InvalidOperationException("선택 어댑터의 게임 ID/엔진이 현재 프로젝트와 일치하지 않습니다.");
			}
			GameAutomationMacro value = BuildMacroPreview();
			OutputBox.Text = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true
			});
			StatusText.Text = "미리보기만 만들었습니다. 게임에는 입력을 보내지 않았습니다.";
		}
		catch (Exception exception)
		{
			ShowError(exception);
		}
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			GameToolDescriptor selectedDescriptor = SelectedDescriptor;
			if ((object)selectedDescriptor == null || !IsCompatibleWithProject(selectedDescriptor))
			{
				throw new InvalidOperationException("선택 어댑터의 게임 ID/엔진이 현재 프로젝트와 일치하지 않습니다.");
			}
			if (string.IsNullOrWhiteSpace(_projectRoot))
			{
				throw new InvalidOperationException("host가 프로젝트 데이터 폴더를 먼저 연결해야 합니다.");
			}
			GameToolDescriptor descriptor = SelectedDescriptor ?? throw new InvalidOperationException("실행할 어댑터를 선택하세요.");
			GameAutomationTarget target = BuildTarget(descriptor);
			(IReadOnlyList<GameMacroStep> Steps, IReadOnlyList<GameMacroAssertion> Assertions) tuple = ParseStepsAndAssertions();
			IReadOnlyList<GameMacroStep> item = tuple.Steps;
			IReadOnlyList<GameMacroAssertion> item2 = tuple.Assertions;
			_savedMacro = new GameMacroV2Store(_projectRoot).Save(MacroNameBox.Text, target, descriptor, item, item2);
			RefreshSavedMacros(_savedMacro.Id);
			RunButton.IsEnabled = true;
			OutputBox.Text = JsonSerializer.Serialize(_savedMacro, new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true
			});
			StatusText.Text = "프로젝트에 저장됨: macros-v2/" + _savedMacro.Id + ".json · SHA-256 " + _savedMacro.ContentSha256;
		}
		catch (Exception exception)
		{
			ShowError(exception);
		}
	}

	private void RefreshSavedMacros(string? selectedId = null)
	{
		if (SavedMacroPicker != null)
		{
			IReadOnlyList<string> readOnlyList;
			if (!string.IsNullOrWhiteSpace(_projectRoot))
			{
				readOnlyList = new GameMacroV2Store(_projectRoot).ListIds();
			}
			else
			{
				IReadOnlyList<string> readOnlyList2 = Array.Empty<string>();
				readOnlyList = readOnlyList2;
			}
			IReadOnlyList<string> readOnlyList3 = readOnlyList;
			SavedMacroPicker.ItemsSource = readOnlyList3;
			SavedMacroPicker.SelectedItem = ((selectedId != null && readOnlyList3.Contains<string>(selectedId, StringComparer.Ordinal)) ? selectedId : readOnlyList3.FirstOrDefault());
			LoadMacroButton.IsEnabled = (object)SelectedDescriptor != null && readOnlyList3.Count > 0;
		}
	}

	private void RefreshRunHistory(string? selectedRunId = null)
	{
		string selectedRunId2 = selectedRunId;
		if (RunHistoryList != null)
		{
			IReadOnlyList<GameMacroRunReceipt> source;
			if (!string.IsNullOrWhiteSpace(_projectRoot))
			{
				source = new GameMacroRunReceiptStore(_projectRoot).ListRecent(50);
			}
			else
			{
				IReadOnlyList<GameMacroRunReceipt> readOnlyList = Array.Empty<GameMacroRunReceipt>();
				source = readOnlyList;
			}
			RunHistoryItem[] array = source.Select((GameMacroRunReceipt receipt) => new RunHistoryItem($"{receipt.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {receipt.MacroName} · {receipt.Attempt}/{receipt.RequestedRepeatCount} · {receipt.Result.Status}/{receipt.Result.Verdict} · {receipt.AdapterId}{((receipt.EvidenceIntegrityIssue == null) ? string.Empty : " · 증거 확인 필요")}", receipt)).ToArray();
			RunHistoryList.ItemsSource = array;
			RunHistoryList.SelectedItem = ((selectedRunId2 == null) ? null : array.FirstOrDefault((RunHistoryItem item) => item.Receipt.RunId == selectedRunId2));
			if (RunHistoryList.SelectedItem == null)
			{
				RunHistoryDetailsBox.Text = $"최근 실행 기록 {array.Length}건 · 저장 위치: macro-runs";
			}
		}
	}

	private void RunHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (RunHistoryList.SelectedItem is RunHistoryItem runHistoryItem)
		{
			RunHistoryDetailsBox.Text = JsonSerializer.Serialize(runHistoryItem.Receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true
			});
		}
	}

	private void LoadSavedMacro_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(_projectRoot))
			{
				throw new InvalidOperationException("먼저 프로젝트 데이터 폴더를 연결하세요.");
			}
			GameToolDescriptor descriptor = SelectedDescriptor ?? throw new InvalidOperationException("불러올 어댑터를 선택하세요.");
			string id = (SavedMacroPicker.SelectedItem as string) ?? throw new InvalidOperationException("불러올 저장 매크로를 선택하세요.");
			_savedMacro = new GameMacroV2Store(_projectRoot).LoadForAdapter(id, descriptor);
			MacroNameBox.Text = _savedMacro.Name;
			TargetIdBox.Text = _savedMacro.Target.TargetId;
			StepsBox.Text = string.Join(Environment.NewLine, _savedMacro.Steps.Select((GameMacroStep step) => step.Action + ((step.Arguments.Count == 0) ? string.Empty : ("|" + string.Join(";", from pair in step.Arguments.OrderBy<KeyValuePair<string, string>, string>((KeyValuePair<string, string> pair) => pair.Key, StringComparer.Ordinal)
				select pair.Key + "=" + pair.Value)))));
			AssertionsBox.Text = string.Join(Environment.NewLine, _savedMacro.Assertions.Select((GameMacroAssertion assertion) => $"{assertion.AfterStepId}|{assertion.ObservationKey}|{assertion.Operator}|{assertion.ExpectedValue}"));
			RunButton.IsEnabled = true;
			OutputBox.Text = JsonSerializer.Serialize(_savedMacro, new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true
			});
			StatusText.Text = "저장 매크로 불러옴 · 대상 " + _savedMacro.Target.TargetId + " · SHA-256 " + _savedMacro.ContentSha256;
		}
		catch (Exception exception)
		{
			ShowError(exception);
		}
	}

	private async void Run_Click(object sender, RoutedEventArgs e)
	{
		if ((object)_savedMacro == null)
		{
			return;
		}
		int repeatCount;
		bool flag = !int.TryParse(RepeatCountBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out repeatCount);
		if (!flag)
		{
			bool flag2 = ((repeatCount < 1 || repeatCount > 10) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			ShowError(new InvalidOperationException("반복 횟수는 1에서 10 사이의 정수여야 합니다."));
			return;
		}
		bool current = (sender as Button)?.IsEnabled ?? true;
		RunButton.IsEnabled = false;
		StopButton.IsEnabled = true;
		SetEditingEnabled(enabled: false);
		using CancellationTokenSource cancellation = new CancellationTokenSource();
		_runCancellation = cancellation;
		try
		{
			GameToolDescriptor descriptor = SelectedDescriptor ?? throw new InvalidOperationException("실행 어댑터를 선택하세요.");
			if (_savedMacro.Target != BuildTarget(descriptor) || _savedMacro.AdapterId != descriptor.AdapterId)
			{
				throw new InvalidOperationException("저장 후 게임·대상·버전 정보가 바뀌었습니다. 먼저 새 매크로로 저장하세요.");
			}
			StringBuilder output = new StringBuilder();
			GameMacroRunner runner = new GameMacroRunner();
			GameMacroRunReceiptStore receiptStore = new GameMacroRunReceiptStore(_projectRoot ?? throw new InvalidOperationException("프로젝트 폴더가 연결되지 않았습니다."));
			for (int attempt = 1; attempt <= repeatCount; attempt++)
			{
				DateTimeOffset startedAt = DateTimeOffset.UtcNow;
				GameTargetVerification targetVerification = null;
				GameMacroRunResult result;
				try
				{
					cancellation.Token.ThrowIfCancellationRequested();
					IGameMacroAdapter gameMacroAdapter = await _registry.ConnectAsync(descriptor.AdapterId, _savedMacro.Target, cancellation.Token);
					ConfiguredAsyncDisposable configuredAsyncDisposable = gameMacroAdapter.ConfigureAwait(continueOnCapturedContext: true);
					try
					{
						targetVerification = gameMacroAdapter.TargetVerification;
						result = await runner.RunAsync(_savedMacro, gameMacroAdapter, cancellation.Token);
					}
					finally
					{
						if (gameMacroAdapter is IAsyncDisposable ad) { await ad.DisposeAsync(); }
					}
				}
				catch (OperationCanceledException)
				{
					result = new GameMacroRunResult(RunStatus.Stopped, Verdict.Inconclusive, Array.Empty<GameMacroStepResult>(), Array.Empty<GameMacroAssertionResult>(), "Macro cancelled before this attempt completed; no later steps were dispatched.");
				}
				catch (Exception ex2)
				{
					result = new GameMacroRunResult(RunStatus.Failed, Verdict.Inconclusive, Array.Empty<GameMacroStepResult>(), Array.Empty<GameMacroAssertionResult>(), "Adapter connection or run failed: " + ex2.Message);
				}
				GameMacroRunReceipt gameMacroRunReceipt = receiptStore.Save(_savedMacro, descriptor, attempt, repeatCount, startedAt, DateTimeOffset.UtcNow, result, targetVerification);
				StringBuilder stringBuilder = output;
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(20, 5, stringBuilder);
				handler.AppendLiteral("반복 ");
				handler.AppendFormatted(attempt);
				handler.AppendLiteral("/");
				handler.AppendFormatted(repeatCount);
				handler.AppendLiteral(": ");
				handler.AppendFormatted(result.Status);
				handler.AppendLiteral(" / ");
				handler.AppendFormatted(result.Verdict);
				handler.AppendLiteral(" · receipt ");
				handler.AppendFormatted(gameMacroRunReceipt.RunId);
				stringBuilder2.AppendLine(ref handler);
				if (!string.IsNullOrWhiteSpace(gameMacroRunReceipt.EvidenceIntegrityIssue))
				{
					stringBuilder = output;
					StringBuilder stringBuilder3 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder);
					handler.AppendLiteral("  증거 검증 경고: ");
					handler.AppendFormatted(gameMacroRunReceipt.EvidenceIntegrityIssue);
					stringBuilder3.AppendLine(ref handler);
				}
				foreach (GameMacroAssertionResult assertion in result.Assertions)
				{
					stringBuilder = output;
					StringBuilder stringBuilder4 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(5, 2, stringBuilder);
					handler.AppendLiteral("  ");
					handler.AppendFormatted(assertion.Passed ? "PASS" : (assertion.Evaluated ? "FAIL" : "INCONCLUSIVE"));
					handler.AppendLiteral(" · ");
					handler.AppendFormatted(assertion.Detail);
					stringBuilder4.AppendLine(ref handler);
				}
				if (!string.IsNullOrWhiteSpace(result.Limitation))
				{
					stringBuilder = output;
					StringBuilder stringBuilder5 = stringBuilder;
					handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder);
					handler.AppendLiteral("  제한: ");
					handler.AppendFormatted(result.Limitation);
					stringBuilder5.AppendLine(ref handler);
				}
				foreach (GameMacroStepResult step in result.Steps)
				{
					foreach (KeyValuePair<string, string> observation in step.Observations)
					{
						stringBuilder = output;
						StringBuilder stringBuilder6 = stringBuilder;
						handler = new StringBuilder.AppendInterpolatedStringHandler(6, 2, stringBuilder);
						handler.AppendLiteral("  관측 ");
						handler.AppendFormatted(observation.Key);
						handler.AppendLiteral("=");
						handler.AppendFormatted(observation.Value);
						stringBuilder6.AppendLine(ref handler);
					}
					foreach (string evidenceReference in step.EvidenceReferences)
					{
						stringBuilder = output;
						StringBuilder stringBuilder7 = stringBuilder;
						handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
						handler.AppendLiteral("  증거 ");
						handler.AppendFormatted(evidenceReference);
						stringBuilder7.AppendLine(ref handler);
					}
				}
				if (result.Status != RunStatus.Completed || result.Verdict != 0)
				{
					break;
				}
				result = null;
			}
			OutputBox.Text = output.ToString();
			RefreshRunHistory();
			StatusText.Text = ((descriptor.Engine == "fixture-only") ? "합성 시뮬레이션 결과입니다. 실제 게임 플레이 성공을 뜻하지 않습니다." : "어댑터 실행이 끝났습니다. 판정은 각 실행에서 수집된 관측값 범위에만 적용됩니다.");
		}
		catch (Exception exception)
		{
			ShowError(exception);
		}
		finally
		{
			if (_runCancellation == cancellation)
			{
				_runCancellation = null;
			}
			StopButton.IsEnabled = false;
			SetEditingEnabled(enabled: true);
			RunButton.IsEnabled = current && (object)_savedMacro != null;
		}
	}

	private void Stop_Click(object sender, RoutedEventArgs e)
	{
		_runCancellation?.Cancel();
		StopButton.IsEnabled = false;
		StatusText.Text = "실행 중단을 요청했습니다. 현재 어댑터 동작 종료 후 다음 단계는 보내지 않습니다.";
	}

	private void SetEditingEnabled(bool enabled)
	{
		AdapterPicker.IsEnabled = enabled;
		SavedMacroPicker.IsEnabled = enabled;
		LoadMacroButton.IsEnabled = enabled && SavedMacroPicker.Items.Count > 0;
		MacroNameBox.IsEnabled = enabled;
		TargetIdBox.IsEnabled = enabled;
		StepsBox.IsEnabled = enabled;
		AssertionsBox.IsEnabled = enabled;
		RepeatCountBox.IsEnabled = enabled;
		Button previewButton = PreviewButton;
		int isEnabled;
		if (enabled)
		{
			GameToolDescriptor selectedDescriptor = SelectedDescriptor;
			if ((object)selectedDescriptor != null)
			{
				isEnabled = (IsCompatibleWithProject(selectedDescriptor) ? 1 : 0);
				goto IL_0095;
			}
		}
		isEnabled = 0;
		goto IL_0095;
		IL_0095:
		previewButton.IsEnabled = (byte)isEnabled != 0;
		SaveButton.IsEnabled = PreviewButton.IsEnabled;
	}

	private GameAutomationMacro BuildMacroPreview()
	{
		GameToolDescriptor gameToolDescriptor = SelectedDescriptor ?? throw new InvalidOperationException("실행할 어댑터를 선택하세요.");
		(IReadOnlyList<GameMacroStep> Steps, IReadOnlyList<GameMacroAssertion> Assertions) tuple = ParseStepsAndAssertions();
		IReadOnlyList<GameMacroStep> item = tuple.Steps;
		IReadOnlyList<GameMacroAssertion> item2 = tuple.Assertions;
		GameAutomationTarget target = BuildTarget(gameToolDescriptor);
		GameAutomationMacro gameAutomationMacro = new GameAutomationMacro(2, Guid.NewGuid().ToString("N"), MacroNameBox.Text.Trim(), DateTimeOffset.UtcNow, target, gameToolDescriptor.AdapterId, gameToolDescriptor.ContractVersion, gameToolDescriptor.CapabilitySha256, item, item2, "미리보기는 저장 전입니다.");
		IReadOnlyList<string> readOnlyList = GameMacroValidation.Validate(gameAutomationMacro, gameToolDescriptor);
		if (readOnlyList.Count != 0)
		{
			throw new InvalidDataException(string.Join(Environment.NewLine, readOnlyList));
		}
		return gameAutomationMacro;
	}

	private GameAutomationTarget BuildTarget(GameToolDescriptor descriptor)
	{
		string text = TargetIdBox.Text.Trim();
		int length = text.Length;
		bool flag = ((length < 1 || length > 160) ? true : false);
		if (flag || text.Any(char.IsControl))
		{
			throw new InvalidOperationException("대상 ID는 제어 문자가 없는 1–160자여야 합니다.");
		}
		return new GameAutomationTarget(descriptor.GameId, text, text, "unspecified", descriptor.Engine, descriptor.RequiresOwnedProcess ? "owned" : "explicit-disposable", (descriptor.Engine == "fixture-only") ? "synthetic" : "desktop");
	}

	private (IReadOnlyList<GameMacroStep> Steps, IReadOnlyList<GameMacroAssertion> Assertions) ParseStepsAndAssertions()
	{
		List<GameMacroStep> list = new List<GameMacroStep>();
		string[] array = StepsBox.Text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text in array)
		{
			string[] array2 = text.Split('|', 2);
			if (array2.Length == 0 || string.IsNullOrWhiteSpace(array2[0]))
			{
				throw new InvalidOperationException("잘못된 단계: " + text);
			}
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
			if (array2.Length == 2 && !string.IsNullOrWhiteSpace(array2[1]))
			{
				string[] array3 = array2[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				foreach (string text2 in array3)
				{
					string[] array4 = text2.Split('=', 2);
					if (array4.Length != 2 || !dictionary.TryAdd(array4[0].Trim(), array4[1].Trim()))
					{
						throw new InvalidOperationException("단계 인수 형식 오류 또는 중복 키: " + text2);
					}
				}
			}
			string text3 = array2[0].Trim();
			string id = text3.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? text3;
			list.Add(new GameMacroStep(id, text3, dictionary));
		}
		List<GameMacroAssertion> list2 = new List<GameMacroAssertion>();
		array = AssertionsBox.Text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text4 in array)
		{
			string[] array5 = text4.Split('|');
			if (array5.Length != 4)
			{
				throw new InvalidOperationException("기대 결과는 stepId|관측키|연산자|기대값 형식이어야 합니다: " + text4);
			}
			list2.Add(new GameMacroAssertion("assertion-" + (list2.Count + 1).ToString(CultureInfo.InvariantCulture), array5[0].Trim(), array5[1].Trim(), array5[2].Trim(), array5[3].Trim()));
		}
		return (Steps: list, Assertions: list2);
	}

	private void ShowError(Exception exception)
	{
		StatusText.Text = exception.Message;
		MessageBox.Show(Window.GetWindow(this), exception.Message, "게임 매크로", MessageBoxButton.OK, MessageBoxImage.Exclamation);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "gamemacros/gamemacroworkbenchview.baml");
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
			AdapterPicker = (ComboBox)target;
			AdapterPicker.SelectionChanged += AdapterPicker_SelectionChanged;
			break;
		case 2:
			AdapterInfo = (TextBlock)target;
			break;
		case 3:
			EngineToolGuideText = (TextBlock)target;
			break;
		case 4:
			MacroNameBox = (TextBox)target;
			break;
		case 5:
			TargetIdBox = (TextBox)target;
			break;
		case 6:
			StepsBox = (TextBox)target;
			break;
		case 7:
			AssertionsBox = (TextBox)target;
			break;
		case 8:
			RepeatCountBox = (TextBox)target;
			break;
		case 9:
			PreviewButton = (Button)target;
			PreviewButton.Click += Preview_Click;
			break;
		case 10:
			SaveButton = (Button)target;
			SaveButton.Click += Save_Click;
			break;
		case 11:
			SavedMacroPicker = (ComboBox)target;
			break;
		case 12:
			LoadMacroButton = (Button)target;
			LoadMacroButton.Click += LoadSavedMacro_Click;
			break;
		case 13:
			RunButton = (Button)target;
			RunButton.Click += Run_Click;
			break;
		case 14:
			StopButton = (Button)target;
			StopButton.Click += Stop_Click;
			break;
		case 15:
			StatusText = (TextBlock)target;
			break;
		case 16:
			OutputBox = (TextBox)target;
			break;
		case 17:
			RunHistoryList = (ListBox)target;
			RunHistoryList.SelectionChanged += RunHistoryList_SelectionChanged;
			break;
		case 18:
			RunHistoryDetailsBox = (TextBox)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
