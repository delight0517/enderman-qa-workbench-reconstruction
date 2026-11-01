using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndermanQa.Desktop.Adapters;
using EndermanQa.Desktop.Persistence;
using Microsoft.Win32;

namespace EndermanQa.Desktop;

internal sealed class QaProjectDialog : Window
{
	private sealed record ProjectChoice(QaProjectDefinition Project, string Label, BitmapFrame? Thumbnail);

	private readonly QaProjectCatalog _catalog;

	private readonly string _caseCatalogSource;

	private readonly ListBox _projects = new ListBox
	{
		MinHeight = 260.0,
		Margin = new Thickness(0.0, 8.0, 12.0, 0.0),
		Padding = new Thickness(6.0),
		Background = Brush("Theme.Surface"),
		BorderBrush = Brush("Theme.Border")
	};

	private readonly TextBox _projectSearch = FieldBox();

	private readonly TextBlock _projectCount = new TextBlock
	{
		Foreground = Brush("Theme.TextMuted"),
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly TextBlock _projectDetails = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Brush("Theme.TextSecondary"),
		Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
	};

	private readonly Image _projectThumbnail = new Image
	{
		Height = 130.0,
		Stretch = Stretch.UniformToFill,
		Margin = new Thickness(0.0, 8.0, 0.0, 8.0)
	};

	private readonly TextBox _selectedGameTarget = FieldBox();

	private readonly TextBox _selectedDescription = FieldBox();

	private readonly TextBox _selectedGameProjectRoot = FieldBox();

	private readonly ComboBox _selectedEngine = CreateEnginePicker();

	private readonly ListBox _registeredFiles = new ListBox
	{
		Height = 116.0,
		Background = Brush("Theme.SurfaceInset"),
		Foreground = Brush("Theme.TextPrimary"),
		BorderBrush = Brush("Theme.Border")
	};

	private readonly ProjectLibraryStore _library = new ProjectLibraryStore();

	private readonly string? _activeId;

	private readonly TextBox _newName = FieldBox();

	private readonly TextBox _newGameTarget = FieldBox();

	private readonly ComboBox _newEngine = CreateEnginePicker();

	private readonly TextBox _newDescription = FieldBox();

	private readonly TextBox _newGameProjectRoot = FieldBox();

	private readonly ComboBox _newProjectTemplate = new ComboBox
	{
		Width = 380.0,
		HorizontalAlignment = HorizontalAlignment.Left,
		Margin = new Thickness(0.0, 4.0, 0.0, 10.0),
		Padding = new Thickness(7.0)
	};

	private readonly TextBlock _newThumbnailLabel = new TextBlock
	{
		Text = "기본 QA 프로젝트 썸네일 사용 · 게임별 이미지로 바꿀 수 있습니다",
		Foreground = Brush("Theme.TextMuted"),
		VerticalAlignment = VerticalAlignment.Center
	};

	private string? _newThumbnailPath;

	private readonly TextBox _jiraSite = FieldBox();

	private readonly TextBox _jiraProjectName = FieldBox();

	private readonly TextBox _jiraProjectKey = FieldBox();

	private readonly TextBox _jiraIssueTypes = FieldBox();

	public QaProjectDefinition? SelectedProject { get; private set; }

	private ProjectChoice? SelectedChoice => _projects.SelectedItem as ProjectChoice;

