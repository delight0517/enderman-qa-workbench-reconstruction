using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using EndermanQa.Desktop.Adapters;
using EndermanQa.Desktop.AiSetup;
using EndermanQa.Desktop.Contracts;
using EndermanQa.Desktop.Evidence;
using EndermanQa.Desktop.GameMacros;
using EndermanQa.Desktop.Guides;
using EndermanQa.Desktop.Integrations;
using EndermanQa.Desktop.Manager;
using EndermanQa.Desktop.Modules;
using EndermanQa.Desktop.Persistence;
using EndermanQa.Desktop.ProjectData;
using EndermanQa.Desktop.Results;
using EndermanQa.Desktop.Scenarios;
using EndermanQa.Desktop.Tools;
using Microsoft.Win32;

namespace EndermanQa.Desktop;

public class MainWindow : Window, IComponentConnector
{
	private ProjectDataBrowser? _projectDataBrowser;

	private ProjectWorkspaceShell? _workspaceShell;

	// Keep candidate state separate from the installed Workbench and its 100-case records.
	private readonly string _appDataRoot = GetCandidateAppDataRoot();
	private static string GetCandidateAppDataRoot()
	{
		string? previewRoot = Environment.GetEnvironmentVariable("ENDERMAN_QA_PREVIEW_DATA_ROOT");
		return string.IsNullOrWhiteSpace(previewRoot)
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndermanQaWorkbench-Candidate-v27")
			: Path.GetFullPath(previewRoot);
	}

	private string _projectRoot;

	private string _dataRoot;

	private readonly List<ManualEvidence> _manualEvidence = new List<ManualEvidence>();

	private string _caseId = Guid.NewGuid().ToString("N");

	private DateTimeOffset _caseStartedAt = DateTimeOffset.UtcNow;

	private CancellationTokenSource? _runCancellation;

	private IGameAdapter? _activeAdapter;

	private QaScenario? _pendingAiScenario;

	private QaPlan? _pendingAiPlan;

	private bool _pendingAiUsesJava;

	private JavaServerWorkspace? _javaWorkspace;

	private OwnedJavaServerProcess? _javaServer;

	private OwnedJavaClientProcess? _javaClient;

	private OwnedLoopbackLatencyProxy? _javaLatencyProxy;

	private bool _javaClientStarting;

	private string? _javaClientProjectDataRoot;

	private CancellationTokenSource? _suiteCancellation;

	private bool _draftDirty = true;

	private bool _closeAfterSafeSave;

	private bool _closeSavePending;

	private bool _jiraSubmissionBusy;

	private AiRouterSetupWindow? _aiRouterSetupWindow;

	private AiQaConversationStore _aiConversationStore;

	private readonly List<SavedAiQaConversationTurn> _aiConversation = new List<SavedAiQaConversationTurn>();

	private AiQaConversationStore _toolConversationStore;

	private ToolWorkspaceStore _toolWorkspaceStore;

	private readonly List<SavedAiQaConversationTurn> _toolConversation = new List<SavedAiQaConversationTurn>();

	private IReadOnlyList<ToolWorkspaceStore.GeneratedToolFile> _toolGeneratedFiles = Array.Empty<ToolWorkspaceStore.GeneratedToolFile>();

	private AiConversationProfile _toolProfile;

	private string? _lastToolPrompt;

	private string? _lastToolResponse;

	private string? _lastToolModel;

	private bool _restoringToolState;

	private bool _restoringAiState;

	private bool _restoringProjectGameContext;

	private bool _aiRequestBusy;

	private readonly LiveThemeModuleV1 _liveTheme;

	private ProjectHomeDashboard? _homeDashboard;
	private readonly ModuleHostV1<ProjectHomeDashboard> _projectBriefModuleHost = new ModuleHostV1<ProjectHomeDashboard>();

	private readonly AiVirtualDisplayManagerClient _virtualDisplayManager = new AiVirtualDisplayManagerClient();

	private string? _lastAiPrompt;

	private string? _lastAiResponse;

	private string? _lastAiModel;

	private ProjectArtifactStore.Artifact? _selectedArtifact;

	private readonly DispatcherTimer _draftTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(5.0)
	};

	internal TextBlock CurrentProjectName;

	internal TabControl MainStages;

	internal TextBlock StageProjectName;

	internal ComboBox GameTargetPicker;

	internal TextBox SelectedGameVersionBox;

	internal GameMacroWorkbenchView GameMacroWorkbenchContent;

	internal TextBlock StageContextLabel;

	internal ListBox AiChatHistory;

	internal TextBox AiChatInput;

	internal TextBlock PlanSummary;

	internal Button RunAiPlanButton;

	internal Button RunEndermanSuiteButton;

	internal Button RerunSuiteAnomaliesButton;

	internal Button JavaDeathSmokeButton;

	internal Button JavaFallTestButton;

	internal Button JavaWaterTestButton;

	internal Button Tc014RelocationSmokeButton;

	internal Button Tc014ClientTestButton;

	internal Button Tc060ClientTestButton;

	internal Button RunPlanButton;

	internal Button StopRunButton;

	internal TextBlock ArtifactProjectPathLabel;

	internal ListBox ProjectArtifactList;

	internal TextBlock ArtifactPreviewTitle;

	internal TextBox ArtifactPreview;

	internal TextBlock ArtifactHashLabel;

	internal TextBlock JiraStageStatus;

	internal ToolStudioView ToolStudioContent;

	internal OnlyChatEmbeddedView OnlyChatEmbeddedContent;

	internal Grid MainContentGrid;

	internal TextBox CaseTitle;

	internal TextBox CaseTarget;

	internal TextBox GameVersion;

	internal TextBox Preconditions;

	internal TextBox ManualSteps;

	internal TextBox ExpectedResult;

	internal TextBox ActualResult;

	internal ComboBox VerdictPicker;

	internal TextBox ManualNotes;

	internal ListBox EvidenceList;

	internal TextBox EventLog;

	internal TabItem EvidencePreviewTab;

	internal EvidencePreviewControl ManualEvidencePreview;

	internal SuiteResultsView SuiteResultsContent;

	internal Button JiraCsvExportButton;

	internal Button ExcelExportButton;

	internal TextBox JavaPathBox;

	internal TextBox ExistingInstanceIdBox;

	internal CheckBox EulaAcceptedCheck;

	internal Button StartJavaServerButton;

	internal Button StopJavaServerButton;

	internal CheckBox UseLatencyProxyCheckBox;

	internal TextBox ClientToServerDelayBox;

	internal TextBox ServerToClientDelayBox;

	internal TextBlock ClientSessionStatus;

	internal TextBox ClientCaptureCaseIdBox;

	internal Button StartJavaClientButton;

	internal Button CaptureClientButton;

	internal Button StopJavaClientButton;

	internal Button WorkflowGuideButton;

	internal Button AiSetupButton;

	internal MacroAndStorageView MacroAndStorageContent;

	internal ModuleManagerView ModuleManagerContent;

	internal TextBlock TargetStatus;

	internal WorkflowGuideView WorkflowGuideContent;

	private bool _contentLoaded;

	private ProjectArtifactStore ArtifactStore => new ProjectArtifactStore(_dataRoot);

	private void BrowseProjectData_Click(object sender, RoutedEventArgs e)
	{
		if (_projectDataBrowser != null)
		{
			_projectDataBrowser.Activate();
			return;
		}
		_projectDataBrowser = new ProjectDataBrowser(new QaProjectCatalog(_appDataRoot))
		{
			Owner = this
		};
		_projectDataBrowser.Closed += delegate
		{
			_projectDataBrowser = null;
		};
		_projectDataBrowser.Show();
	}

	public MainWindow()
	{
		InitializeComponent();
		Title = "Enderman QA Workbench · Reconstructed Preview (0.3.184)";
		_liveTheme = new LiveThemeModuleV1(_appDataRoot);
		MainStageNavigation.Configure(MainStages, SuiteResultsContent);
		SuiteResultsContent.AttachExportButtons(ExcelExportButton, JiraCsvExportButton);
		ProjectGameSettingsLayout.Apply(this);
		var homeTab = MainStages.Items.OfType<TabItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "project-game", StringComparison.Ordinal));
		if (homeTab?.Content is ScrollViewer homeScroll && homeScroll.Content is StackPanel homeStack)
		{
			var projectBriefFactory = new ProjectHomeDashboardFactoryV1(
				homeStack,
				SuiteResultsContent,
				() => MainStageNavigation.Select(MainStages, "qa-results"),
				() => { MainStageNavigation.Select(MainStages, "qa-results"); _ = SuiteResultsContent.ExportPortfolioAsync(); },
				() => BrowseProjectData_Click(this, new RoutedEventArgs()),
				() => ManageProject_Click(this, new RoutedEventArgs()));
			if (_projectBriefModuleHost.Apply(projectBriefFactory, projectBriefFactory.ModuleId, projectBriefFactory.Version, projectBriefFactory.SourceHash))
			{
				_homeDashboard = _projectBriefModuleHost.Active;
			}
			else
			{
				var failure = "프로젝트 브리프 모듈을 불러오지 못했습니다 · " + _projectBriefModuleHost.LastError;
				EventLog.AppendText(failure + "\n");
				homeStack.Children.Insert(Math.Min(2, homeStack.Children.Count), new TextBlock { Text = failure, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Foreground = System.Windows.Media.Brushes.DarkRed });
			}
		}
		QaProjectDefinition activeProject = new QaProjectCatalog(_appDataRoot).GetActiveProject();
		_workspaceShell = ProjectWorkspaceShell.Attach(
			MainStages,
			OnlyChatEmbeddedContent,
			activeProject?.Name ?? "기본 작업 공간",
			Path.Combine(activeProject?.DataRoot ?? _appDataRoot, "evidence"),
			() => ManageProject_Click(this, new RoutedEventArgs()),
			() => BrowseProjectData_Click(this, new RoutedEventArgs()));
		SuiteResultsContent.SelectedEvidenceChanged += (_, e) => _ = _workspaceShell.ShowEvidenceAsync(e.Attachments);
		RestoreProjectGameContext(activeProject);
		_projectRoot = activeProject?.RootPath ?? _appDataRoot;
		_dataRoot = activeProject?.DataRoot ?? _appDataRoot;
		_aiConversationStore = new AiQaConversationStore(_dataRoot);
		_toolProfile = ToolStudioContent.SelectedProfile;
		_toolConversationStore = CreateToolConversationStore(_dataRoot, _toolProfile);
		_toolWorkspaceStore = new ToolWorkspaceStore(_dataRoot);
		ToolStudioContent.ProfileChanged += ToolStudio_ProfileChanged;
		ToolStudioContent.SendRequested += ToolStudio_SendRequested;
		ToolStudioContent.SaveRequested += ToolStudio_SaveRequested;
		ToolStudioContent.OpenFilesRequested += ToolStudio_OpenFilesRequested;
		ToolStudioContent.DraftChanged += ToolStudio_DraftChanged;
		ToolStudioContent.GeneratedFileSelected += ToolStudio_GeneratedFileSelected;
		ToolStudioContent.OnlyChatRequested += ToolStudio_OnlyChatRequested;
		ToolStudioContent.OnlyChatConnectRequested += ToolStudio_OnlyChatConnectRequested;
		MacroAndStorageContent.MacroRunRequested = delegate(ProjectQaMacro macro)
		{
			RunMacroAsync(macro);
		};
		GameMacroWorkbenchContent.RegisterProvider(new MinecraftServerMacroProvider(() => _javaServer, () => _javaWorkspace));
		MacroAndStorageContent.ProjectDataMutationBlocked = () => _aiRequestBusy || _jiraSubmissionBusy || _runCancellation != null || _suiteCancellation != null;
		JavaPathBox.Text = GetDefaultJavaPath();
		SelectedGameVersionBox.LostFocus += delegate
		{
			TryPersistProjectGameContext("field change");
		};
		GameTargetPicker.SelectionChanged += delegate
		{
			TryPersistProjectGameContext("game selection");
			UpdateEndermanSuiteAvailability();
		};
		TextBox[] array = new TextBox[8] { CaseTitle, CaseTarget, GameVersion, Preconditions, ManualSteps, ExpectedResult, ActualResult, ManualNotes };
		for (int i = 0; i < array.Length; i++)
		{
			array[i].TextChanged += delegate
			{
				_draftDirty = true;
			};
		}
		VerdictPicker.SelectionChanged += delegate
		{
			_draftDirty = true;
		};
		_draftTimer.Tick += async delegate
		{
			await PersistDraftAsync();
		};
		_draftTimer.Start();
		base.Closing += MainWindow_Closing;
		AiChatInput.TextChanged += delegate
		{
			PersistAiDraft();
		};
		base.Loaded += async delegate
		{
			try
			{
				SuiteResultsContent.StartPresentationProfileModule(_appDataRoot);
			}
			catch (Exception ex)
			{
				EventLog.AppendText("Results presentation profile unavailable · compiled default retained · " + ex.Message + "\n");
			}
			RefreshCurrentProjectLabel();
			RestoreDraft();
			RefreshJiraStatus();
			await RestoreAiConversationAsync();
			await RestoreToolConversationAsync();
			RefreshProjectArtifacts();
			RefreshToolWorkspaceFiles();
			SetMacroStorageProject(new QaProjectCatalog(_appDataRoot).GetActiveProject());
			UpdateEndermanSuiteAvailability();
			GameMacroWorkbenchContent.SetProjectRoot(_dataRoot, activeProject?.GameTarget, QaProjectCatalog.GetProjectEngineId(activeProject));
			RestorePreparedTargetForProject(activeProject);
			await SuiteResultsContent.LoadProjectAsync(_projectRoot, _dataRoot);
			_homeDashboard?.Update(activeProject);
			try
			{
				if (SuiteResultsContent.Parent is FrameworkElement fe)
				{
					DependencyObject cur = fe;
					while (cur != null && !(cur is TabItem))
					{
						cur = System.Windows.Media.VisualTreeHelper.GetParent(cur) ?? (cur as FrameworkElement)?.Parent;
					}
					if (cur is TabItem ti)
					{
						ti.IsSelected = true;
						if (ti.Parent is TabControl tc)
						{
							tc.SelectedItem = ti;
							DependencyObject parentControl = tc;
							while (parentControl != null && !(parentControl is TabItem))
							{
								parentControl = System.Windows.Media.VisualTreeHelper.GetParent(parentControl) ?? (parentControl as FrameworkElement)?.Parent;
							}
							if (parentControl is TabItem parentTabItem)
							{
								parentTabItem.IsSelected = true;
								if (parentTabItem.Parent is TabControl rootTc)
								{
									rootTc.SelectedItem = parentTabItem;
								}
							}
						}
					}
				}
			}
			catch { }
			ModuleManagerContent.SetAppDataRoot(_appDataRoot);
			ModuleManagerContent.SetProjectRoot(_dataRoot);
			try
			{
				_liveTheme.Initialize();
				ModuleManagerContent.SetLiveTheme(_liveTheme);
			}
			catch (Exception ex)
			{
				ModuleManagerContent.ShowThemeError("실시간 UI 모듈을 시작하지 못했습니다 · " + ex.Message);
			}
		};
		base.Closed += async delegate
		{
			PersistToolConversationDraft();
			OnlyChatEmbeddedContent.Shutdown();
			_draftTimer.Stop();
			_projectBriefModuleHost.Dispose();
			_liveTheme.Dispose();
			ModuleManagerContent.ClearLiveTheme();
			if (_javaClient != null)
			{
				await StopOwnedJavaClientAsync(updateUi: false);
			}
			if (_javaServer != null)
			{
				await _javaServer.DisposeAsync();
			}
		};
	}

	private string GetDefaultJavaPath()
	{
		string text = Path.Combine(_appDataRoot, "toolchain", "jdk-25.0.5+8", "jdk-25.0.5+8", "bin", "java.exe");
		if (File.Exists(text))
		{
			return text;
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "work", "toolchain", "jdk-25.0.5", "bin", "java.exe");
	}

	private async Task RestoreAiConversationAsync()
	{
		_restoringAiState = true;
		try
		{
			_aiConversation.Clear();
			List<SavedAiQaConversationTurn> aiConversation = _aiConversation;
			aiConversation.AddRange(await _aiConversationStore.LoadRecentAsync());
			AiChatInput.Text = _aiConversationStore.LoadDraft();
			RenderAiConversation();
			if (_aiConversation.Count > 0)
			{
				EventLog.AppendText($"AI CHAT RESTORED · {_aiConversation.Count}개 최근 메시지 · local project data\n");
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			EventLog.AppendText("AI CHAT RESTORE WARNING · " + ex.Message + "\n");
		}
		finally
		{
			_restoringAiState = false;
		}
	}

	private static AiQaConversationStore CreateToolConversationStore(string dataRoot, AiConversationProfile profile)
	{
		return new AiQaConversationStore(dataRoot, profile switch
		{
			AiConversationProfile.ToolDesigner => "tool-builder", 
			AiConversationProfile.WorkbenchEvolution => "self-evolution", 
			_ => throw new ArgumentOutOfRangeException("profile", "Tool Studio requires a tool or self-evolution profile."), 
		});
	}

	private static string ToolProfileLabel(AiConversationProfile profile)
	{
		return profile switch
		{
			AiConversationProfile.ToolDesigner => "도구 설계", 
			AiConversationProfile.WorkbenchEvolution => "Workbench 자체 개선", 
			_ => "QA", 
		};
	}

	private void RenderToolConversation()
	{
		ToolStudioContent.SetConversation(from turn in _toolConversation.TakeLast(40)
			select ((turn.Role == "user") ? "나" : ("AI" + (string.IsNullOrWhiteSpace(turn.ProviderModel) ? "" : (" (" + turn.ProviderModel + ")")))) + ": " + turn.Content);
	}

	private async Task RestoreToolConversationAsync()
	{
		_restoringToolState = true;
		try
		{
			_toolConversation.Clear();
			List<SavedAiQaConversationTurn> toolConversation = _toolConversation;
			toolConversation.AddRange(await _toolConversationStore.LoadRecentAsync());
			ToolStudioContent.SetConversation(from turn in _toolConversation.TakeLast(40)
				select ((turn.Role == "user") ? "나" : ("AI" + (string.IsNullOrWhiteSpace(turn.ProviderModel) ? "" : (" (" + turn.ProviderModel + ")")))) + ": " + turn.Content);
			ToolStudioContent.SetPrompt(_toolConversationStore.LoadDraft());
			ToolStudioContent.SetStatus($"{ToolProfileLabel(_toolProfile)} · 이 프로젝트에 로컬 저장 · {_toolConversation.Count}개 메시지");
		}
		catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			ToolStudioContent.SetStatus("대화 복원 실패 · 기존 파일은 보존됨");
			EventLog.AppendText("TOOL CHAT RESTORE WARNING · " + ex.GetType().Name + "\n");
		}
		finally
		{
			_restoringToolState = false;
		}
	}

	private void PersistToolConversationDraft()
	{
		if (_restoringToolState)
		{
			return;
		}
		try
		{
			_toolConversationStore.SaveDraft(ToolStudioContent.PromptText);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			EventLog.AppendText("TOOL CHAT DRAFT SAVE WARNING · " + ex.GetType().Name + "\n");
		}
	}

	private void ToolStudio_DraftChanged(object sender, RoutedEventArgs e)
	{
		PersistToolConversationDraft();
	}

	private async void ToolStudio_ProfileChanged(object sender, RoutedEventArgs e)
	{
		if (_aiRequestBusy)
		{
			ToolStudioContent.SetSelectedProfile(_toolProfile);
			ToolStudioContent.SetStatus("요청 처리 중에는 작업 모드를 바꿀 수 없습니다.");
			return;
		}
		PersistToolConversationDraft();
		_toolProfile = ToolStudioContent.SelectedProfile;
		_toolConversationStore = CreateToolConversationStore(_dataRoot, _toolProfile);
		_lastToolPrompt = null;
		_lastToolResponse = null;
		_lastToolModel = null;
		await RestoreToolConversationAsync();
	}

	private async void ToolStudio_SendRequested(object sender, RoutedEventArgs e)
	{
		if (_aiRequestBusy || _runCancellation != null)
		{
			return;
		}
		string request = ToolStudioContent.PromptText.Trim();
		if (string.IsNullOrWhiteSpace(request))
		{
			ToolStudioContent.SetStatus("먼저 만들 도구나 개선하려는 기능을 설명하세요.");
			return;
		}
		if (string.IsNullOrWhiteSpace(ToolStudioContent.TargetName))
		{
			ToolStudioContent.SetStatus("대상 앱·게임 또는 Workbench 기능을 입력하세요.");
			return;
		}
		_aiRequestBusy = true;
		ToolStudioContent.SetStatus("AI Router에 요청 중 · 완료되면 답변을 직접 파일로 저장할 수 있습니다.");
		try
		{
			await _toolConversationStore.AppendAsync("user", request);
			_toolConversation.Add(new SavedAiQaConversationTurn("user", request, null, DateTimeOffset.UtcNow));
			RenderToolConversation();
			using HttpClient http = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(60.0)
			};
			AiQaConversationService aiQaConversationService = new AiQaConversationService(new AiAccountRouterClient(http));
			AiQaConversationTurn[] history = _toolConversation.Take(_toolConversation.Count - 1).TakeLast(10).Select(delegate(SavedAiQaConversationTurn turn)
			{
				string role = turn.Role;
				string content;
				if (turn.Content.Length <= 2500)
				{
					content = turn.Content;
				}
				else
				{
					string content2 = turn.Content;
					int length = content2.Length;
					int num = length - 2500;
					content = content2.Substring(num, length - num);
				}
				return new AiQaConversationTurn(role, content);
			})
				.ToArray();
			string savedEvidenceContext = $"Workbench project: {new QaProjectCatalog(_appDataRoot).GetActiveProject()?.Name ?? "default"}\nDesign target: {ToolStudioContent.TargetName}\nCurrent Workbench version: {GetType().Assembly.GetName().Version?.ToString() ?? "unknown"}\n{((_toolProfile == AiConversationProfile.WorkbenchEvolution) ? "Current reload capability: ui.theme color tokens only; C#/XAML feature code requires a validated build and controlled restart. Generated source is not applied or executed." : "External target integration and runtime capabilities must be verified before claiming support. Generated code is an inert proposal until separately reviewed and built.")}";
			AiQaConversationReply reply = await aiQaConversationService.SendAsync(request, history, savedEvidenceContext, _toolProfile);
			await _toolConversationStore.AppendAsync("assistant", reply.Content, reply.ProviderModel);
			_toolConversation.Add(new SavedAiQaConversationTurn("assistant", reply.Content, reply.ProviderModel, reply.ReceivedAt));
			_lastToolPrompt = request;
			_lastToolResponse = reply.Content;
			_lastToolModel = reply.ProviderModel;
			RenderToolConversation();
			ToolStudioContent.ClearPrompt();
			ToolStudioContent.SetStatus(ToolProfileLabel(_toolProfile) + " 답변 · " + reply.ProviderModel + " · 저장 전 검토 가능");
			EventLog.AppendText("TOOL STUDIO REPLY · profile=" + ToolProfileLabel(_toolProfile) + " · saved in project-local chat; no source or game changes\n");
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException || ex is JsonException || ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			ToolStudioContent.SetStatus("AI 응답 실패 · 작성한 초안은 유지했습니다.");
			EventLog.AppendText("TOOL STUDIO REQUEST FAILED · " + ex.GetType().Name + "\n");
			MessageBox.Show(ex.Message, "Tool Studio AI 요청 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_aiRequestBusy = false;
		}
	}

	private void ToolStudio_SaveRequested(object sender, RoutedEventArgs e)
	{
		if (_lastToolResponse == null)
		{
			int num = _toolConversation.FindLastIndex((SavedAiQaConversationTurn x) => x.Role == "assistant");
			int num2 = ((num < 0) ? (-1) : _toolConversation.FindLastIndex(num, (SavedAiQaConversationTurn x) => x.Role == "user"));
			if (num >= 0 && num2 >= 0)
			{
				_lastToolPrompt = _toolConversation[num2].Content;
				_lastToolResponse = _toolConversation[num].Content;
				_lastToolModel = _toolConversation[num].ProviderModel;
			}
		}
		if (string.IsNullOrWhiteSpace(_lastToolPrompt) || string.IsNullOrWhiteSpace(_lastToolResponse))
		{
			ToolStudioContent.SetStatus("먼저 AI 답변을 받아야 프로젝트 파일로 저장할 수 있습니다.");
			return;
		}
		try
		{
			string text = _lastToolPrompt.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
			if (string.IsNullOrWhiteSpace(text))
			{
				text = ToolProfileLabel(_toolProfile);
			}
			if (text.Length > 100)
			{
				text = text.Substring(0, 100);
			}
			string text2 = ((_toolProfile == AiConversationProfile.WorkbenchEvolution) ? "self-evolution" : "tool-design");
			string gameVersion = ((_toolProfile != AiConversationProfile.WorkbenchEvolution) ? "사용자 지정 대상 · 버전 확인 필요" : (GetType().Assembly.GetName().Version?.ToString() ?? "unknown"));
			ProjectArtifactStore.Artifact artifact = ArtifactStore.CreateAiArtifact(text, _lastToolPrompt, _lastToolResponse, _lastToolModel, ToolStudioContent.TargetName, gameVersion, text2);
			_selectedArtifact = artifact;
			RefreshProjectArtifacts();
			ProjectArtifactList.SelectedItem = ProjectArtifactList.Items.Cast<ProjectArtifactStore.Artifact>().FirstOrDefault((ProjectArtifactStore.Artifact x) => x.Id == artifact.Id);
			MainStageNavigation.Select(MainStages, "project-files");
			ToolStudioContent.SetStatus("프로젝트 파일 생성됨 · " + artifact.Id + " · 저장 전에 내용을 확인하세요.");
			EventLog.AppendText($"TOOL ARTIFACT CREATED · profile={text2} · target={ToolStudioContent.TargetName} · {artifact.Id}\n");
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			MessageBox.Show(ex.Message, "Tool Studio 파일 저장 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private static string ToolWorkspaceFileLabel(ToolWorkspaceStore.GeneratedToolFile file)
	{
		return $"{file.WorkspaceId}  ·  {file.RelativePath}  ·  {file.Target}";
	}

	private void RefreshToolWorkspaceFiles()
	{
		ToolStudioContent.SetGeneratedPreview("파일을 선택하면 앱 안에서 내용을 미리 볼 수 있습니다.");
		try
		{
			_toolGeneratedFiles = _toolWorkspaceStore.ListFiles();
			ToolStudioContent.SetGeneratedFiles(_toolGeneratedFiles.Select(ToolWorkspaceFileLabel));
			if (_toolGeneratedFiles.Count == 0)
			{
				ToolStudioContent.SetGeneratedPreview("아직 생성된 코드 파일이 없습니다. AI에게 파일별 구현 초안을 요청한 뒤 ‘응답에서 코드 파일 생성’을 누르세요.");
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) ? 1 : 0) != 0)
		{
			_toolGeneratedFiles = Array.Empty<ToolWorkspaceStore.GeneratedToolFile>();
			ToolStudioContent.SetGeneratedFiles(Array.Empty<string>());
			ToolStudioContent.SetGeneratedPreview("도구 파일 목록을 안전하게 읽지 못했습니다: " + ex.Message);
		}
	}

	private void ToolStudio_GeneratedFileSelected(object sender, RoutedEventArgs e)
	{
		string selectedLabel = ToolStudioContent.SelectedGeneratedFile;
		ToolWorkspaceStore.GeneratedToolFile generatedToolFile = _toolGeneratedFiles.FirstOrDefault((ToolWorkspaceStore.GeneratedToolFile x) => ToolWorkspaceFileLabel(x) == selectedLabel);
		if ((object)generatedToolFile == null)
		{
			return;
		}
		try
		{
			ToolStudioContent.SetGeneratedPreview(_toolWorkspaceStore.Read(generatedToolFile));
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			ToolStudioContent.SetGeneratedPreview("파일을 읽지 않았습니다: " + ex.Message);
		}
	}

	private void ToolStudio_CreatePackageRequested(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_lastToolResponse))
		{
			int num = _toolConversation.FindLastIndex((SavedAiQaConversationTurn x) => x.Role == "assistant");
			int num2 = ((num < 0) ? (-1) : _toolConversation.FindLastIndex(num, (SavedAiQaConversationTurn x) => x.Role == "user"));
			if (num >= 0 && num2 >= 0)
			{
				_lastToolPrompt = _toolConversation[num2].Content;
				_lastToolResponse = _toolConversation[num].Content;
				_lastToolModel = _toolConversation[num].ProviderModel;
			}
		}
		if (string.IsNullOrWhiteSpace(_lastToolResponse))
		{
			ToolStudioContent.SetStatus("코드 파일이 포함된 AI 답변을 먼저 받아야 합니다.");
			return;
		}
		try
		{
			string text = ((_toolProfile == AiConversationProfile.WorkbenchEvolution) ? "self-evolution" : "tool-design");
			IReadOnlyList<ToolWorkspaceStore.GeneratedToolFile> readOnlyList = _toolWorkspaceStore.CreateFromResponse(ToolStudioContent.TargetName, text, _lastToolResponse);
			RefreshToolWorkspaceFiles();
			ToolStudioContent.SelectGeneratedFile(ToolWorkspaceFileLabel(readOnlyList[0]));
			ToolStudioContent.SetStatus($"도구 작업 파일 {readOnlyList.Count}개 생성됨 · 프로젝트 데이터 안에서 확인하세요. 자동 빌드·실행은 하지 않았습니다.");
			EventLog.AppendText($"TOOL WORKSPACE CREATED · profile={text} · target={ToolStudioContent.TargetName} · files={readOnlyList.Count} · isolated under project data\n");
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			ToolStudioContent.SetStatus(ex.Message);
			MessageBox.Show(ex.Message, "도구 코드 파일 생성 실패", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
	}

	private void ToolStudio_OpenFilesRequested(object sender, RoutedEventArgs e)
	{
		RefreshToolWorkspaceFiles();
		RefreshProjectArtifacts();
		MainStageNavigation.Select(MainStages, "project-files");
	}

	private void ToolStudio_OnlyChatRequested(object sender, RoutedEventArgs e)
	{
		if (!_aiRequestBusy && _runCancellation == null && _suiteCancellation == null && !_jiraSubmissionBusy)
		{
			OwnedJavaServerProcess javaServer = _javaServer;
			if (javaServer == null || javaServer.HasExited)
			{
				QaProjectDefinition activeProject = new QaProjectCatalog(_appDataRoot).GetActiveProject();
				if ((object)activeProject == null)
				{
					MessageBox.Show(this, "Only Chat 결과를 분리 저장할 프로젝트를 먼저 만들거나 선택하세요.", "프로젝트 필요", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					return;
				}
				string text = ToolStudioContent.PromptText.Trim();
				if (text.Length == 0)
				{
					ToolStudioContent.SetStatus("Only Chat에 보낼 작업 지시를 먼저 입력하세요.");
					return;
				}
				string workspacePath = ((_toolProfile == AiConversationProfile.WorkbenchEvolution) ? (ResolveCanonicalSourceRoot() ?? activeProject.RootPath) : activeProject.RootPath);
				new OnlyChatWorkDialog(this, activeProject, ToolProfileLabel(_toolProfile), ToolStudioContent.TargetName, text, workspacePath).ShowDialog();
				return;
			}
		}
		MessageBox.Show(this, "현재 앱의 실행 중 작업을 먼저 마무리한 뒤 Only Chat 작업을 시작하세요.", "작업 공간 사용 중", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private async void ToolStudio_OnlyChatConnectRequested(object sender, RoutedEventArgs e)
	{
		_workspaceShell?.ShowOnlyChat();
		await OnlyChatEmbeddedContent.ConnectAsync();
	}

	private static string? ResolveCanonicalSourceRoot()
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "gdrive", "My Drive", "Codex App Registry", "APP_PATH_REGISTRY.json");
			if (!File.Exists(text) || new FileInfo(text).Length > 2097152)
			{
				return null;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(text));
			if (!jsonDocument.RootElement.TryGetProperty("apps", out var value) || value.ValueKind != JsonValueKind.Array)
			{
				return null;
			}
			JsonElement value4;
			JsonElement jsonElement = value.EnumerateArray().FirstOrDefault((JsonElement x) => x.TryGetProperty("id", out value4) && value4.GetString() == "enderman-qa-workbench");
			if (jsonElement.ValueKind != JsonValueKind.Object || !jsonElement.TryGetProperty("windows", out var value2) || !value2.TryGetProperty("source", out var value3))
			{
				return null;
			}
			string @string = value3.GetString();
			if (string.IsNullOrWhiteSpace(@string) || !Path.IsPathFullyQualified(@string) || !Directory.Exists(@string))
			{
				return null;
			}
			string text2 = Path.Combine(@string, "app", "EndermanQa.Desktop", "EndermanQa.Desktop.csproj");
			string typeVersion = GetTypeVersion();
			if (!File.Exists(text2) || new FileInfo(text2).Length > 262144)
			{
				return null;
			}
			return Regex.IsMatch(File.ReadAllText(text2), "<Version>\\s*" + Regex.Escape(typeVersion) + "\\s*</Version>") ? Path.GetFullPath(@string) : null;
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static string GetTypeVersion()
	{
		string text = typeof(MainWindow).Assembly.GetName().Version?.ToString();
		object obj;
		if (text == null || !text.EndsWith(".0", StringComparison.Ordinal))
		{
			obj = typeof(MainWindow).Assembly.GetName().Version?.ToString();
			if (obj == null)
			{
				return "unknown";
			}
		}
		else
		{
			string text2 = text;
			obj = text2.Substring(0, text2.Length - 2);
		}
		return (string)obj;
	}

	private async void VirtualDisplayStatus_Click(object sender, RoutedEventArgs e)
	{
		VdmLeaseV2PipeClient.ProviderReadiness readiness = new VdmLeaseV2PipeClient().ProbeReadiness();
		string text;
		try
		{
			VirtualDisplayManagerStatus virtualDisplayManagerStatus = await _virtualDisplayManager.GetStatusAsync();
			text = $"v1 debug/status: {virtualDisplayManagerStatus.State} · displayEnabled={virtualDisplayManagerStatus.DisplayEnabled} · run {virtualDisplayManagerStatus.RunId ?? "(unknown)"}";
		}
		catch (Exception ex) when (((ex is IOException || ex is TimeoutException || ex is UnauthorizedAccessException || ex is OperationCanceledException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			text = "v1 debug/status: unavailable (" + ex.GetType().Name + ")";
		}
		string messageBoxText = $"{text}\n\nv2 task lease: {readiness.State}\n{readiness.Detail}\n\n" + "이 확인은 v2 lease를 요청하거나 디스플레이를 바꾸지 않습니다. v1 활성 상태는 격리·비미러링 증명이 아닙니다. 클라이언트 시작 버튼을 누르고 실제 lease가 발급·검증될 때에만 Minecraft를 시작합니다.";
		EventLog.AppendText($"VDM READINESS · debug={text.Split('\n')[0]} · lease={readiness.State}\n");
		MessageBox.Show(this, messageBoxText, "AI Virtual Display Manager", MessageBoxButton.OK, (readiness.State == "endpoint-present") ? MessageBoxImage.Asterisk : MessageBoxImage.Exclamation);
	}

	private void PersistAiDraft()
	{
		if (_restoringAiState)
		{
			return;
		}
		try
		{
			_aiConversationStore.SaveDraft(AiChatInput.Text);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			EventLog.AppendText("AI DRAFT SAVE WARNING · " + ex.Message + "\n");
		}
	}

	private void RefreshCurrentProjectLabel()
	{
		QaProjectDefinition activeProject = new QaProjectCatalog(_appDataRoot).GetActiveProject();
		CurrentProjectName.Text = activeProject?.Name ?? "기본 작업 공간";
		_workspaceShell?.UpdateProjectName(CurrentProjectName.Text);
		StageProjectName.Text = CurrentProjectName.Text;
		StageContextLabel.Text = $"{CurrentProjectName.Text}  ·  {((ComboBoxItem)GameTargetPicker.SelectedItem)?.Content ?? "Minecraft Java Edition"}  ·  엔진 {QaProjectCatalog.GetProjectEngineId(activeProject)}  ·  {SelectedGameVersionBox.Text}";
		ArtifactProjectPathLabel.Text = "저장 위치: " + _dataRoot + "\\generated  ·  현재 프로젝트에만 보입니다.";
	}

	private void RestoreProjectGameContext(QaProjectDefinition? project)
	{
		_restoringProjectGameContext = true;
		try
		{
			string gameTarget = (string.IsNullOrWhiteSpace(project?.GameTarget) ? "Minecraft Java Edition" : project.GameTarget);
			ComboBoxItem comboBoxItem = GameTargetPicker.Items.Cast<ComboBoxItem>().ToArray().FirstOrDefault(delegate(ComboBoxItem item)
			{
				if (!string.Equals(item.Content?.ToString(), gameTarget, StringComparison.Ordinal))
				{
					object content = item.Content;
					if (content == null)
					{
						return false;
					}
					return (content.ToString()?.StartsWith(gameTarget + " · ", StringComparison.Ordinal)).GetValueOrDefault();
				}
				return true;
			});
			if (comboBoxItem == null)
			{
				comboBoxItem = new ComboBoxItem
				{
					Content = gameTarget + " · " + QaProjectCatalog.GetProjectEngineId(project) + " · 어댑터 미등록"
				};
				GameTargetPicker.Items.Add(comboBoxItem);
				GameTargetPicker.Items.Cast<ComboBoxItem>().ToArray();
			}
			else if (!string.Equals(gameTarget, "Minecraft Java Edition", StringComparison.Ordinal))
			{
				comboBoxItem.Content = gameTarget + " · " + QaProjectCatalog.GetProjectEngineId(project) + " · 어댑터 미등록";
			}
			GameTargetPicker.SelectedItem = comboBoxItem;
			SelectedGameVersionBox.Text = (string.IsNullOrWhiteSpace(project?.GameVersionMemo) ? "버전 확인 필요" : project.GameVersionMemo);
		}
		finally
		{
			_restoringProjectGameContext = false;
		}
	}

	private void PersistProjectGameContext()
	{
		if (_restoringProjectGameContext)
		{
			return;
		}
		QaProjectCatalog qaProjectCatalog = new QaProjectCatalog(_appDataRoot);
		QaProjectDefinition activeProject = qaProjectCatalog.GetActiveProject();
		if ((object)activeProject != null)
		{
			string text = (GameTargetPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? activeProject.GameTarget ?? "Minecraft Java Edition";
			int num = text.IndexOf(" · ", StringComparison.Ordinal);
			string text2 = ((num > 0) ? text.Substring(0, num) : text);
			string text3 = SelectedGameVersionBox.Text.Trim();
			string text4 = ((string.IsNullOrWhiteSpace(text3) || string.Equals(text3, "버전 확인 필요", StringComparison.Ordinal)) ? null : text3);
			if (!string.Equals(activeProject.GameTarget, text2, StringComparison.Ordinal) || !string.Equals(activeProject.GameVersionMemo ?? string.Empty, text4 ?? string.Empty, StringComparison.Ordinal))
			{
				qaProjectCatalog.UpdateProjectGameContext(activeProject.ProjectId, text2, text4);
			}
		}
	}

	private void TryPersistProjectGameContext(string source)
	{
		try
		{
			PersistProjectGameContext();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			EventLog.AppendText($"PROJECT GAME CONTEXT SAVE WARNING · {source} · {ex.GetType().Name}: {ex.Message}\n");
			TargetStatus.Text = "게임 버전 메모 저장 실패 · 다시 저장하거나 프로젝트 전환을 취소하세요.";
		}
	}

	private void GoToAiStage_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			PersistProjectGameContext();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			MessageBox.Show("게임 버전 메모를 저장하지 못해 다음 단계로 이동하지 않았습니다.\n" + ex.Message, "프로젝트 정보 저장 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		RefreshCurrentProjectLabel();
		MainStageNavigation.Select(MainStages, "ai-work");
		AiChatInput.Focus();
	}

	private void GoToArtifactsStage_Click(object sender, RoutedEventArgs e)
	{
		RefreshProjectArtifacts();
		MainStageNavigation.Select(MainStages, "project-files");
	}

	private void GoToJiraStage_Click(object sender, RoutedEventArgs e)
	{
		RefreshJiraStageStatus();
		MainStageNavigation.Select(MainStages, "jira-submit");
	}

	private void RefreshJiraStageStatus()
	{
		JiraConnectionSettings jiraConnectionSettings = new JiraConnectionStore(_appDataRoot, _dataRoot).Load();
		JiraStageStatus.Text = (((object)jiraConnectionSettings == null) ? "연결 설정 필요" : (jiraConnectionSettings.Site + "  ·  " + jiraConnectionSettings.ProjectKey));
	}

	private void RefreshProjectArtifacts()
	{
		string priorId = _selectedArtifact?.Id;
		IReadOnlyList<ProjectArtifactStore.Artifact> readOnlyList = ArtifactStore.List();
		ProjectArtifactList.ItemsSource = readOnlyList;
		_selectedArtifact = readOnlyList.FirstOrDefault((ProjectArtifactStore.Artifact x) => x.Id == priorId);
		ProjectArtifactList.SelectedItem = _selectedArtifact;
		if ((object)_selectedArtifact == null)
		{
			ArtifactPreviewTitle.Text = ((readOnlyList.Count == 0) ? "아직 저장된 파일이 없습니다." : "파일을 선택하면 미리보기가 표시됩니다.");
			ArtifactPreview.Text = ((readOnlyList.Count == 0) ? "AI 답변을 만든 뒤 ‘AI 답변을 프로젝트 파일로 저장’을 누르면 이 프로젝트의 generated 폴더에 Markdown 파일이 만들어집니다." : string.Empty);
			ArtifactHashLabel.Text = string.Empty;
		}
	}

	private void ProjectArtifactList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		_selectedArtifact = ProjectArtifactList.SelectedItem as ProjectArtifactStore.Artifact;
		if ((object)_selectedArtifact == null)
		{
			return;
		}
		try
		{
			ArtifactPreviewTitle.Text = _selectedArtifact.Title;
			ArtifactPreview.Text = ArtifactStore.Read(_selectedArtifact);
			ArtifactHashLabel.Text = "SHA-256 " + _selectedArtifact.Sha256.Substring(0, 16) + "…  ·  " + _selectedArtifact.FilePath;
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			ArtifactPreview.Text = "파일 미리보기 실패: " + ex.Message;
		}
	}

	private void RefreshProjectArtifacts_Click(object sender, RoutedEventArgs e)
	{
		RefreshProjectArtifacts();
	}

	private void SaveAiArtifact_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_lastAiPrompt) || string.IsNullOrWhiteSpace(_lastAiResponse))
		{
			int num = _aiConversation.FindLastIndex((SavedAiQaConversationTurn x) => x.Role == "assistant");
			if (num >= 0)
			{
				int num2 = _aiConversation.FindLastIndex(num, (SavedAiQaConversationTurn x) => x.Role == "user");
				if (num2 >= 0)
				{
					_lastAiPrompt = _aiConversation[num2].Content;
					_lastAiResponse = _aiConversation[num].Content;
					_lastAiModel = _aiConversation[num].ProviderModel;
				}
			}
		}
		if (string.IsNullOrWhiteSpace(_lastAiPrompt) || string.IsNullOrWhiteSpace(_lastAiResponse))
		{
			MessageBox.Show("먼저 AI에게 요청을 보내고 답변을 받은 뒤 저장하세요. 기존 대화 기록은 자동으로 산출물로 바꾸지 않습니다.", "저장할 AI 답변 없음", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		try
		{
			string text = _lastAiPrompt.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
			if (string.IsNullOrWhiteSpace(text))
			{
				text = "AI QA 작업 초안";
			}
			if (text.Length > 100)
			{
				text = text.Substring(0, 100);
			}
			string gameTarget = (GameTargetPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Minecraft Java Edition";
			_selectedArtifact = ArtifactStore.CreateAiArtifact(text, _lastAiPrompt, _lastAiResponse, _lastAiModel, gameTarget, SelectedGameVersionBox.Text.Trim());
			RefreshProjectArtifacts();
			ProjectArtifactList.SelectedItem = ProjectArtifactList.Items.Cast<ProjectArtifactStore.Artifact>().FirstOrDefault((ProjectArtifactStore.Artifact x) => x.Id == _selectedArtifact.Id);
			EventLog.AppendText($"PROJECT ARTIFACT CREATED · {_selectedArtifact.Id} · contract v{1}\n");
			MessageBox.Show("AI 답변을 현재 프로젝트의 generated 폴더에 저장했습니다. 파일 단계에서 내용을 확인할 수 있습니다.", "프로젝트 파일 생성", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			MessageBox.Show(ex.Message, "프로젝트 파일 저장 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async void CreateArtifactJiraIssue_Click(object sender, RoutedEventArgs e)
	{
		if (_jiraSubmissionBusy)
		{
			return;
		}
		if ((object)_selectedArtifact == null)
		{
			MessageBox.Show("먼저 3단계에서 Jira에 제출할 파일을 선택하세요.", "파일 선택 필요", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		JiraConnectionSettings settings;
		try
		{
			settings = GetJiraSettings(promptIfMissing: true);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			MessageBox.Show(ex.Message, "Jira 설정 확인 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if ((object)settings == null || string.IsNullOrWhiteSpace(settings.ProjectKey))
		{
			MessageBox.Show("Jira 연결과 프로젝트 키를 설정하세요.", "Jira 목적지 필요", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			if (!Uri.TryCreate(settings.Site, UriKind.Absolute, out Uri site))
			{
				return;
			}
			string value;
			try
			{
				value = ArtifactStore.Read(_selectedArtifact);
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
			{
				MessageBox.Show(ex2.Message, "산출물 확인 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			string submissionId = settings.ProjectKey + ":artifact:" + _selectedArtifact.Sha256;
			JiraConnectionStore ledger = new JiraConnectionStore(_appDataRoot, _dataRoot);
			JiraSubmissionEntry jiraSubmissionEntry = ledger.LoadLedger().FirstOrDefault((JiraSubmissionEntry x) => x.SubmissionId == submissionId);
			if ((object)jiraSubmissionEntry != null)
			{
				MessageBox.Show("같은 산출물이 이미 등록되어 있습니다: " + jiraSubmissionEntry.IssueKey, "중복 제출 방지", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			else
			{
				if (MessageBox.Show($"현재 선택한 파일을 Jira 작업으로 제출합니다.\n\n프로젝트: {settings.ProjectKey}\n제목: {_selectedArtifact.Title}\n첨부: Markdown 산출물 1개\n\n계속할까요?", "Jira 제출 확인", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
				{
					return;
				}
				_jiraSubmissionBusy = true;
				try
				{
					using HttpClient http = new HttpClient(new HttpClientHandler
					{
						AllowAutoRedirect = false
					})
					{
						Timeout = TimeSpan.FromSeconds(30.0)
					};
					JiraCloudClient client = new JiraCloudClient(http);
					string description = $"Enderman QA Workbench project artifact\nArtifact ID: {_selectedArtifact.Id}\nContract: v{1}\n\n{value}";
					string key = await client.CreateIssueAsync(site, settings.Email, settings.ApiToken, settings.ProjectKey, "Task", "QA 산출물: " + _selectedArtifact.Title, description);
					if (!ledger.TryRecordSubmission(submissionId, _selectedArtifact.Id, "project-artifact", key))
					{
						throw new InvalidOperationException("Jira 이슈 " + key + "는 생성했지만 중복 방지 기록 저장에 실패했습니다. Jira에서 확인한 뒤 재제출하세요.");
					}
					try
					{
						await client.UploadAttachmentAsync(site, settings.Email, settings.ApiToken, key, _selectedArtifact.FilePath);
						TargetStatus.Text = "Jira 제출 완료 · " + key;
						EventLog.AppendText($"JIRA ARTIFACT SUBMITTED · {key} · project artifact {_selectedArtifact.Id} · attachment uploaded\n");
						MessageBox.Show("Jira에 등록하고 Markdown 파일을 첨부했습니다: " + key, "Jira 제출 완료", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					}
					catch (Exception ex3) when (((ex3 is HttpRequestException || ex3 is TaskCanceledException || ex3 is IOException || ex3 is ArgumentException) ? 1 : 0) != 0)
					{
						TargetStatus.Text = "Jira 이슈 생성됨 · 첨부 확인 필요 · " + key;
						EventLog.AppendText($"JIRA ARTIFACT ISSUE CREATED BUT ATTACHMENT FAILED · {key} · {ex3.GetType().Name}\n");
						MessageBox.Show("Jira 작업 " + key + "는 생성했지만 Markdown 첨부는 실패했습니다. 중복 등록을 막기 위해 완료 기록은 보존했습니다.\n\n" + ex3.Message, "Jira 첨부 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					}
				}
				catch (Exception ex4) when (((ex4 is HttpRequestException || ex4 is TaskCanceledException || ex4 is ArgumentException || ex4 is JsonException || ex4 is IOException || ex4 is UnauthorizedAccessException || ex4 is InvalidOperationException) ? 1 : 0) != 0)
				{
					EventLog.AppendText($"JIRA ARTIFACT SUBMIT FAILED · {ex4.GetType().Name}: {ex4.Message}\n");
					MessageBox.Show(ex4.Message, "Jira 제출 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				finally
				{
					_jiraSubmissionBusy = false;
				}
			}
		}
	}

	private void ResetManualEditorForProject(string projectName)
	{
		_caseId = Guid.NewGuid().ToString("N");
		_caseStartedAt = DateTimeOffset.UtcNow;
		_manualEvidence.Clear();
		EvidenceList.Items.Clear();
		CaseTitle.Text = "";
		CaseTarget.Text = "Minecraft Java · " + projectName;
		GameVersion.Text = "기록 필요";
		Preconditions.Text = "";
		ManualSteps.Text = "";
		ExpectedResult.Text = "";
		ActualResult.Text = "";
		ManualNotes.Text = "";
		VerdictPicker.SelectedIndex = 0;
		_draftDirty = false;
	}

	private void RenderAiConversation()
	{
		AiChatHistory.Items.Clear();
		foreach (SavedAiQaConversationTurn item in _aiConversation.TakeLast(30))
		{
			AiChatHistory.Items.Add(((item.Role == "user") ? "나" : ("AI" + (string.IsNullOrWhiteSpace(item.ProviderModel) ? "" : (" (" + item.ProviderModel + ")")))) + ": " + item.Content);
		}
		if (AiChatHistory.Items.Count > 0)
		{
			AiChatHistory.ScrollIntoView(AiChatHistory.Items[AiChatHistory.Items.Count - 1]);
		}
	}

	private string BuildSelectedEvidenceContext()
	{
		QaProjectDefinition activeProject = new QaProjectCatalog(_appDataRoot).GetActiveProject();
		string text = (GameTargetPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Minecraft Java Edition";
		string text2 = $"QA 프로젝트: {activeProject?.Name ?? "기본 작업 공간"} ({activeProject?.ProjectId ?? "default"})\n게임 대상: {activeProject?.GameTarget ?? text}\n엔진/도구 계열: {QaProjectCatalog.GetProjectEngineId(activeProject)}\n게임 버전/환경: {SelectedGameVersionBox.Text.Trim()}\n{GameMacroWorkbenchContent.GetProjectCapabilityContext()}\n실제 어댑터가 없으면 실행 가능한 것처럼 제안하지 말고, 필요한 게임별 연결 요건을 알려주세요.\n";
		SuiteCaseRow selectedCase = SuiteResultsContent.SelectedCase;
		if ((object)selectedCase == null)
		{
			return text2;
		}
		return text2 + $"\n선택 케이스: {selectedCase.IdDisplay} {selectedCase.Title}\n상태/판정: {selectedCase.StatusDisplay} / {selectedCase.VerdictDisplay}\n기대: {selectedCase.ExpectedObservation}\n실제 관측: {selectedCase.ActualObservation}\n반복성: {selectedCase.Repeatability}\n범위: {selectedCase.Coverage}";
	}

	private async void AiChatSend_Click(object sender, RoutedEventArgs e)
	{
		if (_runCancellation != null || _aiRequestBusy)
		{
			return;
		}
		string request = AiChatInput.Text.Trim();
		if (string.IsNullOrWhiteSpace(request))
		{
			PlanSummary.Text = "AI에게 보낼 메시지를 입력하세요.";
			return;
		}
		_aiRequestBusy = true;
		Button sendButton = (Button)sender;
		sendButton.IsEnabled = false;
		try
		{
			await _aiConversationStore.AppendAsync("user", request);
			_aiConversation.Add(new SavedAiQaConversationTurn("user", request, null, DateTimeOffset.UtcNow));
			RenderAiConversation();
			using HttpClient http = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(50.0)
			};
			AiQaConversationService aiQaConversationService = new AiQaConversationService(new AiAccountRouterClient(http));
			AiQaConversationTurn[] history = _aiConversation.Take(_aiConversation.Count - 1).TakeLast(10).Select(delegate(SavedAiQaConversationTurn turn)
			{
				string role = turn.Role;
				string content;
				if (turn.Content.Length <= 2500)
				{
					content = turn.Content;
				}
				else
				{
					string content2 = turn.Content;
					int length = content2.Length;
					int num = length - 2500;
					content = content2.Substring(num, length - num);
				}
				return new AiQaConversationTurn(role, content);
			})
				.ToArray();
			AiQaConversationReply reply = await aiQaConversationService.SendAsync(request, history, BuildSelectedEvidenceContext());
			await _aiConversationStore.AppendAsync("assistant", reply.Content, reply.ProviderModel);
			_aiConversation.Add(new SavedAiQaConversationTurn("assistant", reply.Content, reply.ProviderModel, reply.ReceivedAt));
			_lastAiPrompt = request;
			_lastAiResponse = reply.Content;
			_lastAiModel = reply.ProviderModel;
			RenderAiConversation();
			AiChatInput.Clear();
			PlanSummary.Text = "Router AI 답변 · " + reply.ProviderModel + " · 테스트 실행은 하지 않았습니다.";
			EventLog.AppendText("AI CHAT REPLY · " + reply.ProviderModel + " · saved locally; no game action\n");
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException || ex is JsonException || ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			PlanSummary.Text = "AI 응답 실패 · 입력은 보존했습니다. Router 상태와 저장 권한을 확인하세요.";
			EventLog.AppendText($"AI CHAT FAILED · {ex.GetType().Name}: {ex.Message}\n");
			MessageBox.Show(ex.Message, "AI 대화 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_aiRequestBusy = false;
			sendButton.IsEnabled = true;
		}
	}

	private void WorkflowGuide_Click(object sender, RoutedEventArgs e)
	{
		bool flag = WorkflowGuideContent.Visibility != Visibility.Visible;
		WorkflowGuideContent.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		MainStages.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		WorkflowGuideButton.Content = (flag ? "QA 작업 화면으로" : "앱 작동 원리");
	}

	private void SelectJava_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Java runtime (java.exe)|java.exe",
			Title = "Java 25 실행 파일 선택"
		};
		if (openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			JavaPathBox.Text = openFileDialog.FileName;
		}
	}

	private async void PrepareJavaServer_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			StartJavaServerButton.IsEnabled = false;
			string modJarPath = Path.Combine(AppContext.BaseDirectory, "adapters", "endermanqa-fabric-bridge-0.1.2.jar");
			_javaWorkspace = new JavaServerWorkspaceManager(_appDataRoot, modJarPath).PrepareFresh(Guid.NewGuid().ToString("N"));
			GameMacroWorkbenchContent.SetSuggestedTargetId(_javaWorkspace.InstanceId);
			await new FabricServerInstaller(new HttpClient(new HttpClientHandler
			{
				AllowAutoRedirect = false
			}), Path.Combine(_appDataRoot, "instances")).InstallAsync(_javaWorkspace);
			PersistPreparedTargetRecord("prepared-not-started", eulaAccepted: false, worldCreated: false);
			ExistingInstanceIdBox.Text = _javaWorkspace.InstanceId;
			EulaAcceptedCheck.IsChecked = false;
			GameMacroWorkbenchContent.SetSuggestedTargetId(_javaWorkspace.InstanceId);
			EventLog.AppendText($"JAVA WORKSPACE PREPARED · 26.3 · {_javaWorkspace.InstanceId} · port {_javaWorkspace.ServerPort} · {_javaWorkspace.RootPath}\nEULA stays false until explicit confirmation.\n");
			TargetStatus.Text = "새 격리 서버 준비됨 · 아직 시작되지 않음";
			StartJavaServerButton.IsEnabled = true;
		}
		catch (Exception ex)
		{
			EventLog.AppendText("JAVA PREPARE FAILED · " + ex.Message + "\n");
			MessageBox.Show(ex.Message, "격리 서버 준비 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void PersistPreparedTargetRecord(string state, bool eulaAccepted, bool worldCreated)
	{
		JavaServerWorkspace workspace = _javaWorkspace ?? throw new InvalidOperationException("No app-owned server workspace is selected.");
		QaProjectDefinition project = new QaProjectCatalog(_appDataRoot).GetActiveProject() ?? throw new InvalidOperationException("Select a QA project before saving its target record.");
		string projectRoot = Path.GetFullPath(project.RootPath);
		string projectsRoot = Path.GetFullPath(Path.Combine(_appDataRoot, "projects"));
		if (!projectRoot.StartsWith(projectsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
			|| !projectRoot.Equals(Path.GetFullPath(_projectRoot), StringComparison.OrdinalIgnoreCase))
			throw new System.Security.SecurityException("Prepared target records can only be saved to the active app-owned QA project.");
		string expectedInstanceRoot = Path.GetFullPath(Path.Combine(_appDataRoot, "instances", workspace.InstanceId));
		if (!expectedInstanceRoot.Equals(Path.GetFullPath(workspace.RootPath), StringComparison.OrdinalIgnoreCase))
			throw new System.Security.SecurityException("Prepared target is not under the Workbench-owned instances directory.");
		string loader = Path.Combine(workspace.RootPath, "fabric-server-launch.jar");
		string api = Path.Combine(workspace.ModsPath, "fabric-api-0.165.0+26.3.jar");
		string bridge = Path.Combine(workspace.ModsPath, Path.GetFileName(workspace.ModJarPath));
		var record = new PreparedTargetRecord(
			1,
			typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown",
			project.ProjectId,
			workspace.InstanceId,
			workspace.RootPath,
			workspace.WorldPath,
			"26.3",
			"Fabric dedicated server",
			workspace.ServerPort,
			"127.0.0.1",
			false,
			state,
			eulaAccepted,
			worldCreated,
			Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loader))),
			Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(api))),
			Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bridge))),
			string.IsNullOrWhiteSpace(JavaPathBox.Text) ? null : JavaPathBox.Text.Trim(),
			null,
			DateTimeOffset.UtcNow);
		new PreparedTargetRecordStore().Write(projectRoot, record);
	}

	private void RestorePreparedTargetForProject(QaProjectDefinition? project)
	{
		_javaWorkspace = null;
		ExistingInstanceIdBox.Clear();
		EulaAcceptedCheck.IsChecked = false;
		StartJavaServerButton.IsEnabled = false;
		if (project == null) return;
		string bridgePath = Path.Combine(AppContext.BaseDirectory, "adapters", "endermanqa-fabric-bridge-0.1.2.jar");
		var manager = new JavaServerWorkspaceManager(_appDataRoot, bridgePath);
		try
		{
			PreparedTargetRecord? record = new PreparedTargetRecordStore().Read(_projectRoot);
			if (record == null)
			{
				PresentResumableTargetDiscovery(manager, recordRejected: false, reason: null);
				return;
			}
			if (!record.ProjectId.Equals(project.ProjectId, StringComparison.Ordinal))
				throw new System.Security.SecurityException("Target record belongs to a different QA project.");
			string expectedRoot = Path.GetFullPath(Path.Combine(_appDataRoot, "instances", record.InstanceId));
			if (!expectedRoot.Equals(Path.GetFullPath(record.InstanceRoot), StringComparison.OrdinalIgnoreCase)
				|| !Path.GetFullPath(Path.Combine(expectedRoot, "qa-world")).Equals(Path.GetFullPath(record.WorldPath), StringComparison.OrdinalIgnoreCase))
				throw new System.Security.SecurityException("Target record path does not match the app-owned instance ID.");
			ExistingInstanceIdBox.Text = record.InstanceId;
			GameMacroWorkbenchContent.SetSuggestedTargetId(record.InstanceId);
			if (record.State is "prepared-not-started" or "eula-accepted-not-started")
			{
				JavaServerWorkspace workspace = record.EulaAccepted
					? manager.ResumeAcceptedUnstarted(record.InstanceId)
					: manager.ResumePrepared(record.InstanceId);
				string serverHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(workspace.RootPath, "fabric-server-launch.jar"))));
				string apiHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(workspace.ModsPath, "fabric-api-0.165.0+26.3.jar"))));
				string bridgeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workspace.ModJarPath)));
				if (workspace.ServerPort != record.ServerPort
					|| !serverHash.Equals(record.ServerJarSha256, StringComparison.OrdinalIgnoreCase)
					|| !apiHash.Equals(record.FabricApiSha256, StringComparison.OrdinalIgnoreCase)
					|| !bridgeHash.Equals(record.BridgeJarSha256, StringComparison.OrdinalIgnoreCase))
					throw new System.Security.SecurityException("Prepared target record artifacts or port do not match the validated instance.");
				_javaWorkspace = workspace;
				EulaAcceptedCheck.IsChecked = record.EulaAccepted;
				StartJavaServerButton.IsEnabled = true;
				TargetStatus.Text = record.EulaAccepted
					? "격리 인스턴스 복구됨 · 서버 중지 · 사용자 EULA 동의 기록 보존 · 아직 시작되지 않음"
					: "준비된 격리 인스턴스 복구됨 · 서버 중지 · EULA 미동의 · 시작 전 명시 동의 필요";
				EventLog.AppendText($"PREPARED INSTANCE RESTORED · {workspace.InstanceId} · port {workspace.ServerPort} · EULA accepted={record.EulaAccepted} · no server started.\n");
			}
			else
			{
				EulaAcceptedCheck.IsChecked = record.EulaAccepted;
				TargetStatus.Text = $"저장된 격리 인스턴스 발견 · 상태 {record.State} · 서버를 다시 시작하려면 기존 인스턴스 불러오기를 사용하세요.";
				EventLog.AppendText($"INSTANCE RECORD FOUND · {record.InstanceId} · {record.State} · server/client not launched by restore.\n");
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException || ex is System.Security.SecurityException)
		{
			_javaWorkspace = null;
			ExistingInstanceIdBox.Clear();
			EulaAcceptedCheck.IsChecked = false;
			StartJavaServerButton.IsEnabled = false;
			EventLog.AppendText("PREPARED INSTANCE RESTORE REJECTED · " + ex.Message + "\n");
			try
			{
				PresentResumableTargetDiscovery(manager, recordRejected: true, reason: ex.Message);
			}
			catch (Exception discoveryError) when (discoveryError is IOException || discoveryError is UnauthorizedAccessException || discoveryError is InvalidDataException || discoveryError is InvalidOperationException || discoveryError is ArgumentException || discoveryError is System.Security.SecurityException)
			{
				TargetStatus.Text = "저장 대상 복구가 차단됐고 대체 인스턴스 검색도 안전 검증을 통과하지 못했습니다. 실행 기록을 확인하세요.";
				EventLog.AppendText("RESUMABLE INSTANCE DISCOVERY REJECTED · " + discoveryError.Message + "\n");
			}
		}
	}

	private void PresentResumableTargetDiscovery(JavaServerWorkspaceManager manager, bool recordRejected, string? reason)
	{
		IReadOnlyList<JavaServerWorkspace> discovered = manager.FindResumableInstances();
		if (discovered.Count == 0)
		{
			TargetStatus.Text = recordRejected
				? "저장 대상 복구가 차단됐습니다. 대체 재개 인스턴스가 없습니다. 실행 기록에서 저장 기록 또는 검증 오류를 확인하세요."
				: PreparedTargetDiscoveryPresentation.NoCandidates;
			return;
		}

		if (discovered.Count == 1)
		{
			ExistingInstanceIdBox.Text = discovered[0].InstanceId;
			if (!recordRejected)
				GameMacroWorkbenchContent.SetSuggestedTargetId(discovered[0].InstanceId);
			bool eulaAccepted = File.ReadAllText(discovered[0].EulaPath).Contains("eula=true", StringComparison.OrdinalIgnoreCase);
			TargetStatus.Text = recordRejected
				? $"저장 대상 복구가 차단됐지만 대체 준비 인스턴스 1개를 검증했습니다 · ID {discovered[0].InstanceId} · EULA {(eulaAccepted ? "동의 기록 있음" : "미동의")} · ID를 확인하고 기존 인스턴스 불러오기를 누르세요. 자동 연결/실행은 하지 않았습니다."
				: PreparedTargetDiscoveryPresentation.CandidateStatus(discovered[0], eulaAccepted);
			EventLog.AppendText(PreparedTargetDiscoveryPresentation.CandidateLogEntry(discovered[0], eulaAccepted) + "\n");
			if (recordRejected && !string.IsNullOrWhiteSpace(reason))
				EventLog.AppendText("PROJECT TARGET RECORD REJECTED · " + reason + "\n");
			return;
		}

		TargetStatus.Text = recordRejected
			? $"저장 대상 복구가 차단됐습니다. 대체 재개 인스턴스 {discovered.Count}개를 검증했습니다 · 실행 기록의 ID 목록에서 선택하세요. 자동 연결/실행은 하지 않았습니다."
			: PreparedTargetDiscoveryPresentation.MultipleCandidates(discovered.Count);
		foreach (JavaServerWorkspace candidate in discovered)
		{
			bool eulaAccepted = File.ReadAllText(candidate.EulaPath).Contains("eula=true", StringComparison.OrdinalIgnoreCase);
			EventLog.AppendText(PreparedTargetDiscoveryPresentation.CandidateLogEntry(candidate, eulaAccepted) + "\n");
		}
		if (recordRejected && !string.IsNullOrWhiteSpace(reason))
			EventLog.AppendText("PROJECT TARGET RECORD REJECTED · " + reason + "\n");
	}

	private void StartJavaServer_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			PlanSummary.Text = "현재는 Minecraft Enderman QA 템플릿에만 격리 Java 실행을 연결할 수 있습니다. 다른 게임은 해당 게임 어댑터가 등록된 후 매크로 작업대에서 실행해야 합니다.";
		}
		else
		{
			if ((object)_javaWorkspace == null || _javaServer != null || !EulaAcceptedCheck.IsChecked.GetValueOrDefault())
			{
				return;
			}
			try
			{
				if (!File.ReadAllText(_javaWorkspace.EulaPath).Contains("eula=true", StringComparison.OrdinalIgnoreCase))
				{
					JavaServerWorkspaceManager.AcceptMinecraftEula(_javaWorkspace, userExplicitlyAccepted: true, Path.Combine(_appDataRoot, "instances"));
				}
				PersistPreparedTargetRecord("eula-accepted-not-started", eulaAccepted: true, worldCreated: Directory.Exists(_javaWorkspace.WorldPath));
				_javaServer = OwnedJavaServerProcess.Start(_javaWorkspace, JavaPathBox.Text.Trim(), userAcceptedMinecraftEula: true, Path.Combine(_appDataRoot, "instances"));
				try { PersistPreparedTargetRecord("server-started", eulaAccepted: true, worldCreated: Directory.Exists(_javaWorkspace.WorldPath)); } catch (Exception recordError) { EventLog.AppendText("TARGET RECORD UPDATE WARNING · " + recordError.Message + "\n"); }
				StartJavaServerButton.IsEnabled = false;
				StopJavaServerButton.IsEnabled = true;
				JavaDeathSmokeButton.IsEnabled = true;
				JavaFallTestButton.IsEnabled = true;
				JavaWaterTestButton.IsEnabled = true;
				Tc014RelocationSmokeButton.IsEnabled = true;
				UpdateEndermanSuiteAvailability();
				StartJavaClientButton.IsEnabled = true;
				Tc014ClientTestButton.IsEnabled = false;
				Tc060ClientTestButton.IsEnabled = false;
				TargetStatus.Text = $"앱 소유 Java 서버 시작 · PID {_javaServer.ProcessId} · 격리 인스턴스 {_javaWorkspace.InstanceId}";
				EventLog.AppendText($"JAVA SERVER STARTED · pid {_javaServer.ProcessId} · private instance {_javaWorkspace.InstanceId}\n");
			}
			catch (Exception ex)
			{
				_javaServer = null;
				EventLog.AppendText("JAVA START FAILED · " + ex.Message + "\n");
				MessageBox.Show(ex.Message, "Java 서버 시작 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
	}

	private void OpenExistingJavaServer_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (_javaServer != null || _suiteCancellation != null || _runCancellation != null)
				throw new InvalidOperationException("Stop the active server or QA run before selecting an instance.");
			string modJarPath = Path.Combine(AppContext.BaseDirectory, "adapters", "endermanqa-fabric-bridge-0.1.2.jar");
			var manager = new JavaServerWorkspaceManager(_appDataRoot, modJarPath);
			JavaServerWorkspace selected = manager.OpenExistingOrResumePrepared(ExistingInstanceIdBox.Text.Trim(), out bool resumedPrepared);
			if (resumedPrepared)
			{
				_javaWorkspace = selected;
				GameMacroWorkbenchContent.SetSuggestedTargetId(selected.InstanceId);
				JavaPathBox.Text = GetDefaultJavaPath();
				bool eulaAccepted = File.ReadAllText(selected.EulaPath).Contains("eula=true", StringComparison.OrdinalIgnoreCase);
				EulaAcceptedCheck.IsChecked = eulaAccepted;
				StartJavaServerButton.IsEnabled = true;
				StartJavaClientButton.IsEnabled = false;
				TargetStatus.Text = eulaAccepted
					? "격리 인스턴스 불러옴 · 서버 중지 · 기존 사용자 EULA 동의 보존 · 월드 생성 전"
					: "준비된 격리 인스턴스 불러옴 · 서버 중지 · EULA 미동의";
				EventLog.AppendText($"PREPARED INSTANCE RESUMED · {selected.InstanceId} · EULA accepted={eulaAccepted} · server/client not launched.\n");
				return;
			}
			_javaWorkspace = manager.UpdateBridgeJarInStoppedInstance(selected);
			GameMacroWorkbenchContent.SetSuggestedTargetId(_javaWorkspace.InstanceId);
			JavaPathBox.Text = GetDefaultJavaPath();
			EulaAcceptedCheck.IsChecked = true;
			StartJavaServerButton.IsEnabled = true;
			StartJavaClientButton.IsEnabled = false;
			TargetStatus.Text = "기존 격리 월드 선택됨 · " + _javaWorkspace.InstanceId + " · 월드 보존";
			EventLog.AppendText($"EXISTING INSTANCE SELECTED · {_javaWorkspace.InstanceId}\nWorld preserved at {_javaWorkspace.WorldPath}\nBridge JAR updated only after stopped-instance checks.\n");
		}
		catch (Exception ex)
		{
			EventLog.AppendText("EXISTING/PREPARED INSTANCE OPEN FAILED · " + ex.Message + "\n");
			MessageBox.Show(ex.Message, "격리 인스턴스 불러오기 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}
	private async void RunEndermanSuite_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			PlanSummary.Text = "이 프로젝트는 Minecraft Enderman 100 TC 템플릿이 아닙니다. 게임 매크로 작업대에서 등록 어댑터를 확인하세요.";
			return;
		}
		OwnedJavaServerProcess javaServer = _javaServer;
		if (javaServer == null || javaServer.HasExited || _suiteCancellation != null || _runCancellation != null)
		{
			return;
		}
		string path2 = Path.Combine(_dataRoot, "case-catalog-100.json");
		if (!File.Exists(path2))
		{
			path2 = Path.Combine(_projectRoot, "case-catalog-100.json");
		}
		if (!File.Exists(path2))
		{
			EventLog.AppendText("ENDERMAN SUITE REFUSED · project has no Enderman catalog\n");
			return;
		}
		IReadOnlyList<ExpectedActionPlanCase> cases;
		try
		{
			cases = new ExpectedActionPlanStore().Load(path2);
		}
		catch (Exception ex)
		{
			EventLog.AppendText("SUITE PLAN INVALID · " + ex.Message + "\n");
			MessageBox.Show(ex.Message, "100건 계획 검증 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		_suiteCancellation = new CancellationTokenSource();
		CancellationToken token = _suiteCancellation.Token;
		RunEndermanSuiteButton.IsEnabled = false;
		StopRunButton.IsEnabled = true;
		int completed = 0;
		Progress<SuiteCaseReceipt> progress = new Progress<SuiteCaseReceipt>(delegate(SuiteCaseReceipt receipt)
		{
			completed++;
			EventLog.AppendText($"SUITE {completed}/100 · #{receipt.SourceId:000} · {receipt.Status} · {receipt.Verdict} · retries {receipt.RepeatRunIds.Count}\n");
			PlanSummary.Text = $"100건 같은 서버 순차 실행 · {completed}/100\n#{receipt.SourceId:000} {receipt.Status} / {receipt.Verdict}\n재현: {receipt.Repeatability ?? "해당 없음"}\n{receipt.Limitation ?? receipt.Coverage}";
		});
		try
		{
			IReadOnlyList<SuiteCaseReceipt> readOnlyList = await new EndermanSuiteOrchestrator(_dataRoot).RunAsync(cases, async (CancellationToken ct) => await _javaServer.ConnectGameAdapterAsync(ct), progress, token);
			DirectoryInfo directoryInfo = (from path in Directory.GetDirectories(Path.Combine(_dataRoot, "suite-runs"))
				select new DirectoryInfo(path) into info
				orderby info.CreationTimeUtc descending
				select info).FirstOrDefault();
			int num = readOnlyList.Count((SuiteCaseReceipt x) => x.FirstRunId != null);
			int value = readOnlyList.Count(delegate(SuiteCaseReceipt x)
			{
				string status = x.Status;
				return (status == "reproduced-anomaly" || status == "anomaly-unconfirmed") ? true : false;
			});
			PlanSummary.Text = $"100건 계획 처리 완료 · 실제 Minecraft 실행 {num}건 · 기능 미지원 {readOnlyList.Count - num}건 · 이상 {value}건\n같은 서버/월드 재사용 · 결과: {directoryInfo?.FullName}";
			EventLog.AppendText($"SUITE FINISHED · planned 100 · dispatched {num} · unsupported {readOnlyList.Count - num} · anomalies {value}\nResults: {directoryInfo?.FullName}\n");
		}
		catch (OperationCanceledException)
		{
			EventLog.AppendText($"SUITE STOPPED · checkpointed {completed}/100; completed journals retained.\n");
		}
		catch (Exception ex3)
		{
			EventLog.AppendText($"SUITE ERROR · checkpointed {completed}/100 · {ex3.Message}\n");
			MessageBox.Show(ex3.Message, "스위트 실행 오류", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_suiteCancellation.Dispose();
			_suiteCancellation = null;
			Button runEndermanSuiteButton = RunEndermanSuiteButton;
			javaServer = _javaServer;
			runEndermanSuiteButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			StopRunButton.IsEnabled = false;
		}
	}

	private async Task RunMacroAsync(ProjectQaMacro macro)
	{
		ProjectQaMacro macro2 = macro;
		OwnedJavaServerProcess javaServer = _javaServer;
		if (javaServer == null || javaServer.HasExited || _suiteCancellation != null || _runCancellation != null)
		{
			MacroAndStorageContent.SetRunActive(active: false, "매크로를 실행하려면 이 앱이 소유한 Java 격리 서버를 먼저 시작하세요.");
			return;
		}
		_suiteCancellation = new CancellationTokenSource();
		CancellationToken token = _suiteCancellation.Token;
		MacroAndStorageContent.SetRunActive(active: true, $"‘{macro2.Name}’ 매크로 실행 준비 · 케이스 {macro2.Cases.Count}개 · AI 요청 없음");
		RunEndermanSuiteButton.IsEnabled = false;
		RerunSuiteAnomaliesButton.IsEnabled = false;
		StopRunButton.IsEnabled = true;
		int completed = 0;
		Progress<SuiteCaseReceipt> progress = new Progress<SuiteCaseReceipt>(delegate(SuiteCaseReceipt receipt)
		{
			completed++;
			EventLog.AppendText($"MACRO {macro2.Name} · {completed}/{macro2.Cases.Count} · #{receipt.SourceId:000} · {receipt.Status}/{receipt.Verdict}\n");
			PlanSummary.Text = $"매크로 {macro2.Name} · {completed}/{macro2.Cases.Count}\n#{receipt.SourceId:000} {receipt.Status} / {receipt.Verdict}\n{receipt.Limitation ?? receipt.Coverage}";
		});
		try
		{
			IReadOnlyList<SuiteCaseReceipt> readOnlyList = await new EndermanSuiteOrchestrator(_dataRoot).RunSelectedAsync(macro2.Cases, async (CancellationToken ct) => await _javaServer.ConnectGameAdapterAsync(ct), progress, token);
			string text = Path.Combine(_dataRoot, "suite-runs");
			DirectoryInfo directoryInfo = (from path in Directory.GetDirectories(text)
				select new DirectoryInfo(path) into info
				orderby info.CreationTimeUtc descending
				select info).FirstOrDefault();
			int num = readOnlyList.Count((SuiteCaseReceipt x) => x.FirstRunId != null);
			new ProjectQaMacroStore(_dataRoot).AppendRunReceipt(macro2, directoryInfo?.FullName ?? text, $"completed; planned={readOnlyList.Count}; dispatched={num}; unsupported={readOnlyList.Count - num}");
			MacroAndStorageContent.SetRunActive(active: false, $"매크로 완료 · 기록 {readOnlyList.Count}건 · 실제 게임 실행 {num}건 · 미지원 {readOnlyList.Count - num}건");
			EventLog.AppendText($"MACRO FINISHED · {macro2.Name} · planned {readOnlyList.Count} · dispatched {num}\nResults: {directoryInfo?.FullName}\n");
		}
		catch (OperationCanceledException)
		{
			new ProjectQaMacroStore(_dataRoot).AppendRunReceipt(macro2, Path.Combine(_dataRoot, "suite-runs"), $"stopped-after-checkpoint={completed}");
			MacroAndStorageContent.SetRunActive(active: false, $"중단됨 · {completed}/{macro2.Cases.Count} 케이스까지 실행 기록 보존");
		}
		catch (Exception ex2)
		{
			MacroAndStorageContent.SetRunActive(active: false, "매크로 오류: " + ex2.Message);
			EventLog.AppendText($"MACRO ERROR · {macro2.Name} · {ex2.Message}\n");
			MessageBox.Show(ex2.Message, "QA 매크로 실행 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_suiteCancellation.Dispose();
			_suiteCancellation = null;
			Button runEndermanSuiteButton = RunEndermanSuiteButton;
			javaServer = _javaServer;
			runEndermanSuiteButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			Button rerunSuiteAnomaliesButton = RerunSuiteAnomaliesButton;
			javaServer = _javaServer;
			rerunSuiteAnomaliesButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			StopRunButton.IsEnabled = false;
		}
	}

	private async void RerunSuiteAnomalies_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			PlanSummary.Text = "이 프로젝트에는 Enderman 케이스가 없습니다.";
			return;
		}
		OwnedJavaServerProcess javaServer = _javaServer;
		if (javaServer == null || javaServer.HasExited || _suiteCancellation != null || _runCancellation != null)
		{
			return;
		}
		_suiteCancellation = new CancellationTokenSource();
		CancellationToken token = _suiteCancellation.Token;
		RunEndermanSuiteButton.IsEnabled = false;
		RerunSuiteAnomaliesButton.IsEnabled = false;
		StopRunButton.IsEnabled = true;
		int count = 0;
		Progress<SuiteCaseReceipt> progress = new Progress<SuiteCaseReceipt>(delegate(SuiteCaseReceipt receipt)
		{
			count++;
			EventLog.AppendText($"ANOMALY RETEST {count} · #{receipt.SourceId:000} · {receipt.Status} · {receipt.Verdict} · retries {receipt.RepeatRunIds.Count}\n");
			PlanSummary.Text = $"이상/설정불가 케이스 재실행 · {count}\n#{receipt.SourceId:000} {receipt.Status} / {receipt.Verdict}\n{receipt.Repeatability ?? "재현 확인 전"}";
		});
		try
		{
			IReadOnlyList<SuiteCaseReceipt> readOnlyList = await new EndermanSuiteOrchestrator(_dataRoot).RerunAnomaliesAsync(async (CancellationToken ct) => await _javaServer.ConnectGameAdapterAsync(ct), progress, token);
			EventLog.AppendText($"ANOMALY RETEST FINISHED · selected {readOnlyList.Count} · new results saved in {Path.Combine(_dataRoot, "suite-runs")}\n");
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			EventLog.AppendText("ANOMALY RETEST FAILED · " + ex.Message + "\n");
			MessageBox.Show(ex.Message, "이상 케이스 재실행 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_suiteCancellation.Dispose();
			_suiteCancellation = null;
			Button runEndermanSuiteButton = RunEndermanSuiteButton;
			javaServer = _javaServer;
			runEndermanSuiteButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			Button rerunSuiteAnomaliesButton = RerunSuiteAnomaliesButton;
			javaServer = _javaServer;
			rerunSuiteAnomaliesButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			StopRunButton.IsEnabled = false;
		}
	}

	private async void StopJavaServer_Click(object sender, RoutedEventArgs e)
	{
		if (_javaServer == null)
		{
			return;
		}
		if (_suiteCancellation != null)
		{
			_suiteCancellation.Cancel();
			EventLog.AppendText("JAVA STOP REQUESTED · active suite cancelled and checkpointed first; press stop again after fixture reconciliation.\n");
			return;
		}
		if (_runCancellation != null)
		{
			_runCancellation.Cancel();
			EventLog.AppendText("JAVA STOP REQUESTED · active QA run cancelled first; press stop again after it checkpoints.\n");
			return;
		}
		int pid = _javaServer.ProcessId;
		try
		{
			_ = 1;
			try
			{
				if (_javaClient != null)
				{
					await StopOwnedJavaClientAsync();
				}
				await _javaServer.StopAsync(TimeSpan.FromSeconds(20.0));
				EventLog.AppendText($"JAVA SERVER STOPPED · owned pid {pid}\n");
				try { PersistPreparedTargetRecord(Directory.Exists(_javaWorkspace?.WorldPath) ? "world-ready" : "eula-accepted-not-started", eulaAccepted: true, worldCreated: Directory.Exists(_javaWorkspace?.WorldPath)); } catch (Exception recordError) { EventLog.AppendText("TARGET RECORD UPDATE WARNING · " + recordError.Message + "\n"); }
			}
			catch (Exception ex)
			{
				EventLog.AppendText($"JAVA STOP FAILED · owned pid {pid} · {ex.Message}\n");
			}
		}
		finally
		{
			await _javaServer.DisposeAsync();
			_javaServer = null;
			StopJavaServerButton.IsEnabled = false;
			StartJavaClientButton.IsEnabled = false;
			CaptureClientButton.IsEnabled = false;
			StopJavaClientButton.IsEnabled = false;
			Tc014ClientTestButton.IsEnabled = false;
			Tc060ClientTestButton.IsEnabled = false;
			JavaDeathSmokeButton.IsEnabled = false;
			JavaFallTestButton.IsEnabled = false;
			JavaWaterTestButton.IsEnabled = false;
			Tc014RelocationSmokeButton.IsEnabled = false;
			RunEndermanSuiteButton.IsEnabled = false;
			RerunSuiteAnomaliesButton.IsEnabled = false;
			StartJavaServerButton.IsEnabled = (object)_javaWorkspace != null;
			ClientSessionStatus.Text = "Minecraft 클라이언트: 꺼짐";
			TargetStatus.Text = "앱 소유 Java 서버 중지됨";
		}
	}

	private async void StartJavaClient_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			PlanSummary.Text = "Minecraft 클라이언트 조작은 Enderman 전용 QA 프로젝트에서만 허용됩니다.";
		}
		else
		{
			if (_javaClientStarting || _javaClient != null)
			{
				return;
			}
			OwnedJavaServerProcess javaServer = _javaServer;
			if (javaServer == null || javaServer.HasExited || (object)_javaWorkspace == null)
			{
				return;
			}
			_javaClientStarting = true;
			StartJavaClientButton.IsEnabled = false;
			UseLatencyProxyCheckBox.IsEnabled = false;
			ClientToServerDelayBox.IsEnabled = false;
			ServerToClientDelayBox.IsEnabled = false;
			_javaClientProjectDataRoot = Path.GetFullPath(_dataRoot);
			ClientSessionStatus.Text = "VDM에 이 실행 전용 가상 화면 lease를 요청하는 중…";
			VerifiedClientDisplayLease lease = null;
			OwnedLoopbackLatencyProxy latencyProxy = null;
			try
			{
				Guid clientSessionId = Guid.NewGuid();
				lease = await VerifiedClientDisplayLease.AcquireAsync(Guid.NewGuid(), clientSessionId, _javaWorkspace.InstanceId);
				if (UseLatencyProxyCheckBox.IsChecked.GetValueOrDefault())
				{
					int result;
					bool flag = !int.TryParse(ClientToServerDelayBox.Text, out result);
					if (!flag)
					{
						bool flag2 = ((result < 0 || result > 500) ? true : false);
						flag = flag2;
					}
					int result2 = default(int);
					bool flag3 = flag || !int.TryParse(ServerToClientDelayBox.Text, out result2);
					if (!flag3)
					{
						bool flag2 = ((result2 < 0 || result2 > 500) ? true : false);
						flag3 = flag2;
					}
					if (flag3)
					{
						throw new InvalidOperationException("지연 값은 방향별로 0–500 ms 정수여야 합니다.");
					}
					latencyProxy = OwnedLoopbackLatencyProxy.Start(_javaServer, Path.Combine(_appDataRoot, "instances"), TimeSpan.FromMilliseconds(result), TimeSpan.FromMilliseconds(result2));
				}
				string javaExecutable = (string.IsNullOrWhiteSpace(JavaPathBox.Text) ? GetDefaultJavaPath() : JavaPathBox.Text.Trim());
				string clientModuleDirectory = Path.Combine(AppContext.BaseDirectory, "ClientAdapter");
				_javaClient = await OwnedJavaClientProcess.StartAsync(_javaWorkspace, Path.Combine(_appDataRoot, "instances"), _javaServer, javaExecutable, clientModuleDirectory, lease, default(CancellationToken), latencyProxy);
				lease = null;
				_javaLatencyProxy = latencyProxy;
				latencyProxy = null;
				ClientSessionStatus.Text = ((_javaLatencyProxy == null) ? $"Minecraft 클라이언트 실행 중 · PID {_javaClient.ClientProcessId} · leased virtual output verified" : $"Minecraft 클라이언트 실행 중 · TCP stream delay {_javaLatencyProxy.ClientToServerDelayMilliseconds}/{_javaLatencyProxy.ServerToClientDelayMilliseconds} ms · 연결 {_javaLatencyProxy.ConnectedToServer}");
				CaptureClientButton.IsEnabled = true;
				StopJavaClientButton.IsEnabled = true;
				Button tc014ClientTestButton = Tc014ClientTestButton;
				int isEnabled;
				if (_javaLatencyProxy == null)
				{
					javaServer = _javaServer;
					if (javaServer != null && !javaServer.HasExited)
					{
						isEnabled = ((!_javaClient.HasExited) ? 1 : 0);
						goto IL_0413;
					}
				}
				isEnabled = 0;
				goto IL_0413;
				IL_046a:
				object tc060ClientTestButton;
				int isEnabled2;
				((UIElement)tc060ClientTestButton).IsEnabled = (byte)isEnabled2 != 0;
				EventLog.AppendText($"JAVA CLIENT STARTED · pid {_javaClient.ClientProcessId} · exact app-owned instance {_javaWorkspace.InstanceId} · VDM lease bound to this run\n");
				if (_javaLatencyProxy != null)
				{
					EventLog.AppendText($"LATENCY PROXY ACTIVE · C→S {_javaLatencyProxy.ClientToServerDelayMilliseconds}ms / S→C {_javaLatencyProxy.ServerToClientDelayMilliseconds}ms per TCP stream read · not packet RTT\n");
				}
				goto end_IL_00f0;
				IL_0413:
				tc014ClientTestButton.IsEnabled = (byte)isEnabled != 0;
				tc060ClientTestButton = Tc060ClientTestButton;
				OwnedLoopbackLatencyProxy javaLatencyProxy = _javaLatencyProxy;
				if (javaLatencyProxy != null && javaLatencyProxy.ClientAccepted && javaLatencyProxy.ConnectedToServer)
				{
					javaServer = _javaServer;
					if (javaServer != null && !javaServer.HasExited)
					{
						OwnedJavaClientProcess javaClient = _javaClient;
						isEnabled2 = ((javaClient != null && !javaClient.HasExited) ? 1 : 0);
						goto IL_046a;
					}
				}
				isEnabled2 = 0;
				goto IL_046a;
				end_IL_00f0:;
			}
			catch (Exception ex)
			{
				if (latencyProxy != null)
				{
					try
					{
						await latencyProxy.DisposeAsync();
					}
					catch
					{
					}
				}
				if (lease != null)
				{
					try
					{
						await lease.ReleaseAsync(CancellationToken.None);
					}
					catch
					{
					}
				}
				ClientSessionStatus.Text = "Minecraft 클라이언트 시작 차단 · " + ex.Message;
				_javaClientProjectDataRoot = null;
				EventLog.AppendText("JAVA CLIENT START BLOCKED · " + ex.Message + "\n");
				MessageBox.Show(ex.Message, "가상 화면 클라이언트 시작 불가", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			finally
			{
				_javaClientStarting = false;
				Button startJavaClientButton = StartJavaClientButton;
				int isEnabled3;
				if (_javaClient == null)
				{
					javaServer = _javaServer;
					isEnabled3 = ((javaServer != null && !javaServer.HasExited) ? 1 : 0);
				}
				else
				{
					isEnabled3 = 0;
				}
				startJavaClientButton.IsEnabled = (byte)isEnabled3 != 0;
				UseLatencyProxyCheckBox.IsEnabled = _javaClient == null;
				ClientToServerDelayBox.IsEnabled = _javaClient == null;
				ServerToClientDelayBox.IsEnabled = _javaClient == null;
				Button tc014ClientTestButton2 = Tc014ClientTestButton;
				int isEnabled4;
				if (_javaLatencyProxy == null)
				{
					javaServer = _javaServer;
					if (javaServer != null && !javaServer.HasExited)
					{
						OwnedJavaClientProcess javaClient = _javaClient;
						isEnabled4 = ((javaClient != null && !javaClient.HasExited) ? 1 : 0);
						goto IL_0792;
					}
				}
				isEnabled4 = 0;
				goto IL_0792;
				IL_07e9:
				object tc060ClientTestButton2;
				int isEnabled5;
				((UIElement)tc060ClientTestButton2).IsEnabled = (byte)isEnabled5 != 0;
				goto end_IL_06e7;
				IL_0792:
				tc014ClientTestButton2.IsEnabled = (byte)isEnabled4 != 0;
				tc060ClientTestButton2 = Tc060ClientTestButton;
				OwnedLoopbackLatencyProxy javaLatencyProxy = _javaLatencyProxy;
				if (javaLatencyProxy != null && javaLatencyProxy.ClientAccepted && javaLatencyProxy.ConnectedToServer)
				{
					javaServer = _javaServer;
					if (javaServer != null && !javaServer.HasExited)
					{
						OwnedJavaClientProcess javaClient = _javaClient;
						isEnabled5 = ((javaClient != null && !javaClient.HasExited) ? 1 : 0);
						goto IL_07e9;
					}
				}
				isEnabled5 = 0;
				goto IL_07e9;
				end_IL_06e7:;
			}
		}
	}

	private async void StopJavaClient_Click(object sender, RoutedEventArgs e)
	{
		await StopOwnedJavaClientAsync();
	}

	private async Task StopOwnedJavaClientAsync(bool updateUi = true)
	{
		OwnedJavaClientProcess client = _javaClient;
		if (client == null)
		{
			return;
		}
		StopJavaClientButton.IsEnabled = false;
		CaptureClientButton.IsEnabled = false;
		if (updateUi)
		{
			ClientSessionStatus.Text = "앱 소유 Minecraft 클라이언트 정상 종료 중…";
		}
		try
		{
			await client.StopAsync(TimeSpan.FromSeconds(20.0));
			EventLog.AppendText($"JAVA CLIENT STOPPED · owned pid {client.ClientProcessId} · exact lease released\n");
		}
		catch (Exception ex)
		{
			EventLog.AppendText($"JAVA CLIENT STOP ERROR · owned pid {client.ClientProcessId} · {ex.Message}\n");
			if (updateUi)
			{
				MessageBox.Show(ex.Message, "클라이언트 종료 오류", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		finally
		{
			OwnedLoopbackLatencyProxy latencyProxy = _javaLatencyProxy;
			if (latencyProxy != null)
			{
				try
				{
					await latencyProxy.DisposeAsync();
					string value = await latencyProxy.WriteSessionReceiptAsync(client.SessionRoot, client.ClientProcessId);
					EventLog.AppendText($"LATENCY PROXY CLOSED · accepted={latencyProxy.ClientAccepted} · connected={latencyProxy.ConnectedToServer} · bytes C→S={latencyProxy.ClientToServerBytes}, S→C={latencyProxy.ServerToClientBytes}\nEvidence: {value}\n");
				}
				catch (Exception ex2)
				{
					EventLog.AppendText($"LATENCY PROXY RECEIPT WARNING · {ex2.GetType().Name}: {ex2.Message}\n");
				}
				_javaLatencyProxy = null;
			}
			await client.DisposeAsync();
			_javaClient = null;
			_javaClientProjectDataRoot = null;
			StopJavaClientButton.IsEnabled = false;
			CaptureClientButton.IsEnabled = false;
			Tc014ClientTestButton.IsEnabled = false;
			Tc060ClientTestButton.IsEnabled = false;
			Button startJavaClientButton = StartJavaClientButton;
			OwnedJavaServerProcess javaServer = _javaServer;
			startJavaClientButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			UseLatencyProxyCheckBox.IsEnabled = true;
			ClientToServerDelayBox.IsEnabled = true;
			ServerToClientDelayBox.IsEnabled = true;
			if (updateUi)
			{
				ClientSessionStatus.Text = "Minecraft 클라이언트: 꺼짐 · lease 해제됨";
			}
			Tc014ClientTestButton.IsEnabled = false;
			Tc060ClientTestButton.IsEnabled = false;
		}
	}

	private async void CaptureClient_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			ClientSessionStatus.Text = "선택한 프로젝트는 Minecraft Enderman QA 대상이 아닙니다.";
			return;
		}
		OwnedJavaClientProcess client = _javaClient;
		if (client == null || client.HasExited)
		{
			return;
		}
		CaptureClientButton.IsEnabled = false;
		try
		{
			OwnedJavaClientProcess.CaptureEvidence evidence = await client.CaptureFramebufferAsync();
			string projectDataRoot = _javaClientProjectDataRoot ?? throw new InvalidOperationException("Client capture has no project root bound to its start action.");
			ClientFramebufferEvidenceStore.StoredEvidence storedEvidence = await new ClientFramebufferEvidenceStore().StoreAsync(Path.Combine(_appDataRoot, "instances"), projectDataRoot, client.SessionRoot, evidence, ClientCaptureCaseIdBox.Text);
			ClientSessionStatus.Text = $"스크린샷 저장 완료 · {storedEvidence.ProjectRelativeFolder} · SHA-256 {storedEvidence.Sha256.Substring(0, 12)}…";
			PlanSummary.Text = $"Minecraft 화면 캡처 저장 · {storedEvidence.Bytes:N0} bytes · {storedEvidence.ProjectRelativeFolder}\n케이스 연결: {storedEvidence.CaseReference ?? "미지정"}\n무결성: SHA-256 확인됨";
			EventLog.AppendText($"CLIENT FRAMEBUFFER CAPTURE STORED · {storedEvidence.ProjectRelativeFolder}\nPNG {storedEvidence.Bytes} bytes · SHA-256 {storedEvidence.Sha256}\nIndex: {storedEvidence.IndexPath}\n");
		}
		catch (Exception ex)
		{
			ClientSessionStatus.Text = "화면 캡처 차단/실패 · " + ex.Message;
			EventLog.AppendText("CLIENT CAPTURE FAILED · " + ex.Message + "\n");
			MessageBox.Show(ex.Message, "Minecraft 캡처 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			Button captureClientButton = CaptureClientButton;
			OwnedJavaClientProcess javaClient = _javaClient;
			captureClientButton.IsEnabled = javaClient != null && !javaClient.HasExited;
		}
	}

	private async void JavaFallTest_Click(object sender, RoutedEventArgs e)
	{
		if (IsEndermanSuiteProject())
		{
			await RunJavaHazardScenarioAsync(EndermanDeathScenarios.EnvironmentalFall());
		}
	}

	private async void JavaWaterTest_Click(object sender, RoutedEventArgs e)
	{
		if (IsEndermanSuiteProject())
		{
			await RunJavaHazardScenarioAsync(EndermanDeathScenarios.EnvironmentalWater());
		}
	}

	private async void Tc014RelocationSmoke_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			return;
		}
		OwnedJavaServerProcess javaServer = _javaServer;
		if (javaServer == null || javaServer.HasExited || (object)_javaWorkspace == null || _suiteCancellation != null || _runCancellation != null)
		{
			return;
		}
		_suiteCancellation = new CancellationTokenSource();
		CancellationToken token = _suiteCancellation.Token;
		Tc014RelocationSmokeButton.IsEnabled = false;
		RunEndermanSuiteButton.IsEnabled = false;
		RerunSuiteAnomaliesButton.IsEnabled = false;
		StopRunButton.IsEnabled = true;
		PlanSummary.Text = "TC-014 이동 어댑터 점검 중 · 서버 fixture만 확인하며 클라이언트 사례 판정은 하지 않습니다.";
		try
		{
			Tc014RelocationSmokeResult tc014RelocationSmokeResult = await Tc014RelocationSmokeRunner.RunAsync(_javaWorkspace, _javaServer, Path.Combine(_appDataRoot, "instances"), _dataRoot, token);
			EventLog.AppendText($"TC-014 ADAPTER SMOKE · server fixture verified · uuid {tc014RelocationSmokeResult.Receipt.EntityUuid} · released {tc014RelocationSmokeResult.Receipt.FixtureReleased} · sha256 {tc014RelocationSmokeResult.EvidenceSha256}\nEvidence: {tc014RelocationSmokeResult.EvidencePath}\nClient cached-target attack and TC-014 verdict were not run.\n");
			PlanSummary.Text = $"TC-014 서버 이동 adapter 점검 완료 · fixture 정리 {tc014RelocationSmokeResult.Receipt.FixtureReleased}\n클라이언트 캐시 공격은 미실행이며 공식 #14 결과는 NotRun 유지\n증거: {tc014RelocationSmokeResult.EvidencePath}";
		}
		catch (Exception ex)
		{
			EventLog.AppendText($"TC-014 ADAPTER SMOKE FAILED · {ex.GetType().Name}: {ex.Message}\n");
			PlanSummary.Text = "TC-014 서버 fixture 점검 실패 · 증거 경로와 오류를 이벤트 로그에서 확인하세요.";
		}
		finally
		{
			_suiteCancellation.Dispose();
			_suiteCancellation = null;
			Button tc014RelocationSmokeButton = Tc014RelocationSmokeButton;
			javaServer = _javaServer;
			tc014RelocationSmokeButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			Button runEndermanSuiteButton = RunEndermanSuiteButton;
			javaServer = _javaServer;
			runEndermanSuiteButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			Button rerunSuiteAnomaliesButton = RerunSuiteAnomaliesButton;
			javaServer = _javaServer;
			rerunSuiteAnomaliesButton.IsEnabled = javaServer != null && !javaServer.HasExited;
			StopRunButton.IsEnabled = false;
		}
	}

	private async void Tc014ClientTest_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			return;
		}
		OwnedJavaServerProcess javaServer = _javaServer;
		if (javaServer != null && !javaServer.HasExited)
		{
			OwnedJavaClientProcess javaClient = _javaClient;
			if (javaClient != null && !javaClient.HasExited && _javaLatencyProxy == null && (object)_javaWorkspace != null && _suiteCancellation == null && _runCancellation == null)
			{
				_suiteCancellation = new CancellationTokenSource();
				CancellationToken token = _suiteCancellation.Token;
				Tc014ClientTestButton.IsEnabled = false;
				Tc060ClientTestButton.IsEnabled = false;
				RunEndermanSuiteButton.IsEnabled = false;
				RerunSuiteAnomaliesButton.IsEnabled = false;
				StopRunButton.IsEnabled = true;
				PlanSummary.Text = "TC-014 실행 중 · 기존 qa-world와 실행 중인 클라이언트를 재사용합니다. 공격은 한 번만 보내고 서버 결과를 확인합니다.";
				try
				{
					SuiteCaseReceipt receipt = await Tc014ClientStalePositionRunner.RunAsync(_javaWorkspace, javaServer, javaClient, Path.Combine(_appDataRoot, "instances"), _dataRoot, token);
					await SuiteResultsContent.RefreshAsync(CancellationToken.None);
					EventLog.AppendText($"TC-014 CLIENT SUITE · {receipt.Status}/{receipt.Verdict} · repeat={receipt.Repeatability} · capture={receipt.CaptureStatus}\nEvidence: {receipt.JournalPath}\n");
					PlanSummary.Text = $"TC-014 클라이언트 테스트 · {receipt.Status} / {receipt.Verdict}\n{receipt.Repeatability}\n{receipt.Limitation}\n기록: {receipt.JournalPath}";
					return;
				}
				catch (OperationCanceledException)
				{
					EventLog.AppendText("TC-014 CLIENT SUITE STOPPED · any one-shot attack was reconciled before fixture cleanup; see attempt journal.\n");
					PlanSummary.Text = "TC-014 중단됨 · 실행 증거 폴더의 시도 기록을 확인하세요.";
					return;
				}
				catch (Exception ex2)
				{
					EventLog.AppendText($"TC-014 CLIENT SUITE ERROR · {ex2.GetType().Name}: {ex2.Message}\n");
					PlanSummary.Text = "TC-014 클라이언트 실행 실패 · 세부 오류는 이벤트 로그에서 확인하세요.";
					return;
				}
				finally
				{
					_suiteCancellation.Dispose();
					_suiteCancellation = null;
					Button tc014ClientTestButton = Tc014ClientTestButton;
					int isEnabled;
					OwnedJavaServerProcess javaServer2;
					if (_javaLatencyProxy == null)
					{
						javaServer2 = _javaServer;
						if (javaServer2 != null && !javaServer2.HasExited)
						{
							OwnedJavaClientProcess javaClient2 = _javaClient;
							isEnabled = ((javaClient2 != null && !javaClient2.HasExited) ? 1 : 0);
							goto IL_0452;
						}
					}
					isEnabled = 0;
					goto IL_0452;
					IL_0452:
					tc014ClientTestButton.IsEnabled = (byte)isEnabled != 0;
					Button tc060ClientTestButton = Tc060ClientTestButton;
					OwnedLoopbackLatencyProxy javaLatencyProxy = _javaLatencyProxy;
					int isEnabled2;
					if (javaLatencyProxy != null && javaLatencyProxy.ClientAccepted && javaLatencyProxy.ConnectedToServer)
					{
						javaServer2 = _javaServer;
						if (javaServer2 != null && !javaServer2.HasExited)
						{
							OwnedJavaClientProcess javaClient2 = _javaClient;
							isEnabled2 = ((javaClient2 != null && !javaClient2.HasExited) ? 1 : 0);
							goto IL_04ac;
						}
					}
					isEnabled2 = 0;
					goto IL_04ac;
					IL_04ac:
					tc060ClientTestButton.IsEnabled = (byte)isEnabled2 != 0;
					Button runEndermanSuiteButton = RunEndermanSuiteButton;
					javaServer2 = _javaServer;
					runEndermanSuiteButton.IsEnabled = javaServer2 != null && !javaServer2.HasExited;
					Button rerunSuiteAnomaliesButton = RerunSuiteAnomaliesButton;
					javaServer2 = _javaServer;
					rerunSuiteAnomaliesButton.IsEnabled = javaServer2 != null && !javaServer2.HasExited;
					StopRunButton.IsEnabled = false;
				}
			}
		}
		Tc014ClientTestButton.IsEnabled = false;
		Tc060ClientTestButton.IsEnabled = false;
		PlanSummary.Text = "TC-014는 지연 프록시가 없는 lease-bound 클라이언트와 앱 소유 서버에서만 실행합니다. 지연 프록시 세션은 #14 판정에 섞지 않습니다.";
	}

	private async void Tc060ClientTest_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			return;
		}
		OwnedJavaServerProcess server = _javaServer;
		if (server != null && !server.HasExited)
		{
			OwnedJavaClientProcess client = _javaClient;
			if (client != null && !client.HasExited)
			{
				OwnedLoopbackLatencyProxy proxy = _javaLatencyProxy;
				if (proxy != null && proxy.ClientAccepted && proxy.ConnectedToServer && (object)_javaWorkspace != null && _suiteCancellation == null && _runCancellation == null)
				{
					FabricClientBridgeStatus fabricClientBridgeStatus;
					try
					{
						fabricClientBridgeStatus = await client.GetBridgeStatusAsync();
					}
					catch (Exception ex)
					{
						PlanSummary.Text = "TC-060 시작 안 함 · 클라이언트 준비 상태를 읽지 못했습니다 (" + ex.GetType().Name + "). 실행 기록이나 게임 입력은 만들지 않았습니다.";
						EventLog.AppendText("TC-060 PREFLIGHT STOP · client equipment status unavailable (" + ex.GetType().Name + "); no suite receipt or game input was created.\n");
						return;
					}
					if (fabricClientBridgeStatus.MainHandItem != "minecraft:bow" || !fabricClientBridgeStatus.HasNormalArrowAmmo)
					{
						string text = ((fabricClientBridgeStatus.MainHandItem != "minecraft:bow" && !fabricClientBridgeStatus.HasNormalArrowAmmo) ? "일반 활을 들고 일반 화살을 인벤토리에 준비하세요." : ((fabricClientBridgeStatus.MainHandItem != "minecraft:bow") ? "일반 활을 주 손에 들어주세요." : "일반 화살을 인벤토리에 준비하세요."));
						PlanSummary.Text = "TC-060 준비 안 됨 · " + text + "\n준비 확인 전에는 시도 기록·월드 변경·클라이언트 입력을 만들지 않았습니다.";
						EventLog.AppendText($"TC-060 PREFLIGHT STOP · mainHand={fabricClientBridgeStatus.MainHandItem}; normalArrowAmmo={fabricClientBridgeStatus.HasNormalArrowAmmo}; no suite receipt, world mutation, or client input.\n");
						return;
					}
					_suiteCancellation = new CancellationTokenSource();
					CancellationToken token = _suiteCancellation.Token;
					Tc060ClientTestButton.IsEnabled = false;
					Tc014ClientTestButton.IsEnabled = false;
					RunEndermanSuiteButton.IsEnabled = false;
					RerunSuiteAnomaliesButton.IsEnabled = false;
					StopRunButton.IsEnabled = true;
					PlanSummary.Text = "TC-060 실행 중 · 기존 qa-world/클라이언트/지연 연결을 재사용합니다. 최대 3발을 순차 관측하며 불확실한 입력은 재전송하지 않습니다.";
					EventLog.AppendText("TC-060 START · reusing the already-running owned server/client/proxy and existing qa-world; no new display/client session is started.\n");
					try
					{
						SuiteCaseReceipt receipt = await Tc060LatencyProjectileRunner.RunAsync(_javaWorkspace, server, client, proxy, Path.Combine(_appDataRoot, "instances"), _dataRoot, token);
						await SuiteResultsContent.RefreshAsync(CancellationToken.None);
						EventLog.AppendText($"TC-060 CLIENT SUITE · {receipt.Status}/{receipt.Verdict} · coverage={receipt.Coverage} · capture={receipt.CaptureStatus}\nEvidence: {receipt.JournalPath}\n");
						PlanSummary.Text = $"TC-060 클라이언트 테스트 · {receipt.Status} / {receipt.Verdict}\n{receipt.Signature}\n{receipt.Limitation}\n기록: {receipt.JournalPath}";
						return;
					}
					catch (OperationCanceledException)
					{
						EventLog.AppendText("TC-060 STOPPED · any uncertain bow/observer action was not repeated; reconcile recorded IDs and fixture cleanup.\n");
						PlanSummary.Text = "TC-060 중단됨 · 새 실행 폴더의 시도 기록을 확인하세요.";
						return;
					}
					catch (Exception ex3)
					{
						EventLog.AppendText($"TC-060 NOT STARTED/FAILED · {ex3.GetType().Name}: {ex3.Message}\n");
						PlanSummary.Text = "TC-060 실행을 시작하지 못했습니다. 사전 조건 오류라면 월드와 판정은 변경되지 않았습니다.";
						return;
					}
					finally
					{
						_suiteCancellation.Dispose();
						_suiteCancellation = null;
						Button tc060ClientTestButton = Tc060ClientTestButton;
						OwnedLoopbackLatencyProxy javaLatencyProxy = _javaLatencyProxy;
						int isEnabled;
						OwnedJavaServerProcess javaServer;
						if (javaLatencyProxy != null && javaLatencyProxy.ClientAccepted && javaLatencyProxy.ConnectedToServer)
						{
							javaServer = _javaServer;
							if (javaServer != null && !javaServer.HasExited)
							{
								OwnedJavaClientProcess javaClient = _javaClient;
								isEnabled = ((javaClient != null && !javaClient.HasExited) ? 1 : 0);
								goto IL_0669;
							}
						}
						isEnabled = 0;
						goto IL_0669;
						IL_06ad:
						object tc014ClientTestButton;
						int isEnabled2;
						((UIElement)tc014ClientTestButton).IsEnabled = (byte)isEnabled2 != 0;
						Button runEndermanSuiteButton = RunEndermanSuiteButton;
						javaServer = _javaServer;
						runEndermanSuiteButton.IsEnabled = javaServer != null && !javaServer.HasExited;
						Button rerunSuiteAnomaliesButton = RerunSuiteAnomaliesButton;
						javaServer = _javaServer;
						rerunSuiteAnomaliesButton.IsEnabled = javaServer != null && !javaServer.HasExited;
						StopRunButton.IsEnabled = false;
						goto end_IL_0602;
						IL_0669:
						tc060ClientTestButton.IsEnabled = (byte)isEnabled != 0;
						tc014ClientTestButton = Tc014ClientTestButton;
						if (_javaLatencyProxy == null)
						{
							javaServer = _javaServer;
							if (javaServer != null && !javaServer.HasExited)
							{
								OwnedJavaClientProcess javaClient = _javaClient;
								isEnabled2 = ((javaClient != null && !javaClient.HasExited) ? 1 : 0);
								goto IL_06ad;
							}
						}
						isEnabled2 = 0;
						goto IL_06ad;
						end_IL_0602:;
					}
				}
			}
		}
		Tc060ClientTestButton.IsEnabled = false;
		PlanSummary.Text = "TC-060 사전 조건 불충족 · 동일한 앱 소유 서버/가상 화면 클라이언트와 실제 연결된 지연 프록시가 필요합니다. 게임에 입력은 보내지 않았습니다.";
	}

	private async Task RunJavaHazardScenarioAsync(QaScenario scenario)
	{
		OwnedJavaServerProcess javaServer = _javaServer;
		if (javaServer == null || javaServer.HasExited || _runCancellation != null)
		{
			return;
		}
		IGameAdapter adapter = null;
		try
		{
			_ = 1;
			try
			{
				adapter = await _javaServer.ConnectGameAdapterAsync();
				var (qaPlan, planValidation) = new ManagerAgent().CreatePlan(scenario, adapter.Target, adapter.Capabilities);
				if (!planValidation.IsValid || (object)qaPlan == null)
				{
					throw new InvalidOperationException("Manager rejected the environmental death test: " + string.Join("; ", planValidation.Errors));
				}
				await RunScenarioAsync(scenario, qaPlan, adapter, "built-in isolated " + scenario.Id);
				adapter = null;
			}
			catch (Exception ex)
			{
				EventLog.AppendText("JAVA ENVIRONMENT QA SETUP FAILED · " + ex.Message + "\n");
				MessageBox.Show(ex.Message, "환경 사망 QA 실행 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		finally
		{
			if (adapter != null)
			{
				await adapter.DisposeAsync();
			}
		}
	}

	private async void JavaDeathSmoke_Click(object sender, RoutedEventArgs e)
	{
		if (!IsEndermanSuiteProject())
		{
			return;
		}
		OwnedJavaServerProcess javaServer = _javaServer;
		if (javaServer == null || javaServer.HasExited || _runCancellation != null)
		{
			return;
		}
		IGameAdapter adapter = null;
		try
		{
			_ = 1;
			try
			{
				adapter = await _javaServer.ConnectGameAdapterAsync();
				QaScenario scenario = new QaScenario("enderman.controlled-death.live-smoke", 1, "격리 Enderman 사망 확인", "서버 생성 개체를 식별해 통제 피해로 사망시키고 서버 이벤트를 확인한다.", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "entity.spawn", "entity.apply_damage", "observation.request" }), new _003C_003Ez__ReadOnlyArray<ScenarioStep>(new ScenarioStep[3]
				{
					new ScenarioStep("spawn", "entity.spawn", new Dictionary<string, string>()),
					new ScenarioStep("kill", "entity.apply_damage", new Dictionary<string, string>()),
					new ScenarioStep("verify", "observation.request", new Dictionary<string, string>())
				}), 45);
				var (qaPlan, planValidation) = new ManagerAgent().CreatePlan(scenario, adapter.Target, adapter.Capabilities);
				if (!planValidation.IsValid || (object)qaPlan == null)
				{
					throw new InvalidOperationException("Manager rejected the built-in Java smoke scenario: " + string.Join("; ", planValidation.Errors));
				}
				await RunScenarioAsync(scenario, qaPlan, adapter, "built-in bounded Java death smoke");
				adapter = null;
			}
			catch (Exception ex)
			{
				EventLog.AppendText("JAVA QA SETUP FAILED · " + ex.Message + "\n");
				MessageBox.Show(ex.Message, "Java QA 실행 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		finally
		{
			if (adapter != null)
			{
				await adapter.DisposeAsync();
			}
		}
	}

	private QaExportRow[] CurrentExportRows()
	{
		ManualQaRecordStore store = new ManualQaRecordStore(_dataRoot);
		return (from record in store.LoadAll()
			select new QaExportRow(record.CaseId, record.Title, record.Target, record.GameVersion, record.Expected, record.Actual, record.Verdict, string.Join(Environment.NewLine, record.Evidence.Select((ManualEvidence file) => store.ResolveEvidencePath(record.CaseId, file))), record.CaseId)).ToArray();
	}

	private async void SaveManualRecord_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			ManualQaRecordStore store = new ManualQaRecordStore(_dataRoot);
			string verdict = (VerdictPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Not Run";
			ManualQaRecord record = new ManualQaRecord(_caseId, CaseTitle.Text.Trim(), CaseTarget.Text.Trim(), GameVersion.Text.Trim(), Preconditions.Text.Trim(), ManualSteps.Text.Trim(), ExpectedResult.Text.Trim(), ActualResult.Text.Trim(), verdict, ManualNotes.Text.Trim(), _caseStartedAt, DateTimeOffset.UtcNow, _manualEvidence.ToArray());
			await store.SaveAsync(record);
			store.ClearDraft();
			_draftDirty = false;
			EventLog.AppendText($"MANUAL QA SAVED · {record.CaseId.Substring(0, 8)} · {record.Verdict} · evidence {record.Evidence.Count}\n{Path.Combine(_dataRoot, "manual-cases", record.CaseId + ".json")}\n");
			TargetStatus.Text = $"수동 기록 저장됨 · {store.LoadAll().Count}개 케이스";
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "QA 기록 저장 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async void AttachScreenshot_Click(object sender, RoutedEventArgs e)
	{
		await AttachEvidenceAsync("Screenshots (*.png;*.jpg;*.jpeg;*.webp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.gif");
	}

	private async void AttachVideo_Click(object sender, RoutedEventArgs e)
	{
		await AttachEvidenceAsync("Videos (*.mp4;*.mkv;*.webm;*.mov)|*.mp4;*.mkv;*.webm;*.mov");
	}

	private async void AttachOther_Click(object sender, RoutedEventArgs e)
	{
		await AttachEvidenceAsync("Logs and evidence (*.txt;*.json;*.log)|*.txt;*.json;*.log");
	}

	private async Task AttachEvidenceAsync(string filter)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = filter,
			Multiselect = true,
			Title = "이 QA 케이스에 첨부할 증거 선택"
		};
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "screenshots");
		if (Directory.Exists(text))
		{
			openFileDialog.InitialDirectory = text;
		}
		if (!openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			return;
		}
		ManualQaRecordStore store = new ManualQaRecordStore(_dataRoot);
		string[] fileNames = openFileDialog.FileNames;
		foreach (string file in fileNames)
		{
			try
			{
				ManualEvidence manualEvidence = await store.AddEvidenceAsync(_caseId, file);
				_manualEvidence.Add(manualEvidence);
				_draftDirty = true;
				EvidenceList.Items.Add($"{manualEvidence.FileName} · {manualEvidence.SizeBytes / 1024:N0} KB · SHA {manualEvidence.Sha256.Substring(0, 12)}");
				EvidenceList.SelectedIndex = EvidenceList.Items.Count - 1;
				EventLog.AppendText($"EVIDENCE ATTACHED · {manualEvidence.MediaType} · {manualEvidence.RelativePath}\n");
			}
			catch (Exception ex)
			{
				MessageBox.Show(Path.GetFileName(file) + "\n" + ex.Message, "증거 첨부 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
	}

	private async void ManualEvidenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		int selectedIndex = EvidenceList.SelectedIndex;
		if (selectedIndex < 0 || selectedIndex >= _manualEvidence.Count || string.IsNullOrWhiteSpace(_dataRoot))
		{
			ManualEvidencePreview.ClearPreview();
			return;
		}
		EvidencePreviewTab.IsSelected = true;
		ManualEvidence manualEvidence = _manualEvidence[selectedIndex];
		try
		{
			string path = new ManualQaRecordStore(_dataRoot).ResolveEvidencePath(_caseId, manualEvidence);
			await ManualEvidencePreview.ShowEvidenceAsync(path, manualEvidence.MediaType, manualEvidence.Sha256);
		}
		catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException))
		{
			ManualEvidencePreview.ShowUnavailable("증거 파일을 열지 못했습니다 · " + ex.Message);
		}
	}

	private void NewManualRecord_Click(object sender, RoutedEventArgs e)
	{
		if (!HasUnsavedDraft() || MessageBox.Show("현재 미저장 입력은 자동 초안으로 보관됩니다. 새 기록으로 이동할까요?", "초안 보관", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			_caseId = Guid.NewGuid().ToString("N");
			_caseStartedAt = DateTimeOffset.UtcNow;
			_manualEvidence.Clear();
			EvidenceList.Items.Clear();
			ManualEvidencePreview.ClearPreview();
			_pendingAiScenario = null;
			_pendingAiPlan = null;
			RunAiPlanButton.IsEnabled = false;
			CaseTitle.Clear();
			Preconditions.Clear();
			ManualSteps.Clear();
			ActualResult.Clear();
			ManualNotes.Clear();
			VerdictPicker.SelectedIndex = 0;
			CaseTarget.Text = "Minecraft Java · 테스트 월드";
			GameVersion.Text = "기록 필요";
			ExpectedResult.Clear();
			_draftDirty = true;
			CaseTitle.Focus();
			TargetStatus.Text = "새 수동 QA 기록 · 아직 저장되지 않음";
		}
	}

	private bool HasUnsavedDraft()
	{
		if (string.IsNullOrWhiteSpace(CaseTitle.Text) && string.IsNullOrWhiteSpace(ManualSteps.Text) && string.IsNullOrWhiteSpace(ActualResult.Text))
		{
			return _manualEvidence.Count > 0;
		}
		return true;
	}

	private ManualQaRecord CaptureCurrentDraft()
	{
		string verdict = (VerdictPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Not Run";
		return new ManualQaRecord(_caseId, CaseTitle.Text.Trim(), CaseTarget.Text.Trim(), GameVersion.Text.Trim(), Preconditions.Text.Trim(), ManualSteps.Text.Trim(), ExpectedResult.Text.Trim(), ActualResult.Text.Trim(), verdict, ManualNotes.Text.Trim(), _caseStartedAt, DateTimeOffset.UtcNow, _manualEvidence.ToArray());
	}

	private async Task PersistDraftAsync()
	{
		if (!_draftDirty)
		{
			return;
		}
		try
		{
			await new ManualQaRecordStore(_dataRoot).SaveDraftAsync(CaptureCurrentDraft());
			_draftDirty = false;
		}
		catch (IOException ex)
		{
			EventLog.AppendText("DRAFT SAVE WARNING · " + ex.Message + "\n");
		}
	}

	private async void MainWindow_Closing(object? sender, CancelEventArgs e)
	{
		if (_closeAfterSafeSave)
		{
			return;
		}
		e.Cancel = true;
		if (_closeSavePending)
		{
			return;
		}
		if (_aiRequestBusy || _jiraSubmissionBusy || _suiteCancellation != null || _runCancellation != null)
		{
			EventLog.AppendText("CLOSE DEFERRED · an AI/Jira/QA operation is active; stop or let it checkpoint before updating the app.\n");
			return;
		}
		_closeSavePending = true;
		try
		{
			PersistProjectGameContext();
			await new ManualQaRecordStore(_dataRoot).SaveDraftAsync(CaptureCurrentDraft());
			_draftDirty = false;
			PersistToolConversationDraft();
			_closeAfterSafeSave = true;
			Close();
		}
		catch (Exception ex)
		{
			EventLog.AppendText("CLOSE DEFERRED · project context or manual draft save failed (" + ex.GetType().Name + "); current window and session remain open.\n");
		}
		finally
		{
			_closeSavePending = false;
		}
	}

	private void RestoreDraft()
	{
		ManualQaRecord manualQaRecord = new ManualQaRecordStore(_dataRoot).LoadDraft();
		if ((object)manualQaRecord != null)
		{
			ApplyRecord(manualQaRecord);
			_draftDirty = false;
			TargetStatus.Text = "이전 미저장 초안을 복원했습니다.";
			EventLog.AppendText("DRAFT RESTORED · local autosaved draft loaded\n");
		}
	}

	private void ApplyRecord(ManualQaRecord record)
	{
		ManualQaRecord record2 = record;
		_caseId = record2.CaseId;
		_caseStartedAt = record2.StartedAt;
		_manualEvidence.Clear();
		_manualEvidence.AddRange(record2.Evidence);
		CaseTitle.Text = record2.Title;
		CaseTarget.Text = record2.Target;
		GameVersion.Text = record2.GameVersion;
		Preconditions.Text = record2.Preconditions;
		ManualSteps.Text = record2.Steps;
		ExpectedResult.Text = record2.Expected;
		ActualResult.Text = record2.Actual;
		ManualNotes.Text = record2.Notes;
		VerdictPicker.SelectedIndex = Math.Max(0, VerdictPicker.Items.Cast<ComboBoxItem>().ToList().FindIndex((ComboBoxItem item) => (string)item.Content == record2.Verdict));
		_draftDirty = false;
		EvidenceList.Items.Clear();
		foreach (ManualEvidence item in _manualEvidence)
		{
			EvidenceList.Items.Add($"{item.FileName} · {item.SizeBytes / 1024:N0} KB · SHA {item.Sha256.Substring(0, Math.Min(12, item.Sha256.Length))}");
		}
		if (EvidenceList.Items.Count > 0)
		{
			EvidenceList.SelectedIndex = 0;
		}
	}

	private void LoadManualRecord_Click(object sender, RoutedEventArgs e)
	{
		IReadOnlyList<ManualQaRecord> records = new ManualQaRecordStore(_dataRoot).LoadAll();
		if (records.Count == 0)
		{
			MessageBox.Show("저장된 QA 기록이 없습니다.", "기록 불러오기", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		Window dialog = new Window
		{
			Title = "QA 기록 선택",
			Owner = this,
			Width = 720.0,
			Height = 420.0,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = Brushes.White
		};
		ListBox list = new ListBox
		{
			Margin = new Thickness(12.0),
			ItemsSource = records.Select((ManualQaRecord r) => $"{r.UpdatedAt.LocalDateTime:g} · {r.Verdict} · {r.Title} · {r.CaseId}").ToArray()
		};
		Button button = new Button
		{
			Content = "선택 기록 열기",
			Margin = new Thickness(12.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0)
		};
		button.Click += delegate
		{
			if (list.SelectedIndex >= 0)
			{
				ApplyRecord(records[list.SelectedIndex]);
				dialog.DialogResult = true;
				dialog.Close();
			}
		};
		DockPanel dockPanel = new DockPanel();
		DockPanel.SetDock(button, Dock.Bottom);
		dockPanel.Children.Add(button);
		dockPanel.Children.Add(list);
		dialog.Content = dockPanel;
		if (dialog.ShowDialog().GetValueOrDefault())
		{
			TargetStatus.Text = "저장된 QA 기록을 불러왔습니다.";
			EventLog.AppendText("RECORD LOADED · " + _caseId + "\n");
		}
	}

	private async void AiDraft_Click(object sender, RoutedEventArgs e)
	{
		AiAccountRouterClient client = new AiAccountRouterClient(new HttpClient());
		RouterConnectionStatus routerConnectionStatus = await client.ReadStatusAsync();
		TargetStatus.Text = routerConnectionStatus.Message;
		EventLog.AppendText($"AI ROUTER · reachable={routerConnectionStatus.Connected} inferenceReady={routerConnectionStatus.InferenceAvailable} profiles={routerConnectionStatus.RedactedProfiles.Count}\n");
		if (!routerConnectionStatus.InferenceAvailable)
		{
			MessageBox.Show(routerConnectionStatus.Message + "\n라우터를 자동 시작하거나 로그인하지 않았습니다.", "AI 초안 사용 불가", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		try
		{
			AiScenarioDraft aiScenarioDraft = await client.DraftScenarioAsync(CaseTitle.Text.Trim(), CaseTarget.Text.Trim(), GameVersion.Text.Trim());
			Preconditions.Text = aiScenarioDraft.Preconditions;
			ManualSteps.Text = string.Join(Environment.NewLine, aiScenarioDraft.Steps.Select((string step, int index) => $"{index + 1}. {step}"));
			ExpectedResult.Text = aiScenarioDraft.Expected;
			VerdictPicker.SelectedIndex = 0;
			EventLog.AppendText($"AI DRAFT RECEIVED · model {aiScenarioDraft.ProviderModel} · {aiScenarioDraft.Steps.Count} suggested steps · not executed; review before use\n");
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException || ex is JsonException) ? 1 : 0) != 0)
		{
			EventLog.AppendText("AI DRAFT FAILED · " + ex.Message + "\n");
			MessageBox.Show(ex.Message, "AI 초안 요청 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async void PreviewPlan_Click(object sender, RoutedEventArgs e)
	{
		if (_runCancellation == null && !_aiRequestBusy)
		{
			QaScenario scenario = EndermanDeathScenarios.ControlledFall();
			SyntheticGameAdapter syntheticGameAdapter = new SyntheticGameAdapter();
			var (qaPlan, planValidation) = new ManagerAgent().CreatePlan(scenario, syntheticGameAdapter.Target, syntheticGameAdapter.Capabilities);
			if (!planValidation.IsValid || (object)qaPlan == null)
			{
				EventLog.AppendText("PLAN REJECTED: " + string.Join("; ", planValidation.Errors) + Environment.NewLine);
				await syntheticGameAdapter.DisposeAsync();
			}
			else
			{
				await RunScenarioAsync(scenario, qaPlan, syntheticGameAdapter, "built-in synthetic scenario");
			}
		}
	}

	private async void AiPlan_Click(object sender, RoutedEventArgs e)
	{
		if (_runCancellation != null || _aiRequestBusy)
		{
			return;
		}
		_aiRequestBusy = true;
		_pendingAiScenario = null;
		_pendingAiPlan = null;
		RunAiPlanButton.IsEnabled = false;
		string request = AiChatInput.Text.Trim();
		if (string.IsNullOrWhiteSpace(request))
		{
			request = CaseTitle.Text.Trim();
		}
		if (string.IsNullOrWhiteSpace(request))
		{
			PlanSummary.Text = "테스트할 내용을 먼저 입력하세요.";
			return;
		}
		IGameAdapter adapter = null;
		AiAccountRouterClient client = new AiAccountRouterClient(new HttpClient());
		try
		{
			_ = 4;
			try
			{
				RouterConnectionStatus routerConnectionStatus = await client.ReadStatusAsync();
				TargetStatus.Text = routerConnectionStatus.Message;
				EventLog.AppendText($"AI PLAN · router reachable={routerConnectionStatus.Connected}; inference ready={routerConnectionStatus.InferenceAvailable}\n");
				if (!routerConnectionStatus.InferenceAvailable)
				{
					MessageBox.Show(routerConnectionStatus.Message + "\nAI 요청을 보내지 않았습니다.", "AI 계획 사용 불가", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					return;
				}
				OwnedJavaServerProcess javaServer = _javaServer;
				IGameAdapter gameAdapter = ((javaServer == null || javaServer.HasExited || (object)_javaWorkspace == null) ? ((IGameAdapter)new SyntheticGameAdapter()) : ((IGameAdapter)(await _javaServer.ConnectGameAdapterAsync())));
				adapter = gameAdapter;
				string text = string.Join("\n", AiChatHistory.Items.Cast<string>().TakeLast(12));
				string objective = "이전 대화 문맥과 선택한 QA 증거를 참고하되 최신 요청을 우선하세요. 대화:\n" + text + "\n선택 QA 증거:\n" + BuildSelectedEvidenceContext() + "\n최신 요청: " + request;
				AiExecutableScenarioDraft draft = await client.DraftExecutableScenarioAsync(objective, adapter.Target, adapter.Capabilities);
				var (plan, planValidation) = new ManagerAgent().CreatePlan(draft.Scenario, adapter.Target, adapter.Capabilities);
				if (!planValidation.IsValid || (object)plan == null)
				{
					PlanSummary.Text = "AI 제안 거부됨 · " + string.Join("; ", planValidation.Errors);
					RunAiPlanButton.IsEnabled = false;
					_pendingAiScenario = null;
					_pendingAiPlan = null;
					return;
				}
				_pendingAiScenario = draft.Scenario;
				_pendingAiPlan = plan;
				_pendingAiUsesJava = adapter is FabricJavaGameAdapter;
				RunAiPlanButton.IsEnabled = true;
				await _aiConversationStore.AppendAsync("user", request);
				await _aiConversationStore.AppendAsync("assistant", $"매크로 초안: {draft.Scenario.Title} · {draft.Scenario.Steps.Count}개 단계. 검토 후 실행 버튼이 필요합니다.", draft.ProviderModel);
				_aiConversation.Add(new SavedAiQaConversationTurn("user", request, null, DateTimeOffset.UtcNow));
				_aiConversation.Add(new SavedAiQaConversationTurn("assistant", $"매크로 초안: {draft.Scenario.Title} · {draft.Scenario.Steps.Count}개 단계. 검토 후 실행 버튼이 필요합니다.", draft.ProviderModel, DateTimeOffset.UtcNow));
				RenderAiConversation();
				AiChatInput.Clear();
				PlanSummary.Text = $"AI 제안 · 사람이 실행을 눌러야 시작합니다.\n{draft.Scenario.Title}\n{draft.Scenario.Steps.Count} 단계 · {draft.Scenario.TimeoutSeconds}s · {draft.ProviderModel}\n{string.Join(" → ", draft.Scenario.Steps.Select((ScenarioStep x) => x.Action))}\nManager 검증 통과 · 대상: {adapter.Target.Ownership} · {adapter.Target.TargetId}";
				EventLog.AppendText($"AI PLAN READY FOR REVIEW · {draft.Scenario.Id} · hash {plan.PlanHash.Substring(0, 12)} · not executed\n");
			}
			catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException || ex is JsonException || ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				EventLog.AppendText("AI PLAN FAILED · " + ex.Message + "\n");
				MessageBox.Show(ex.Message, "AI 계획 생성 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		finally
		{
			if (adapter != null)
			{
				await adapter.DisposeAsync();
			}
			_aiRequestBusy = false;
		}
	}

	private async void RunAiPlan_Click(object sender, RoutedEventArgs e)
	{
		if ((object)_pendingAiPlan == null || (object)_pendingAiScenario == null || _runCancellation != null)
		{
			return;
		}
		QaPlan plan = _pendingAiPlan;
		QaScenario scenario = _pendingAiScenario;
		bool useJava = _pendingAiUsesJava;
		_pendingAiPlan = null;
		_pendingAiScenario = null;
		RunAiPlanButton.IsEnabled = false;
		try
		{
			IGameAdapter gameAdapter;
			if (useJava)
			{
				OwnedJavaServerProcess javaServer = _javaServer;
				if (javaServer != null && !javaServer.HasExited)
				{
					gameAdapter = await _javaServer.ConnectGameAdapterAsync();
					goto IL_010e;
				}
			}
			gameAdapter = new SyntheticGameAdapter();
			goto IL_010e;
			IL_010e:
			IGameAdapter adapter = gameAdapter;
			await RunScenarioAsync(scenario, plan, adapter, useJava ? "AI-proposed Java server plan" : "AI-proposed synthetic plan");
		}
		catch (Exception ex)
		{
			EventLog.AppendText("AI RUN SETUP FAILED · " + ex.Message + "\n");
			MessageBox.Show(ex.Message, "실행 연결 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task RunScenarioAsync(QaScenario scenario, QaPlan plan, IGameAdapter adapter, string origin)
	{
		string sourceKind = (adapter.Capabilities.AuthoritativeEntityState ? "Minecraft Java authoritative telemetry" : "Synthetic mock");
		TargetStatus.Text = (adapter.Capabilities.AuthoritativeEntityState ? ("격리 Java 서버 · " + adapter.Target.GameVersion + " · " + adapter.Target.TargetId) : "Synthetic 어댑터 · 모의 대상 (Minecraft 연결 아님)");
		PlanSummary.Text = $"검증 통과 · {plan.Steps.Count}개 허용 단계 · plan {plan.PlanHash.Substring(0, 12)}\n대상: {plan.TargetId}\n계획 출처: {origin}\n증거: {sourceKind}";
		string runId = Guid.NewGuid().ToString("N");
		string journalPath = Path.Combine(_dataRoot, "runs", runId + ".jsonl");
		RunJournal journal = new RunJournal(journalPath);
		_runCancellation = new CancellationTokenSource();
		_activeAdapter = adapter;
		RunPlanButton.IsEnabled = false;
		StopRunButton.IsEnabled = true;
		try
		{
			_ = 6;
			try
			{
				string provenance = (adapter.Capabilities.AuthoritativeEntityState ? "minecraft-java-authoritative" : "synthetic-only");
				await journal.AppendAsync(new
				{
					kind = "plan",
					runId = runId,
					target = adapter.Target,
					scenario = scenario,
					plan = plan,
					recordedAt = DateTimeOffset.UtcNow,
					provenance = provenance
				});
				EventLog.AppendText($"PLAN VALIDATED · run {runId.Substring(0, 8)} · {plan.Steps.Count} bounded steps · {origin}\n");
				ScenarioRunResult result = await new ScenarioRunner().RunAsync(runId, plan, adapter, _runCancellation.Token);
				foreach (AdapterActionResult action in result.Actions)
				{
					await journal.AppendAsync(new
					{
						kind = "adapter-action",
						runId = runId,
						action = action
					});
					EventLog.AppendText(action.StepId + " · " + action.Detail + "\n");
				}
				if ((object)result.Before != null)
				{
					await journal.AppendAsync(new
					{
						kind = "entity-snapshot",
						runId = runId,
						snapshot = result.Before
					});
				}
				if ((object)result.After != null)
				{
					await journal.AppendAsync(new
					{
						kind = "entity-snapshot",
						runId = runId,
						snapshot = result.After
					});
				}
				foreach (QaEvent item in result.Actions.SelectMany((AdapterActionResult x) => x.Events))
				{
					await journal.AppendAsync(new
					{
						kind = "game-event",
						runId = runId,
						qaEvent = item
					});
				}
				await journal.AppendAsync(new
				{
					kind = "verdict",
					runId = runId,
					Status = result.Status,
					Verdict = result.Verdict,
					Limitation = result.Limitation,
					provenance = provenance,
					journalPath = journalPath
				});
				PlanSummary.Text = $"QA 런 종료 · {result.Status}\n판정: {result.Verdict}\n개체: {result.Before?.RunEntityId ?? "unknown"}\n상태: {result.Before?.LifeState.ToString() ?? "unknown"} → {result.After?.LifeState.ToString() ?? "unknown"}\n좌표: {FormatPosition(result.Before?.Position)} → {FormatPosition(result.After?.Position)}\n캡처: unavailable (실제 게임 픽셀 아님)\n{result.Limitation}";
				EventLog.AppendText($"{sourceKind.ToUpperInvariant()} RESULT · {result.Status} / {result.Verdict} · {provenance}\nJournal: {journalPath}\n");
			}
			catch (Exception ex)
			{
				EventLog.AppendText("RUN ERROR · " + ex.Message + "\n");
			}
		}
		finally
		{
			await adapter.DisposeAsync();
			_activeAdapter = null;
			_runCancellation.Dispose();
			_runCancellation = null;
			RunPlanButton.IsEnabled = true;
			StopRunButton.IsEnabled = false;
		}
	}

	private static string FormatPosition(WorldPositionSnapshot? position)
	{
		if ((object)position != null)
		{
			return $"{position.X:F1}, {position.Y:F1}, {position.Z:F1} ({position.Dimension})";
		}
		return "unknown";
	}

	private async void StopRun_Click(object sender, RoutedEventArgs e)
	{
		if (_suiteCancellation != null)
		{
			_suiteCancellation.Cancel();
			EventLog.AppendText("SUITE STOP REQUESTED · current case will checkpoint and the same server will remain running.\n");
			return;
		}
		CancellationTokenSource runCancellation = _runCancellation;
		if (runCancellation != null)
		{
			runCancellation.Cancel();
			if (_activeAdapter != null)
			{
				await _activeAdapter.StopAsync(1L, CancellationToken.None);
			}
			StopRunButton.IsEnabled = false;
			EventLog.AppendText("STOP REQUESTED · queued synthetic steps cancelled; no game process was targeted\n");
		}
	}

	private void OpenAiSetup_Click(object sender, RoutedEventArgs e)
	{
		if (_aiRouterSetupWindow != null)
		{
			_aiRouterSetupWindow.Activate();
			return;
		}
		_aiRouterSetupWindow = new AiRouterSetupWindow
		{
			Owner = this
		};
		_aiRouterSetupWindow.Closed += delegate
		{
			_aiRouterSetupWindow = null;
		};
		_aiRouterSetupWindow.Show();
	}

	private async void RouterStatus_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			RouterConnectionStatus routerConnectionStatus = await new AiAccountRouterClient(new HttpClient()).ReadStatusAsync();
			TargetStatus.Text = routerConnectionStatus.Message;
			EventLog.AppendText($"ROUTER · {routerConnectionStatus.Message} Inference ready: {routerConnectionStatus.InferenceAvailable}; profiles: {routerConnectionStatus.RedactedProfiles.Count}\n");
		}
		catch (Exception ex)
		{
			TargetStatus.Text = "Router 상태를 안전하게 판별하지 못했습니다.";
			EventLog.AppendText($"ROUTER STATUS ERROR · {ex.GetType().Name}: {ex.Message}\n");
		}
	}

	private async void ObserveFabric_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Enderman QA bridge descriptor (endermanqa-bridge.json)|endermanqa-bridge.json",
			FileName = "endermanqa-bridge.json",
			InitialDirectory = Path.Combine(_dataRoot, "runs")
		};
		if (!openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			return;
		}
		try
		{
			using FabricBridgeClient bridge = FabricBridgeClient.FromDescriptor(openFileDialog.FileName);
			TargetStatus.Text = "Fabric bridge에 읽기 전용 연결 중…";
			FabricObservationSnapshot snapshot = await bridge.ObserveAsync();
			string text = Path.Combine(_dataRoot, "bridge-observations");
			Directory.CreateDirectory(text);
			string value = Regex.Replace(snapshot.Status.TargetId, "[^A-Za-z0-9_-]", "_");
			string reportPath = Path.Combine(text, $"{value}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json");
			JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true
			};
			await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(snapshot, options));
			TargetStatus.Text = $"Java 서버 관측 저장됨 · {snapshot.Status.GameVersion} · Enderman {snapshot.Endermen.Count} · death events {snapshot.Deaths.Count} · observe-only";
			EventLog.AppendText($"FABRIC OBSERVATION · target {snapshot.Status.TargetId} · ownership {snapshot.Status.Ownership} · Endermen {snapshot.Endermen.Count} · deaths {snapshot.Deaths.Count}\nReport: {reportPath}\n{snapshot.Limitation}\n");
		}
		catch (Exception ex) when (((ex is IOException || ex is HttpRequestException || ex is TaskCanceledException || ex is InvalidOperationException || ex is JsonException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			TargetStatus.Text = "Fabric bridge 관측 실패 · 게임 상태는 변경하지 않았습니다.";
			EventLog.AppendText($"FABRIC OBSERVATION FAILED · {ex.GetType().Name}: {ex.Message}\n");
			MessageBox.Show(ex.Message, "Java 관측 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async void ManageProject_Click(object sender, RoutedEventArgs e)
	{
		OwnedJavaServerProcess javaServer = _javaServer;
		if ((javaServer != null && !javaServer.HasExited) || _suiteCancellation != null || _runCancellation != null || _aiRequestBusy || _jiraSubmissionBusy || GameMacroWorkbenchContent.IsRunning)
		{
			MessageBox.Show("프로젝트를 바꾸기 전에 현재 QA/AI/Jira 작업과 앱 소유 서버를 마무리하세요. 기록과 작업 중 데이터가 섞이지 않도록 전환을 막았습니다.", "프로젝트 전환 보류", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		try
		{
			PersistProjectGameContext();
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			MessageBox.Show("게임 버전 메모를 저장하지 못해 프로젝트를 바꾸지 않았습니다.\n" + ex.Message, "프로젝트 전환 보류", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		PersistToolConversationDraft();
		await PersistDraftAsync();
		string caseCatalogSource = Path.Combine(AppContext.BaseDirectory, "Scenarios", "enderman-death-suite-100.json");
		QaProjectCatalog qaProjectCatalog = new QaProjectCatalog(_appDataRoot);
		string activeId = qaProjectCatalog.GetActiveProject()?.ProjectId;
		QaProjectDefinition selected = QaProjectDialog.Show(this, qaProjectCatalog, caseCatalogSource, activeId);
		if ((object)selected != null)
		{
			string dataRoot = _dataRoot;
			_projectRoot = selected.RootPath;
			_dataRoot = selected.DataRoot;
			GameMacroWorkbenchContent.SetProjectRoot(_dataRoot, selected.GameTarget, QaProjectCatalog.GetProjectEngineId(selected));
			RestorePreparedTargetForProject(selected);
			RestoreProjectGameContext(selected);
			RefreshCurrentProjectLabel();
			_workspaceShell?.UpdateProjectName(selected.Name);
			_lastAiPrompt = null;
			_lastAiResponse = null;
			_lastAiModel = null;
			_selectedArtifact = null;
			_lastToolPrompt = null;
			_lastToolResponse = null;
			_lastToolModel = null;
			_aiConversationStore = new AiQaConversationStore(_dataRoot);
			_toolConversationStore = CreateToolConversationStore(_dataRoot, _toolProfile);
			_toolWorkspaceStore = new ToolWorkspaceStore(_dataRoot);
			_aiConversation.Clear();
			_toolConversation.Clear();
			if (!string.Equals(dataRoot, _dataRoot, StringComparison.OrdinalIgnoreCase))
			{
				ResetManualEditorForProject(selected.Name);
				RestoreDraft();
			}
			await SuiteResultsContent.LoadProjectAsync(_projectRoot, _dataRoot);
			_homeDashboard?.Update(selected);
			ModuleManagerContent.SetProjectRoot(_dataRoot);
			ModuleManagerContent.SetAppDataRoot(_appDataRoot);
			await RestoreAiConversationAsync();
			await RestoreToolConversationAsync();
			RefreshJiraStatus();
			RefreshJiraStageStatus();
			RefreshProjectArtifacts();
			RefreshToolWorkspaceFiles();
			SetMacroStorageProject(selected);
			EventLog.AppendText($"QA PROJECT OPEN · {selected.Name} · {selected.ProjectId}\nProject files: {selected.RootPath}\nProject data: {selected.DataRoot}\n");
		}
	}

	private void SetMacroStorageProject(QaProjectDefinition? project)
	{
		if ((object)project == null)
		{
			MacroAndStorageContent.SetNoProject();
			UpdateEndermanSuiteAvailability();
		}
		else
		{
			string text = Path.Combine(project.DataRoot, "case-catalog-100.json");
			MacroAndStorageContent.SetProject(project.RootPath, project.DataRoot, File.Exists(text) ? text : null, project.Name);
			UpdateEndermanSuiteAvailability();
		}
	}

	private bool IsEndermanSuiteProject()
	{
		bool num = File.Exists(Path.Combine(_projectRoot, "case-catalog-100.json")) || File.Exists(Path.Combine(_dataRoot, "case-catalog-100.json"));
		string text = (GameTargetPicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
		if (num)
		{
			return text.Contains("Minecraft", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private void UpdateEndermanSuiteAvailability()
	{
		int num;
		if (IsEndermanSuiteProject())
		{
			OwnedJavaServerProcess javaServer = _javaServer;
			num = ((javaServer != null && !javaServer.HasExited) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool flag = (byte)num != 0;
		RunEndermanSuiteButton.IsEnabled = flag && _suiteCancellation == null;
		RerunSuiteAnomaliesButton.IsEnabled = flag && _suiteCancellation == null;
		QaProjectDefinition activeProject = new QaProjectCatalog(_appDataRoot).GetActiveProject();
		if ((object)activeProject != null && !IsEndermanSuiteProject())
		{
			PlanSummary.Text = activeProject.GameTarget + " 프로젝트입니다. Enderman 전용 실행은 비활성화되어 있습니다. 등록된 게임별 어댑터가 있으면 ‘도구 제작 → 게임별 매크로 작업대’에서 선택하세요.";
		}
	}

	private void RefreshJiraStatus()
	{
		QaProjectDefinition activeProject = new QaProjectCatalog(_appDataRoot).GetActiveProject();
		JiraConnectionSettings jiraConnectionSettings = new JiraConnectionStore(_appDataRoot, _dataRoot).Load();
		object obj;
		if ((object)jiraConnectionSettings != null)
		{
			obj = (string.IsNullOrWhiteSpace(jiraConnectionSettings.ProjectKey) ? "Jira 계정 보호 저장됨 · 프로젝트 선택 필요" : ("Jira 목적지 " + jiraConnectionSettings.ProjectKey));
		}
		else
		{
			string text = activeProject?.JiraProjectKey;
			obj = ((text != null && text.Length > 0) ? ("Jira " + text + " · 계정 설정 필요") : "Jira 연결 설정 필요");
		}
		string text2 = (string)obj;
		TargetStatus.Text = "프로젝트: " + (activeProject?.Name ?? "기본 작업공간") + " · " + text2;
		RefreshJiraStageStatus();
	}

	private void ConfigureJira_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			JiraConnectionStore jiraConnectionStore = new JiraConnectionStore(_appDataRoot, _dataRoot);
			JiraConnectionSettings jiraConnectionSettings = JiraConnectionDialog.Show(this, ExistingOrProjectJiraDefaults(jiraConnectionStore));
			if ((object)jiraConnectionSettings != null)
			{
				jiraConnectionStore.Save(jiraConnectionSettings);
				RefreshJiraStatus();
				EventLog.AppendText($"JIRA CONNECTION SAVED · {jiraConnectionSettings.Site} · project {(string.IsNullOrWhiteSpace(jiraConnectionSettings.ProjectKey) ? "not selected" : jiraConnectionSettings.ProjectKey)} · credential protected with Windows DPAPI\n");
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			EventLog.AppendText($"JIRA SETTINGS FAILED · {ex.GetType().Name}: {ex.Message}\n");
			MessageBox.Show(ex.Message, "Jira 설정 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async void CreateJiraIssue_Click(object sender, RoutedEventArgs e)
	{
		if (_jiraSubmissionBusy)
		{
			return;
		}
		ManualQaRecord record = CaptureCurrentDraft();
		if (string.IsNullOrWhiteSpace(record.Title))
		{
			MessageBox.Show("먼저 QA 테스트 제목을 입력하세요.", "Jira 등록", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		string verdict = record.Verdict;
		if ((!(verdict == "Fail") && !(verdict == "Inconclusive")) || 1 == 0)
		{
			MessageBox.Show("Jira 결함/조사 등록은 Fail 또는 Inconclusive 판정에서만 가능합니다. Pass와 Not Run은 이슈로 제출하지 않습니다.", "Jira 제출 대상 확인", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		JiraConnectionSettings settings = GetJiraSettings(promptIfMissing: true);
		if ((object)settings == null)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(settings.ProjectKey))
		{
			MessageBox.Show("Jira 프로젝트가 아직 선택되지 않았습니다. Jira에서 프로젝트를 만든 뒤 ‘Jira 설정’에서 프로젝트 목록을 불러오고 키를 선택하세요.", "Jira 프로젝트 필요", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			if (!Uri.TryCreate(settings.Site, UriKind.Absolute, out Uri site))
			{
				return;
			}
			string text = (string.Equals(record.Verdict, "Fail", StringComparison.OrdinalIgnoreCase) ? settings.IssueType : "Task");
			string[] attachments = ResolveManualEvidence(record);
			string description = $"QA Case: {record.CaseId}\nTarget: {record.Target} {record.GameVersion}\nVerdict: {record.Verdict}\nPreconditions: {record.Preconditions}\nReproduction steps:\n{record.Steps}\nExpected result: {record.Expected}\nActual result: {record.Actual}\nNotes: {record.Notes}\nEvidence files: {string.Join("; ", attachments.Select(Path.GetFileName))}";
			JiraConnectionStore ledger = new JiraConnectionStore(_appDataRoot, _dataRoot);
			string submissionId = JiraQaCandidateService.ManualSubmissionId(settings.ProjectKey, record);
			JiraSubmissionEntry jiraSubmissionEntry = ledger.LoadLedger().FirstOrDefault((JiraSubmissionEntry x) => x.SubmissionId == submissionId);
			if ((object)jiraSubmissionEntry != null)
			{
				MessageBox.Show("이 QA 기록은 이미 Jira에 등록되었습니다: " + jiraSubmissionEntry.IssueKey + "\n중복 이슈 생성을 막았습니다.", "이미 등록됨", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			else
			{
				if (MessageBox.Show($"Jira {text} 이슈를 생성합니다.\n\nProject: {settings.ProjectKey}\nSummary: {record.Title}\n첨부 파일: {attachments.Length}개\n\nQA 설명과 선택된 증거 파일이 Jira로 전송됩니다.", "Jira 이슈 생성 확인", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
				{
					return;
				}
				_jiraSubmissionBusy = true;
				try
				{
					using HttpClient http = new HttpClient(new HttpClientHandler
					{
						AllowAutoRedirect = false
					})
					{
						Timeout = TimeSpan.FromSeconds(30.0)
					};
					JiraCloudClient client = new JiraCloudClient(http);
					string key = await client.CreateIssueAsync(site, settings.Email, settings.ApiToken, settings.ProjectKey, text, record.Title, description);
					ledger.TryRecordSubmission(submissionId, record.CaseId, record.CaseId, key);
					int value = await UploadJiraEvidenceAsync(client, settings, site, key, attachments);
					TargetStatus.Text = $"Jira 이슈 등록 완료 · {key} · 첨부 {value}/{attachments.Length}";
					EventLog.AppendText($"JIRA ISSUE CREATED · {key} · manual case {record.CaseId} · attachments {value}/{attachments.Length}\n");
				}
				catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is ArgumentException || ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
				{
					TargetStatus.Text = "Jira 등록 실패 · QA 기록은 로컬에 유지됨";
					EventLog.AppendText($"JIRA CREATE FAILED · {ex.GetType().Name}: {ex.Message}\n");
					MessageBox.Show(ex.Message, "Jira 등록 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				finally
				{
					_jiraSubmissionBusy = false;
				}
			}
		}
	}

	private async void SubmitSuiteIssues_Click(object sender, RoutedEventArgs e)
	{
		if (_jiraSubmissionBusy)
		{
			return;
		}
		JiraConnectionSettings settings = GetJiraSettings(promptIfMissing: true);
		if ((object)settings == null)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(settings.ProjectKey))
		{
			MessageBox.Show("접근 가능한 Jira 프로젝트가 없습니다. Jira 프로젝트 생성/권한 설정 후 ‘Jira 설정’에서 프로젝트를 선택하세요.", "Jira 프로젝트 필요", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			if (!Uri.TryCreate(settings.Site, UriKind.Absolute, out Uri site))
			{
				return;
			}
			JiraConnectionStore store = new JiraConnectionStore(_appDataRoot, _dataRoot);
			IReadOnlyList<JiraQaCandidate> readOnlyList = new JiraQaCandidateService(_dataRoot).LoadLatestSuiteCandidates(store.LoadLedger());
			if (readOnlyList.Count == 0)
			{
				MessageBox.Show("최근 자동 QA 결과에 새로 제출할 실패 또는 미확정 항목이 없습니다.", "Jira 제출", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}
			int num = readOnlyList.Count((JiraQaCandidate x) => x.SourceLabel.Contains("reproduced-anomaly", StringComparison.Ordinal));
			string value = string.Join("\n", readOnlyList.Select((JiraQaCandidate x) => $"• {x.IssueType} · {(x.SourceLabel.Contains("reproduced-anomaly", StringComparison.Ordinal) ? "재현된 결함 후보" : "조사 필요")} : {x.Title} ({x.Attachments.Count}개 로그)"));
			if (MessageBox.Show($"최근 자동 QA의 미제출 결과 {readOnlyList.Count}건을 Jira에 생성합니다.\n이슈 유형: Task {readOnlyList.Count}건 · 재현된 결함 후보 {num}건 · 조사 필요 {readOnlyList.Count - num}건\n\nVW 프로젝트에는 Bug 유형이 없다고 전달받아 모두 Task로 만들고, 설명에 결함/조사 분류를 보존합니다. 증거 로그도 첨부됩니다.\n\n{value}\n\n계속할까요?", "QA 결과 Jira 제출", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
			{
				return;
			}
			_jiraSubmissionBusy = true;
			int succeeded = 0;
			int failed = 0;
			try
			{
				using HttpClient http = new HttpClient(new HttpClientHandler
				{
					AllowAutoRedirect = false
				})
				{
					Timeout = TimeSpan.FromMinutes(3.0)
				};
				JiraCloudClient client = new JiraCloudClient(http);
				foreach (JiraQaCandidate candidate in readOnlyList)
				{
					try
					{
						string summary = ((candidate.Title.Length > 250) ? candidate.Title.Substring(0, 250) : candidate.Title);
						string key = await client.CreateIssueAsync(site, settings.Email, settings.ApiToken, settings.ProjectKey, candidate.IssueType, summary, candidate.Description);
						string submissionId = settings.ProjectKey + ":" + candidate.SubmissionId;
						store.TryRecordSubmission(submissionId, candidate.CaseId, candidate.RunId, key);
						int value2 = await UploadJiraEvidenceAsync(client, settings, site, key, candidate.Attachments);
						succeeded++;
						EventLog.AppendText($"JIRA SUITE ISSUE CREATED · {key} · {candidate.CaseId} · logs {value2}/{candidate.Attachments.Count}\n");
					}
					catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is ArgumentException || ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
					{
						failed++;
						EventLog.AppendText($"JIRA SUITE ISSUE FAILED · {candidate.CaseId} · {ex.GetType().Name}: {ex.Message}\n");
					}
				}
				TargetStatus.Text = $"Jira QA 제출 완료 · 성공 {succeeded} · 실패 {failed}";
				MessageBox.Show($"Jira 이슈 등록: {succeeded}건 성공, {failed}건 실패. 성공한 항목은 중복 제출 방지 목록에 기록했습니다.", "QA Jira 제출 결과", MessageBoxButton.OK, (failed == 0) ? MessageBoxImage.Asterisk : MessageBoxImage.Exclamation);
			}
			finally
			{
				_jiraSubmissionBusy = false;
			}
		}
	}

	private JiraConnectionSettings? GetJiraSettings(bool promptIfMissing)
	{
		JiraConnectionStore jiraConnectionStore = new JiraConnectionStore(_appDataRoot, _dataRoot);
		JiraConnectionSettings jiraConnectionSettings = jiraConnectionStore.Load();
		if (!promptIfMissing || ((object)jiraConnectionSettings != null && !string.IsNullOrWhiteSpace(jiraConnectionSettings.ProjectKey)))
		{
			return jiraConnectionSettings;
		}
		JiraConnectionSettings jiraConnectionSettings2 = JiraConnectionDialog.Show(this, ExistingOrProjectJiraDefaults(jiraConnectionStore));
		if ((object)jiraConnectionSettings2 == null)
		{
			return null;
		}
		jiraConnectionStore.Save(jiraConnectionSettings2);
		RefreshJiraStatus();
		return jiraConnectionSettings2;
	}

	private JiraConnectionSettings? ExistingOrProjectJiraDefaults(JiraConnectionStore store)
	{
		JiraConnectionSettings jiraConnectionSettings = store.Load();
		if ((object)jiraConnectionSettings != null)
		{
			return jiraConnectionSettings;
		}
		QaProjectDefinition activeProject = new QaProjectCatalog(_appDataRoot).GetActiveProject();
		if ((object)activeProject == null || string.IsNullOrWhiteSpace(activeProject.JiraSite))
		{
			return null;
		}
		IReadOnlyList<string>? jiraIssueTypes = activeProject.JiraIssueTypes;
		string issueType = ((jiraIssueTypes != null && jiraIssueTypes.Contains<string>("Task", StringComparer.OrdinalIgnoreCase)) ? "Task" : (activeProject.JiraIssueTypes?.FirstOrDefault() ?? "Task"));
		return new JiraConnectionSettings(activeProject.JiraSite, string.Empty, activeProject.JiraProjectKey ?? string.Empty, string.Empty, issueType);
	}

	private string[] ResolveManualEvidence(ManualQaRecord record)
	{
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(_dataRoot).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string root = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		return (from x in record.Evidence
			select Path.GetFullPath(Path.Combine(_dataRoot, x.RelativePath)) into path
			where path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
			where new FileInfo(path).Length <= 536870912
			select path).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private async Task<int> UploadJiraEvidenceAsync(JiraCloudClient client, JiraConnectionSettings settings, Uri site, string issueKey, IReadOnlyList<string> paths)
	{
		int uploaded = 0;
		foreach (string path in paths)
		{
			try
			{
				await client.UploadAttachmentAsync(site, settings.Email, settings.ApiToken, issueKey, path);
				uploaded++;
			}
			catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is ArgumentException) ? 1 : 0) != 0)
			{
				EventLog.AppendText($"JIRA ATTACHMENT FAILED · {Path.GetFileName(path)} · {ex.GetType().Name}: {ex.Message}\n");
			}
		}
		return uploaded;
	}

	private void ExportExcel_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "Excel workbook (*.xlsx)|*.xlsx",
			FileName = "Enderman-QA.xlsx"
		};
		if (saveFileDialog.ShowDialog().GetValueOrDefault())
		{
			QaExportRow[] array = CurrentExportRows();
			ExportServices.ExportExcel(saveFileDialog.FileName, array);
			EventLog.AppendText($"XLSX exported: {saveFileDialog.FileName} · {array.Length} records\n");
		}
	}

	private void ExportJira_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "Jira CSV (*.csv)|*.csv",
			FileName = "Enderman-QA-Jira.csv"
		};
		if (saveFileDialog.ShowDialog().GetValueOrDefault())
		{
			QaExportRow[] array = CurrentExportRows();
			ExportServices.ExportJiraCsv(saveFileDialog.FileName, array);
			EventLog.AppendText($"Jira-import CSV exported: {saveFileDialog.FileName} · {array.Length} records\n");
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "mainwindow.baml");
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	internal Delegate _CreateDelegate(Type delegateType, string handler)
	{
		return Delegate.CreateDelegate(delegateType, this, handler);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			CurrentProjectName = (TextBlock)target;
			break;
		case 2:
			((Button)target).Click += ManageProject_Click;
			break;
		case 3:
			MainStages = (TabControl)target;
			break;
		case 4:
			StageProjectName = (TextBlock)target;
			break;
		case 5:
			((Button)target).Click += ManageProject_Click;
			break;
		case 6:
			GameTargetPicker = (ComboBox)target;
			break;
		case 7:
			SelectedGameVersionBox = (TextBox)target;
			break;
		case 8:
			((Button)target).Click += GoToAiStage_Click;
			break;
		case 9:
			GameMacroWorkbenchContent = (GameMacroWorkbenchView)target;
			break;
		case 10:
			StageContextLabel = (TextBlock)target;
			break;
		case 11:
			AiChatHistory = (ListBox)target;
			break;
		case 12:
			AiChatInput = (TextBox)target;
			break;
		case 13:
			((Button)target).Click += AiChatSend_Click;
			break;
		case 14:
			((Button)target).Click += SaveAiArtifact_Click;
			break;
		case 15:
			((Button)target).Click += GoToArtifactsStage_Click;
			break;
		case 16:
			PlanSummary = (TextBlock)target;
			break;
		case 17:
			((Button)target).Click += AiPlan_Click;
			break;
		case 18:
			RunAiPlanButton = (Button)target;
			RunAiPlanButton.Click += RunAiPlan_Click;
			break;
		case 19:
			RunEndermanSuiteButton = (Button)target;
			RunEndermanSuiteButton.Click += RunEndermanSuite_Click;
			break;
		case 20:
			RerunSuiteAnomaliesButton = (Button)target;
			RerunSuiteAnomaliesButton.Click += RerunSuiteAnomalies_Click;
			break;
		case 21:
			JavaDeathSmokeButton = (Button)target;
			JavaDeathSmokeButton.Click += JavaDeathSmoke_Click;
			break;
		case 22:
			JavaFallTestButton = (Button)target;
			JavaFallTestButton.Click += JavaFallTest_Click;
			break;
		case 23:
			JavaWaterTestButton = (Button)target;
			JavaWaterTestButton.Click += JavaWaterTest_Click;
			break;
		case 24:
			Tc014RelocationSmokeButton = (Button)target;
			Tc014RelocationSmokeButton.Click += Tc014RelocationSmoke_Click;
			break;
		case 25:
			Tc014ClientTestButton = (Button)target;
			Tc014ClientTestButton.Click += Tc014ClientTest_Click;
			break;
		case 26:
			Tc060ClientTestButton = (Button)target;
			Tc060ClientTestButton.Click += Tc060ClientTest_Click;
			break;
		case 27:
			RunPlanButton = (Button)target;
			RunPlanButton.Click += PreviewPlan_Click;
			break;
		case 28:
			StopRunButton = (Button)target;
			StopRunButton.Click += StopRun_Click;
			break;
		case 29:
			ArtifactProjectPathLabel = (TextBlock)target;
			break;
		case 30:
			ProjectArtifactList = (ListBox)target;
			ProjectArtifactList.SelectionChanged += ProjectArtifactList_SelectionChanged;
			break;
		case 31:
			ArtifactPreviewTitle = (TextBlock)target;
			break;
		case 32:
			ArtifactPreview = (TextBox)target;
			break;
		case 33:
			((Button)target).Click += RefreshProjectArtifacts_Click;
			break;
		case 34:
			((Button)target).Click += GoToJiraStage_Click;
			break;
		case 35:
			ArtifactHashLabel = (TextBlock)target;
			break;
		case 36:
			JiraStageStatus = (TextBlock)target;
			break;
		case 37:
			((Button)target).Click += ConfigureJira_Click;
			break;
		case 38:
			((Button)target).Click += CreateArtifactJiraIssue_Click;
			break;
		case 39:
			((Button)target).Click += SubmitSuiteIssues_Click;
			break;
		case 40:
			((Button)target).Click += CreateJiraIssue_Click;
			break;
		case 41:
			ToolStudioContent = (ToolStudioView)target;
			break;
		case 42:
			OnlyChatEmbeddedContent = (OnlyChatEmbeddedView)target;
			break;
		case 43:
			MainContentGrid = (Grid)target;
			break;
		case 44:
			((Button)target).Click += AiDraft_Click;
			break;
		case 45:
			CaseTitle = (TextBox)target;
			break;
		case 46:
			CaseTarget = (TextBox)target;
			break;
		case 47:
			GameVersion = (TextBox)target;
			break;
		case 48:
			Preconditions = (TextBox)target;
			break;
		case 49:
			ManualSteps = (TextBox)target;
			break;
		case 50:
			ExpectedResult = (TextBox)target;
			break;
		case 51:
			ActualResult = (TextBox)target;
			break;
		case 52:
			VerdictPicker = (ComboBox)target;
			break;
		case 53:
			ManualNotes = (TextBox)target;
			break;
		case 54:
			((Button)target).Click += AttachScreenshot_Click;
			break;
		case 55:
			((Button)target).Click += AttachVideo_Click;
			break;
		case 56:
			((Button)target).Click += AttachOther_Click;
			break;
		case 57:
			EvidenceList = (ListBox)target;
			EvidenceList.SelectionChanged += ManualEvidenceList_SelectionChanged;
			break;
		case 58:
			((Button)target).Click += SaveManualRecord_Click;
			break;
		case 59:
			((Button)target).Click += LoadManualRecord_Click;
			break;
		case 60:
			((Button)target).Click += NewManualRecord_Click;
			break;
		case 61:
			EventLog = (TextBox)target;
			break;
		case 62:
			EvidencePreviewTab = (TabItem)target;
			break;
		case 63:
			ManualEvidencePreview = (EvidencePreviewControl)target;
			break;
		case 64:
			SuiteResultsContent = (SuiteResultsView)target;
			break;
		case 65:
			SuiteResultsPresentation.ApplyExportActionButton((Button)target, primary: true);
			ExcelExportButton = (Button)target;
			ExcelExportButton.Click += ExportExcel_Click;
			break;
		case 66:
			SuiteResultsPresentation.ApplyExportActionButton((Button)target, primary: false);
			JiraCsvExportButton = (Button)target;
			JiraCsvExportButton.Click += ExportJira_Click;
			break;
		case 67:
			JavaPathBox = (TextBox)target;
			break;
		case 68:
			((Button)target).Click += SelectJava_Click;
			break;
		case 69:
			ExistingInstanceIdBox = (TextBox)target;
			break;
		case 70:
			((Button)target).Click += OpenExistingJavaServer_Click;
			break;
		case 71:
			EulaAcceptedCheck = (CheckBox)target;
			break;
		case 72:
			((Button)target).Click += PrepareJavaServer_Click;
			break;
		case 73:
			StartJavaServerButton = (Button)target;
			StartJavaServerButton.Click += StartJavaServer_Click;
			break;
		case 74:
			StopJavaServerButton = (Button)target;
			StopJavaServerButton.Click += StopJavaServer_Click;
			break;
		case 75:
			UseLatencyProxyCheckBox = (CheckBox)target;
			break;
		case 76:
			ClientToServerDelayBox = (TextBox)target;
			break;
		case 77:
			ServerToClientDelayBox = (TextBox)target;
			break;
		case 78:
			ClientSessionStatus = (TextBlock)target;
			break;
		case 79:
			ClientCaptureCaseIdBox = (TextBox)target;
			break;
		case 80:
			StartJavaClientButton = (Button)target;
			StartJavaClientButton.Click += StartJavaClient_Click;
			break;
		case 81:
			CaptureClientButton = (Button)target;
			CaptureClientButton.Click += CaptureClient_Click;
			break;
		case 82:
			StopJavaClientButton = (Button)target;
			StopJavaClientButton.Click += StopJavaClient_Click;
			break;
		case 83:
			WorkflowGuideButton = (Button)target;
			WorkflowGuideButton.Click += WorkflowGuide_Click;
			break;
		case 84:
			((Button)target).Click += BrowseProjectData_Click;
			break;
		case 85:
			((Button)target).Click += ObserveFabric_Click;
			break;
		case 86:
			((Button)target).Click += VirtualDisplayStatus_Click;
			break;
		case 87:
			AiSetupButton = (Button)target;
			AiSetupButton.Click += OpenAiSetup_Click;
			break;
		case 88:
			((Button)target).Click += RouterStatus_Click;
			break;
		case 89:
			((Button)target).Click += ConfigureJira_Click;
			break;
		case 90:
			((Button)target).Click += StopRun_Click;
			break;
		case 91:
			MacroAndStorageContent = (MacroAndStorageView)target;
			break;
		case 92:
			ModuleManagerContent = (ModuleManagerView)target;
			break;
		case 93:
			TargetStatus = (TextBlock)target;
			break;
		case 94:
			WorkflowGuideContent = (WorkflowGuideView)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}