	private QaProjectDialog(Window owner, QaProjectCatalog catalog, string caseCatalogSource, string? activeId)
	{
		base.Owner = owner;
		_catalog = catalog;
		_caseCatalogSource = caseCatalogSource;
		_activeId = activeId;
		base.Title = "QA 프로젝트 라이브러리";
		base.Width = 1160.0;
		base.Height = 840.0;
		base.MinWidth = 980.0;
		base.MinHeight = 720.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.ResizeMode = ResizeMode.CanResize;
		base.Background = Brush("Theme.Canvas");
		base.Foreground = Brush("Theme.TextPrimary");
		base.FontFamily = new FontFamily("Segoe UI");
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(24.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "프로젝트 허브",
			FontSize = 25.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brush("Theme.TextStrong")
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "게임별 썸네일과 프로젝트 폴더를 확인하고, 관련 파일을 프로젝트에 복사·등록합니다. QA 기록과 파일은 프로젝트별로 분리됩니다.",
			FontSize = 14.0,
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("Theme.TextSecondary"),
			Margin = new Thickness(0.0, 4.0, 0.0, 16.0)
		});
		TabControl tabControl = new TabControl
		{
			Background = Brush("Theme.Canvas"),
			BorderThickness = new Thickness(0.0),
			Foreground = Brush("Theme.TextStrong")
		};
		StackPanel stackPanel2 = new StackPanel
		{
			Margin = new Thickness(8.0, 14.0, 8.0, 8.0)
		};
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition(),
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			},
			Children = { (UIElement)SectionTitle("프로젝트 작업 공간") }
		};
		Grid.SetColumn(_projectCount, 1);
		grid.Children.Add(_projectCount);
		stackPanel2.Children.Add(grid);
		stackPanel2.Children.Add(new TextBlock
		{
			Text = "프로젝트를 바꾸면 해당 프로젝트의 실행 결과·초안·증거·Jira 설정만 표시됩니다.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("Theme.TextSecondary"),
			Margin = new Thickness(0.0, 2.0, 0.0, 8.0)
		});
		_projectSearch.ToolTip = "프로젝트 이름 또는 Jira 키 검색";
		_projectSearch.TextChanged += delegate
		{
			RefreshProjects(_activeId);
		};
		stackPanel2.Children.Add(_projectSearch);
		RefreshProjects(activeId);
		Grid grid2 = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(0.95, GridUnitType.Star)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.15, GridUnitType.Star)
		});
		Grid.SetColumn(_projects, 0);
		grid2.Children.Add(_projects);
		Border border = new Border
		{
			Background = Brush("Theme.Surface"),
			BorderBrush = Brush("Theme.Border"),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(16.0),
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0),
			MinHeight = 260.0
		};
		Grid.SetColumn(border, 1);
		StackPanel stackPanel3 = new StackPanel
		{
			Children = 
			{
				(UIElement)new TextBlock
				{
					Text = "프로젝트 대표 썸네일",
					FontSize = 12.0,
					FontWeight = FontWeights.SemiBold,
					Foreground = Brush("Theme.TextAccent")
				},
				(UIElement)_projectThumbnail,
				(UIElement)_projectDetails
			}
		};
		AddField(stackPanel3, "게임 / 앱", _selectedGameTarget, "예: Minecraft Java Edition");
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "엔진 / 테스트 도구 계열",
			Foreground = Brush("Theme.TextMuted")
		});
		stackPanel3.Children.Add(_selectedEngine);
		AddGameProjectFolderField(stackPanel3, _selectedGameProjectRoot, _selectedEngine, "게임 프로젝트 폴더 (읽기 전용 연결)");
		AddField(stackPanel3, "프로젝트 메모", _selectedDescription, "이 프로젝트에서 맡은 게임과 QA 범위");
		Button button = SecondaryButton("프로젝트 정보 저장");
		button.Click += delegate
		{
			SaveSelectedMetadata();
		};
		stackPanel3.Children.Add(button);
		WrapPanel wrapPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
		};
		AddAction(wrapPanel, "대표 이미지 변경", ChooseSelectedThumbnail);
		AddAction(wrapPanel, "프로젝트 폴더", delegate
		{
			OpenSelectedFolder((QaProjectDefinition x) => x.RootPath);
		});
		AddAction(wrapPanel, "데이터 폴더", delegate
		{
			OpenSelectedFolder((QaProjectDefinition x) => x.DataRoot);
		});
		AddAction(wrapPanel, "파일 폴더", delegate
		{
			OpenSelectedFolder((QaProjectDefinition x) => Path.Combine(x.RootPath, "files"));
		});
		stackPanel3.Children.Add(wrapPanel);
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "등록된 프로젝트 파일 · 원본은 이동하지 않고 이 프로젝트의 files 폴더에 사본을 보관합니다.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("Theme.TextSecondary"),
			Margin = new Thickness(0.0, 8.0, 0.0, 4.0)
		});
		stackPanel3.Children.Add(_registeredFiles);
		WrapPanel wrapPanel2 = new WrapPanel
		{
			Margin = new Thickness(0.0, 5.0, 0.0, 0.0)
		};
		AddAction(wrapPanel2, "파일 등록", RegisterSelectedFiles);
		AddAction(wrapPanel2, "선택 파일 열기", OpenSelectedFile);
		stackPanel3.Children.Add(wrapPanel2);
		border.Child = stackPanel3;
		grid2.Children.Add(border);
		stackPanel2.Children.Add(grid2);
		Button button2 = PrimaryButton("선택한 프로젝트 열기");
		button2.HorizontalAlignment = HorizontalAlignment.Right;
		button2.Click += delegate
		{
			ActivateSelected();
		};
		stackPanel2.Children.Add(button2);
		tabControl.Items.Add(new TabItem
		{
			Header = "프로젝트 열기",
			Content = stackPanel2
		});
		StackPanel stackPanel4 = new StackPanel
		{
			Margin = new Thickness(8.0, 14.0, 8.0, 8.0)
		};
		stackPanel4.Children.Add(SectionTitle("새 QA 프로젝트 만들기"));
		stackPanel4.Children.Add(new TextBlock
		{
			Text = "새 게임 QA 프로젝트는 기본적으로 빈 일반 작업 공간으로 시작합니다. Enderman 전용 100 TC는 템플릿에서 명시적으로 선택할 수 있습니다. 다른 프로젝트의 실행 결과·초안·증거·Jira 기록은 복사하지 않습니다.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("Theme.TextSecondary"),
			Margin = new Thickness(0.0, 3.0, 0.0, 12.0)
		});
		AddField(stackPanel4, "프로젝트 이름", _newName, "예: 엔더맨 100 TC 프로젝트");
		_newProjectTemplate.Items.Add(new ComboBoxItem
		{
			Content = "일반 게임 QA · 빈 케이스 목록",
			Tag = "generic"
		});
		_newProjectTemplate.Items.Add(new ComboBoxItem
		{
			Content = "Enderman QA · 100 TC 템플릿",
			Tag = "enderman-100"
		});
		_newProjectTemplate.SelectedIndex = 0;
		_newProjectTemplate.SelectionChanged += delegate
		{
			if ((_newProjectTemplate.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "enderman-100")
			{
				if (string.IsNullOrWhiteSpace(_newGameTarget.Text))
				{
					_newGameTarget.Text = "Minecraft Java Edition";
				}
				_newEngine.SelectedItem = _newEngine.Items.OfType<ComboBoxItem>().FirstOrDefault((ComboBoxItem x) => x.Tag?.ToString() == "minecraft-java");
			}
		};
		stackPanel4.Children.Add(new TextBlock
		{
			Text = "테스트 템플릿",
			Foreground = Brush("Theme.TextMuted")
		});
		stackPanel4.Children.Add(_newProjectTemplate);
		_newGameTarget.ToolTip = "일반 QA 프로젝트는 게임/앱 이름을 입력하세요. Enderman 100 TC 템플릿은 Minecraft Java Edition 전용입니다.";
		AddField(stackPanel4, "게임 / 앱 이름", _newGameTarget, "예: Minecraft Java Edition, 다른 게임 또는 앱");
		stackPanel4.Children.Add(new TextBlock
		{
			Text = "엔진 / 테스트 도구 계열",
			Foreground = Brush("Theme.TextMuted")
		});
		stackPanel4.Children.Add(_newEngine);
		AddGameProjectFolderField(stackPanel4, _newGameProjectRoot, _newEngine, "게임 프로젝트 폴더 (선택 · 읽기 전용 등록)");
		_newDescription.AcceptsReturn = true;
		_newDescription.Height = 56.0;
		_newDescription.TextWrapping = TextWrapping.Wrap;
		AddField(stackPanel4, "이 프로젝트의 범위", _newDescription, "게임, QA 대상, 만들 도구를 간단히 적으세요.");
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 4.0, 0.0, 10.0)
		};
		Button button3 = SecondaryButton("프로젝트 썸네일 선택");
		button3.Click += ChooseNewThumbnail;
		DockPanel.SetDock(button3, Dock.Right);
		dockPanel.Children.Add(button3);
		dockPanel.Children.Add(_newThumbnailLabel);
		stackPanel4.Children.Add(dockPanel);
		stackPanel4.Children.Add(SectionTitle("Jira 연결 정보 · 선택"));
		AddField(stackPanel4, "사이트 주소", _jiraSite, "https://your-team.atlassian.net");
		AddField(stackPanel4, "프로젝트 이름", _jiraProjectName, "Jira 프로젝트 표시 이름");
		AddField(stackPanel4, "프로젝트 키", _jiraProjectKey, "예: VW");
		AddField(stackPanel4, "사용 가능한 이슈 유형", _jiraIssueTypes, "예: Task, Workstream");
		stackPanel4.Children.Add(new TextBlock
		{
			Text = "Jira 정보는 프로젝트별 메타데이터입니다. API 토큰은 이 폼에 저장하지 않습니다.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("Theme.TextMuted"),
			Margin = new Thickness(0.0, 1.0, 0.0, 12.0)
		});
		Button button4 = PrimaryButton("프로젝트 만들고 열기");
		button4.HorizontalAlignment = HorizontalAlignment.Right;
		button4.IsDefault = true;
		button4.Click += delegate
		{
			CreateNew();
		};
		stackPanel4.Children.Add(button4);
		tabControl.Items.Add(new TabItem
		{
			Header = "새 프로젝트",
			Content = new ScrollViewer
			{
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Content = stackPanel4
			}
		});
		stackPanel.Children.Add(tabControl);
		base.Content = stackPanel;
	}

	public static QaProjectDefinition? Show(Window owner, QaProjectCatalog catalog, string caseCatalogSource, string? activeId)
	{
		QaProjectDialog qaProjectDialog = new QaProjectDialog(owner, catalog, caseCatalogSource, activeId);
		if (!qaProjectDialog.ShowDialog().GetValueOrDefault())
		{
			return null;
		}
		return qaProjectDialog.SelectedProject;
	}

	private static TextBox FieldBox()
	{
		return new TextBox
		{
			Padding = new Thickness(9.0),
			Margin = new Thickness(0.0, 3.0, 0.0, 8.0),
			Background = Brush("Theme.SurfaceInset"),
			Foreground = Brush("Theme.TextStrong")
		};
	}

	private static ComboBox CreateEnginePicker()
	{
		return new ComboBox
		{
			Width = 380.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0.0, 3.0, 0.0, 8.0),
			Padding = new Thickness(7.0),
			Items = 
			{
				(object)new ComboBoxItem
				{
					Content = "엔진 미확인 · 먼저 조사 필요",
					Tag = "unknown"
				},
				(object)new ComboBoxItem
				{
					Content = "Unity",
					Tag = "unity"
				},
				(object)new ComboBoxItem
				{
					Content = "Unreal Engine",
					Tag = "unreal"
				},
				(object)new ComboBoxItem
				{
					Content = "Godot",
					Tag = "godot"
				},
				(object)new ComboBoxItem
				{
					Content = "Minecraft Java / Fabric",
					Tag = "minecraft-java"
				},
				(object)new ComboBoxItem
				{
					Content = "기타 / 사용자 지정",
					Tag = "custom"
				}
			},
			SelectedIndex = 0
		};
	}

	private static Brush Brush(string key)
	{
		return (Application.Current.TryFindResource(key) as Brush) ?? Brushes.White;
	}

	private static TextBlock SectionTitle(string text)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = 16.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brush("Theme.TextStrong"),
			Margin = new Thickness(0.0, 4.0, 0.0, 2.0)
		};
	}

	private static void AddField(Panel panel, string label, TextBox box, string placeholder)
	{
		panel.Children.Add(new TextBlock
		{
			Text = label,
			FontWeight = FontWeights.Medium,
			Foreground = Brush("Theme.TextSecondary")
		});
		box.ToolTip = placeholder;
		panel.Children.Add(box);
	}

	private void AddGameProjectFolderField(Panel panel, TextBox pathBox, ComboBox enginePicker, string label)
	{
		TextBox pathBox2 = pathBox;
		ComboBox enginePicker2 = enginePicker;
		panel.Children.Add(new TextBlock
		{
			Text = label,
			Foreground = Brush("Theme.TextMuted")
		});
		pathBox2.IsReadOnly = true;
		pathBox2.ToolTip = "엔진 표식 확인을 위한 읽기 전용 연결입니다. Workbench는 게임 소스나 저장 파일을 수정하지 않습니다.";
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 3.0, 0.0, 8.0)
		};
		Button button = SecondaryButton("폴더 선택");
		button.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		DockPanel.SetDock(button, Dock.Right);
		TextBlock detectionStatus = new TextBlock
		{
			Text = (string.IsNullOrWhiteSpace(pathBox2.Text) ? "게임 소스 경로를 연결하면 엔진 표식을 읽어봅니다. 폴더를 실행하거나 수정하지 않습니다." : "등록된 경로입니다. 다시 선택해 엔진 표식을 확인하세요."),
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("Theme.TextSecondary"),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		button.Click += delegate
		{
			SelectGameProjectFolder(pathBox2, enginePicker2, detectionStatus);
		};
		dockPanel.Children.Add(button);
		dockPanel.Children.Add(pathBox2);
		panel.Children.Add(dockPanel);
		panel.Children.Add(detectionStatus);
	}

	private void SelectGameProjectFolder(TextBox pathBox, ComboBox enginePicker, TextBlock detectionStatus)
	{
		try
		{
			OpenFolderDialog openFolderDialog = new OpenFolderDialog
			{
				Title = "게임 소스 프로젝트 폴더 선택",
				Multiselect = false
			};
			if (Directory.Exists(pathBox.Text))
			{
				openFolderDialog.FolderName = pathBox.Text;
			}
			if (!openFolderDialog.ShowDialog(this).GetValueOrDefault())
			{
				return;
			}
			string fullPath = Path.GetFullPath(openFolderDialog.FolderName);
			GameProjectEngineDetection detection = GameProjectEngineDetector.Detect(fullPath);
			pathBox.Text = fullPath;
			if (detection.EngineId != "unknown")
			{
				enginePicker.SelectedItem = enginePicker.Items.OfType<ComboBoxItem>().FirstOrDefault((ComboBoxItem item) => item.Tag?.ToString() == detection.EngineId);
			}
			detectionStatus.Text = ((detection.EngineId == "unknown") ? ("자동 판별 보류 · " + detection.Detail + " 경로는 계속 읽기 전용입니다.") : $"감지: {detection.EngineId}{(string.IsNullOrWhiteSpace(detection.Version) ? string.Empty : (" " + detection.Version))} · 표식 파일 {detection.EvidenceRelativePath}. 엔진 분류만 했으며 게임/에디터는 실행하지 않았습니다.");
			if (detection.EngineId == "unknown")
			{
				MessageBox.Show(this, detection.Detail, "엔진 자동 판별 결과", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, "선택한 폴더를 읽지 못했습니다. 게임 파일은 변경되지 않았습니다.\n" + ex.Message, "게임 프로젝트 폴더 확인 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private static Button PrimaryButton(string text)
	{
		return new Button
		{
			Content = text,
			Padding = new Thickness(14.0, 9.0, 14.0, 9.0),
			Background = Brush("Theme.Accent"),
			Foreground = Brushes.White,
			BorderThickness = new Thickness(0.0),
			Cursor = Cursors.Hand
		};
	}

	private static Button SecondaryButton(string text)
	{
		return new Button
		{
			Content = text,
			Padding = new Thickness(8.0, 5.0, 8.0, 5.0),
			Margin = new Thickness(0.0, 3.0, 5.0, 3.0),
			Cursor = Cursors.Hand
		};
	}

	private static void AddAction(Panel panel, string text, RoutedEventHandler click)
	{
		Button button = SecondaryButton(text);
		button.Click += click;
		panel.Children.Add(button);
	}

	private static DataTemplate ProjectItemTemplate()
	{
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(StackPanel));
		frameworkElementFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
		frameworkElementFactory.SetValue(FrameworkElement.HeightProperty, 72.0);
		FrameworkElementFactory frameworkElementFactory2 = new FrameworkElementFactory(typeof(Image));
		frameworkElementFactory2.SetValue(FrameworkElement.WidthProperty, 94.0);
		frameworkElementFactory2.SetValue(FrameworkElement.HeightProperty, 58.0);
		frameworkElementFactory2.SetValue(FrameworkElement.MarginProperty, new Thickness(0.0, 0.0, 10.0, 0.0));
		frameworkElementFactory2.SetValue(Image.StretchProperty, Stretch.UniformToFill);
		frameworkElementFactory2.SetBinding(Image.SourceProperty, new Binding("Thumbnail"));
		frameworkElementFactory.AppendChild(frameworkElementFactory2);
		FrameworkElementFactory frameworkElementFactory3 = new FrameworkElementFactory(typeof(TextBlock));
		frameworkElementFactory3.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
		frameworkElementFactory3.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		frameworkElementFactory3.SetBinding(TextBlock.TextProperty, new Binding("Label"));
		frameworkElementFactory.AppendChild(frameworkElementFactory3);
		return new DataTemplate
		{
			VisualTree = frameworkElementFactory
		};
	}

	private void RefreshProjects(string? activeId)
	{
		string activeId2 = activeId;
		IReadOnlyList<QaProjectDefinition> readOnlyList = _catalog.ListProjects();
		string query = _projectSearch.Text.Trim();
		ProjectChoice[] array = (from project in readOnlyList
			where query.Length == 0 || project.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || (project.JiraProjectKey?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
			select new ProjectChoice(project, $"{((project.ProjectId == activeId2) ? "● 현재 열림" : "○ 등록됨")} · {project.Name}\n{project.GameTarget ?? "게임 대상 미설정"} · {QaProjectCatalog.GetProjectEngineId(project)}\n{(string.IsNullOrWhiteSpace(project.JiraProjectKey) ? "Jira 미연결" : ("Jira · " + project.JiraProjectKey))}", _library.LoadThumbnail(project, Path.Combine(AppContext.BaseDirectory, "enderman-qa.png")))).ToArray();
		_projects.ItemsSource = array;
		_projects.ItemTemplate = ProjectItemTemplate();
		_projectCount.Text = $"{readOnlyList.Count}개 프로젝트";
		_projects.SelectedItem = array.FirstOrDefault((ProjectChoice x) => x.Project.ProjectId == activeId2) ?? array.FirstOrDefault();
		_projects.SelectionChanged -= Projects_SelectionChanged;
		_projects.SelectionChanged += Projects_SelectionChanged;
		UpdateProjectDetails(_projects.SelectedItem as ProjectChoice);
	}

	private void Projects_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateProjectDetails(_projects.SelectedItem as ProjectChoice);
	}

	private void UpdateProjectDetails(ProjectChoice? choice)
	{
		if ((object)choice == null)
		{
			_projectDetails.Text = "검색 결과가 없습니다. 검색어를 지우거나 새 QA 프로젝트를 만드세요.";
			return;
		}
		QaProjectDefinition project = choice.Project;
		try
		{
			_library.InitializeProjectFolders(project);
			if (project.ThumbnailRelativePath == null || (!project.ThumbnailRelativePath.Equals("assets/thumbnail.png", StringComparison.OrdinalIgnoreCase) && !project.ThumbnailRelativePath.Equals(Path.Combine("assets", "thumbnail.png"), StringComparison.OrdinalIgnoreCase)) || !File.Exists(Path.Combine(project.RootPath, "assets", "thumbnail.png")))
			{
				_library.SetDefaultThumbnail(project, _catalog);
				project = _catalog.ListProjects().FirstOrDefault((QaProjectDefinition x) => x.ProjectId == project.ProjectId) ?? project;
			}
			_projectThumbnail.Source = _library.LoadThumbnail(project, Path.Combine(AppContext.BaseDirectory, "enderman-qa.png"));
			_registeredFiles.ItemsSource = (from file in _library.ListFiles(project)
				select $"{file.Name}  ·  {(double)file.SizeBytes / 1024.0:N0} KB  ·  SHA {file.Sha256.Substring(0, 8)}").ToArray();
			_selectedGameTarget.Text = project.GameTarget ?? "Minecraft Java Edition";
			_selectedEngine.SelectedItem = _selectedEngine.Items.OfType<ComboBoxItem>().FirstOrDefault((ComboBoxItem x) => x.Tag?.ToString() == QaProjectCatalog.GetProjectEngineId(project)) ?? _selectedEngine.Items[0];
			_selectedGameProjectRoot.Text = project.GameProjectRoot ?? string.Empty;
			_selectedDescription.Text = project.Description ?? string.Empty;
		}
		catch (Exception ex) when (((ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			_projectThumbnail.Source = null;
			_registeredFiles.ItemsSource = Array.Empty<string>();
			_projectDetails.Text = "프로젝트 파일 공간을 읽지 못했습니다: " + ex.Message;
			return;
		}
		string value = (string.IsNullOrWhiteSpace(project.JiraProjectKey) ? "연결 정보 없음" : $"{project.JiraProjectName ?? project.JiraProjectKey} ({project.JiraProjectKey})\n{project.JiraSite}");
		_projectDetails.Text = $"{project.Name}\n{((project.ProjectId == _activeId) ? "현재 열려 있음" : "열면 이 프로젝트의 데이터만 표시")}\n게임: {project.GameTarget}\n엔진/도구 계열: {QaProjectCatalog.GetProjectEngineId(project)}\n게임 소스: {project.GameProjectRoot ?? "미등록"}\n\nJira · {value}\n\n폴더\n{project.RootPath}";
	}

	private void SaveSelectedMetadata()
	{
		ProjectChoice selectedChoice = SelectedChoice;
		if ((object)selectedChoice == null)
		{
			return;
		}
		try
		{
			_catalog.UpdateProjectPresentation(selectedChoice.Project.ProjectId, _selectedGameTarget.Text, _selectedDescription.Text, null, (_selectedEngine.SelectedItem as ComboBoxItem)?.Tag?.ToString(), _selectedGameProjectRoot.Text);
			RefreshProjects(_activeId);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, ex.Message, "프로젝트 정보 저장 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void OpenSelectedFolder(Func<QaProjectDefinition, string> select)
	{
		ProjectChoice selectedChoice = SelectedChoice;
		if ((object)selectedChoice == null)
		{
			return;
		}
		try
		{
			_library.OpenFolder(selectedChoice.Project, select(selectedChoice.Project));
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, ex.Message, "폴더 열기 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void ChooseSelectedThumbnail(object sender, RoutedEventArgs e)
	{
		ProjectChoice choice = SelectedChoice;
		if ((object)choice == null)
		{
			return;
		}
		string text = ChooseImage();
		if (text == null)
		{
			return;
		}
		try
		{
			_library.SetThumbnail(choice.Project, text, _catalog);
			RefreshProjects(_activeId);
			_projects.SelectedItem = _projects.Items.Cast<ProjectChoice>().FirstOrDefault((ProjectChoice x) => x.Project.ProjectId == choice.Project.ProjectId);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, ex.Message, "썸네일 저장 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void RegisterSelectedFiles(object sender, RoutedEventArgs e)
	{
		ProjectChoice selectedChoice = SelectedChoice;
		if ((object)selectedChoice == null)
		{
			return;
		}
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "이 프로젝트에 등록할 파일 선택",
			Multiselect = true,
			Filter = "QA 자료|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.mp4;*.mov;*.webm;*.avi;*.txt;*.md;*.log;*.csv;*.json;*.xml;*.yaml;*.yml;*.toml;*.pdf;*.docx;*.xlsx;*.pptx;*.cs;*.js;*.ts;*.html;*.css;*.py;*.java;*.lua|모든 파일|*.*"
		};
		if (!openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			return;
		}
		try
		{
			_library.RegisterFiles(selectedChoice.Project, openFileDialog.FileNames);
			UpdateProjectDetails(selectedChoice);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, ex.Message, "파일 등록 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void OpenSelectedFile(object sender, RoutedEventArgs e)
	{
		ProjectChoice selectedChoice = SelectedChoice;
		if ((object)selectedChoice == null || _registeredFiles.SelectedIndex < 0)
		{
			return;
		}
		try
		{
			ProjectLibraryStore.ProjectFileEntry projectFileEntry = _library.ListFiles(selectedChoice.Project).ElementAtOrDefault(_registeredFiles.SelectedIndex);
			if ((object)projectFileEntry != null)
			{
				_library.OpenFile(selectedChoice.Project, projectFileEntry);
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is InvalidOperationException || ex is Win32Exception) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, ex.Message, "파일 열기 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void ChooseNewThumbnail(object sender, RoutedEventArgs e)
	{
		_newThumbnailPath = ChooseImage();
		if (_newThumbnailPath != null)
		{
			_newThumbnailLabel.Text = Path.GetFileName(_newThumbnailPath) + " · 새 프로젝트로 복사됩니다.";
		}
	}

	private string? ChooseImage()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "프로젝트 대표 이미지 선택",
			Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
			CheckFileExists = true
		};
		if (!openFileDialog.ShowDialog(this).GetValueOrDefault())
		{
			return null;
		}
		return openFileDialog.FileName;
	}

	private void ActivateSelected()
	{
		if (_projects.SelectedItem is ProjectChoice projectChoice)
		{
			_catalog.Activate(projectChoice.Project.ProjectId);
			SelectedProject = projectChoice.Project;
			base.DialogResult = true;
		}
	}

	private void CreateNew()
	{
		try
		{
			string name = _newName.Text.Trim();
			if (string.IsNullOrWhiteSpace(_newGameTarget.Text))
			{
				throw new ArgumentException("게임 또는 앱 이름을 입력하세요.");
			}
			QaProjectDefinition qaProjectDefinition = _catalog.ListProjects().FirstOrDefault((QaProjectDefinition project) => project.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
			if ((object)qaProjectDefinition != null)
			{
				if (MessageBox.Show(this, "‘" + qaProjectDefinition.Name + "’ 프로젝트가 이미 있습니다. 기존 작업 공간을 열까요?\n\n기존 실행 기록과 설정은 유지됩니다.", "기존 프로젝트 확인", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
				{
					_catalog.Activate(qaProjectDefinition.ProjectId);
					SelectedProject = qaProjectDefinition;
					base.DialogResult = true;
				}
				return;
			}
			string[] jiraIssueTypes = _jiraIssueTypes.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			string text = _jiraSite.Text.Trim();
			if (text.Length > 0 && (!Uri.TryCreate(text, UriKind.Absolute, out Uri result) || result.Scheme != Uri.UriSchemeHttps))
			{
				throw new ArgumentException("Jira 사이트는 https:// 주소로 입력하세요.");
			}
			bool flag = (_newProjectTemplate.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "enderman-100";
			if (flag && !_newGameTarget.Text.Contains("Minecraft", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("Enderman 100 TC 템플릿은 Minecraft Java Edition 대상 프로젝트에만 사용할 수 있습니다.");
			}
			QaProjectDefinition qaProjectDefinition2 = _catalog.CreateProject(name, flag ? _caseCatalogSource : null, text, _jiraProjectName.Text.Trim(), _jiraProjectKey.Text.Trim(), jiraIssueTypes, _newGameTarget.Text, _newDescription.Text, (_newEngine.SelectedItem as ComboBoxItem)?.Tag?.ToString(), string.IsNullOrWhiteSpace(_newGameProjectRoot.Text) ? null : _newGameProjectRoot.Text);
			_library.SetDefaultThumbnail(qaProjectDefinition2, _catalog);
			if (_newThumbnailPath != null)
			{
				try
				{
					_library.SetThumbnail(qaProjectDefinition2, _newThumbnailPath, _catalog);
				}
				catch (Exception ex) when (((ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
				{
					MessageBox.Show(this, "기본 썸네일로 프로젝트를 만들었습니다. 선택한 이미지는 적용하지 못했습니다: " + ex.Message, "썸네일 확인", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				}
			}
			_newThumbnailPath = null;
			SelectedProject = qaProjectDefinition2;
			base.DialogResult = true;
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is ArgumentException || ex2 is InvalidDataException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, ex2.Message, "QA 프로젝트 만들기 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}
}
