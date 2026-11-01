using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EndermanQa.Desktop.Integrations;
using EndermanQa.Desktop.Persistence;
using Microsoft.Win32;

namespace EndermanQa.Desktop.Tools;

internal sealed class OnlyChatWorkDialog : Window
{
	private readonly QaProjectDefinition _project;

	private readonly OnlyChatManagerClient _client = new OnlyChatManagerClient();

	private readonly TextBox _objective = new TextBox
	{
		AcceptsReturn = true,
		TextWrapping = TextWrapping.Wrap,
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		MinHeight = 108.0,
		Padding = new Thickness(9.0)
	};

	private readonly TextBox _workspace = new TextBox
	{
		Padding = new Thickness(8.0)
	};

	private readonly ComboBox _profiles = new ComboBox
	{
		Padding = new Thickness(7.0)
	};

	private readonly ComboBox _access = new ComboBox
	{
		Padding = new Thickness(7.0),
		SelectedIndex = 0
	};

	private readonly TextBox _duration = new TextBox
	{
		Text = "900",
		Width = 90.0,
		Padding = new Thickness(7.0)
	};

	private readonly TextBlock _connection = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Brush("Theme.TextSecondary")
	};

	private readonly TextBlock _runState = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Brush("Theme.TextStrong")
	};

	private readonly Button _start = Button("Only Chat으로 작업 보내기", primary: true);

	private readonly Button _stop = Button("진행 중인 작업 중지");

	private readonly DispatcherTimer _poll = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(3.0)
	};

	private OnlyChatManagerClient.ManagerStatus? _status;

	private OnlyChatManagerClient.RunReceipt? _receipt;

	private readonly OnlyChatRunStore _runStore;

	private bool _busy;

	public OnlyChatWorkDialog(Window owner, QaProjectDefinition project, string taskTitle, string target, string prompt, string workspacePath)
	{
		base.Owner = owner;
		_project = project;
		_runStore = new OnlyChatRunStore(project.DataRoot);
		base.Title = "Only Chat 작업 전송";
		base.Width = 820.0;
		base.Height = 760.0;
		base.MinWidth = 680.0;
		base.MinHeight = 650.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Background = Brush("Theme.Canvas");
		base.Foreground = Brush("Theme.TextPrimary");
		base.FontFamily = new FontFamily("Segoe UI");
		_workspace.Text = (Directory.Exists(workspacePath) ? workspacePath : project.RootPath);
		_objective.Text = $"Workbench 프로젝트: {project.Name}\n게임/앱 대상: {project.GameTarget ?? target}\n요청 작업: {taskTitle}\n\n{prompt}";
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(20.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Only Chat AI 작업",
			FontSize = 23.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = Brush("Theme.TextStrong")
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "현재 설치된 Only Chat의 loopback Manager API를 통해 선택한 프로젝트 폴더에 작업을 요청합니다. 프로필 연결 상태는 시작 시 Only Chat이 다시 검사합니다.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("Theme.TextSecondary"),
			Margin = new Thickness(0.0, 4.0, 0.0, 14.0)
		});
		AddField(stackPanel, "작업 지시", _objective);
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 7.0, 0.0, 2.0)
		};
		Button button = Button("폴더 선택");
		button.Click += ChooseWorkspace;
		DockPanel.SetDock(button, Dock.Right);
		dockPanel.Children.Add(button);
		dockPanel.Children.Add(_workspace);
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Only Chat이 접근할 프로젝트 폴더",
			Foreground = Brush("Theme.TextMuted")
		});
		stackPanel.Children.Add(dockPanel);
		AddField(stackPanel, "Only Chat Codex 프로필", _profiles);
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 2.0, 0.0, 5.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		StackPanel stackPanel2 = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 12.0, 0.0)
		};
		stackPanel2.Children.Add(new TextBlock
		{
			Text = "파일 접근 범위",
			Foreground = Brush("Theme.TextMuted")
		});
		_access.Items.Add(new ComboBoxItem
		{
			Content = "읽기 전용",
			Tag = "read-only"
		});
		_access.Items.Add(new ComboBoxItem
		{
			Content = "선택 폴더 안에서 파일 수정",
			Tag = "workspace-write"
		});
		stackPanel2.Children.Add(_access);
		grid.Children.Add(stackPanel2);
		StackPanel stackPanel3 = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 12.0, 0.0)
		};
		stackPanel3.Children.Add(new TextBlock
		{
			Text = "실제 작업 한도(초)",
			Foreground = Brush("Theme.TextMuted")
		});
		stackPanel3.Children.Add(_duration);
		Grid.SetColumn(stackPanel3, 1);
		grid.Children.Add(stackPanel3);
		Button button2 = Button("Only Chat 연결 새로고침");
		button2.Click += async delegate
		{
			await RefreshAsync();
		};
		Grid.SetColumn(button2, 2);
		button2.VerticalAlignment = VerticalAlignment.Bottom;
		grid.Children.Add(button2);
		stackPanel.Children.Add(grid);
		stackPanel.Children.Add(_connection);
		Border border = new Border
		{
			Background = Brush("Theme.AccentMuted"),
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(9.0),
			Margin = new Thickness(0.0, 9.0, 0.0, 8.0)
		};
		border.Child = new TextBlock
		{
			Text = "전송하면 작업 지시와 선택한 폴더 경로가 Only Chat으로 전달됩니다. 읽기 전용이 기본값입니다. ‘선택 폴더 안에서 파일 수정’을 고르면 Only Chat이 해당 폴더의 파일을 수정할 수 있습니다. 다른 실행 중 작업이 있으면 전송하지 않습니다.",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("Theme.TextPrimary")
		};
		stackPanel.Children.Add(border);
		_runState.Margin = new Thickness(0.0, 2.0, 0.0, 9.0);
		stackPanel.Children.Add(_runState);
		WrapPanel wrapPanel = new WrapPanel
		{
			HorizontalAlignment = HorizontalAlignment.Right
		};
		Button button3 = Button("닫기");
		button3.Click += delegate
		{
			Close();
		};
		_stop.IsEnabled = false;
		_stop.Click += async delegate
		{
			await StopAsync();
		};
		_start.Click += async delegate
		{
			await StartAsync();
		};
		wrapPanel.Children.Add(_stop);
		wrapPanel.Children.Add(_start);
		wrapPanel.Children.Add(button3);
		stackPanel.Children.Add(wrapPanel);
		base.Content = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = stackPanel
		};
		base.Loaded += async delegate
		{
			await RefreshAsync();
		};
		_poll.Tick += async delegate
		{
			await PollRunAsync();
		};
		base.Closed += delegate
		{
			_poll.Stop();
			_client.Dispose();
		};
	}

	private async Task RefreshAsync()
	{
		if (_busy)
		{
			return;
		}
		_busy = true;
		UpdateButtons();
		_connection.Text = "Only Chat runtime 확인 중…";
		try
		{
			_status = await _client.GetStatusAsync();
			OnlyChatManagerClient.ProviderProfile[] source = _status.Profiles.Where(delegate(OnlyChatManagerClient.ProviderProfile x)
			{
				bool flag3 = x.Enabled && !x.CoolingDown;
				if (flag3)
				{
					bool flag4;
					switch (x.Provider)
					{
					case "codex-cli":
					case "codex":
					case "chatgpt-codex":
						flag4 = true;
						break;
					default:
						flag4 = false;
						break;
					}
					flag3 = flag4;
				}
				return flag3;
			}).ToArray();
			_profiles.ItemsSource = source.Select((OnlyChatManagerClient.ProviderProfile x) => $"{x.Name} · {x.Provider} · {x.Model}").ToArray();
			if (_profiles.Items.Count > 0 && _profiles.SelectedIndex < 0)
			{
				_profiles.SelectedIndex = 0;
			}
			_connection.Text = $"Only Chat {_status.Version ?? "version unknown"} · Manager: {_status.State ?? "unavailable"} · 사용 가능 프로필 설정 {_status.Profiles.Count}개 · 현재 진행 중 작업 {_status.ActiveRunCount}개\n{_status.Message}";
			if (!_status.Profiles.Any(delegate(OnlyChatManagerClient.ProviderProfile x)
			{
				bool flag = x.Enabled;
				if (flag)
				{
					bool flag2;
					switch (x.Provider)
					{
					case "codex-cli":
					case "codex":
					case "chatgpt-codex":
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					flag = flag2;
				}
				return flag;
			}))
			{
				_connection.Text += "\n작업 시작에 필요한 활성 Codex 프로필이 없습니다.";
			}
			UpdateButtons();
		}
		catch (Exception ex) when (((ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException) ? 1 : 0) != 0)
		{
			_status = null;
			_profiles.ItemsSource = Array.Empty<string>();
			_connection.Text = "Only Chat에 연결되지 않았습니다. " + ex.Message;
			UpdateButtons();
		}
		finally
		{
			_busy = false;
			UpdateButtons();
		}
	}

	private async Task StartAsync()
	{
		if (_busy || (object)_status == null || _profiles.SelectedIndex < 0 || string.IsNullOrWhiteSpace(_objective.Text))
		{
			return;
		}
		OnlyChatManagerClient.ProviderProfile profile = _status.Profiles.Where(delegate(OnlyChatManagerClient.ProviderProfile x)
		{
			bool flag3 = x.Enabled && !x.CoolingDown;
			if (flag3)
			{
				bool flag4;
				switch (x.Provider)
				{
				case "codex-cli":
				case "codex":
				case "chatgpt-codex":
					flag4 = true;
					break;
				default:
					flag4 = false;
					break;
				}
				flag3 = flag4;
			}
			return flag3;
		}).ElementAtOrDefault(_profiles.SelectedIndex);
		if ((object)profile == null)
		{
			await RefreshAsync();
			return;
		}
		int result;
		bool flag = !int.TryParse(_duration.Text, out result);
		if (!flag)
		{
			bool flag2 = ((result < 1 || result > 604800) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			MessageBox.Show(this, "작업 한도는 1~604800초여야 합니다.", "시간 값 확인", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		string fullPath = Path.GetFullPath(_workspace.Text.Trim());
		string access = (_access.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "read-only";
		if (!Directory.Exists(fullPath))
		{
			MessageBox.Show(this, "작업 폴더를 먼저 선택하세요.", "작업 폴더 확인", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			if (MessageBox.Show(this, $"Only Chat으로 작업을 보낼까요?\n\n프로필: {profile.Name}\n프로젝트: {_project.Name}\n접근 폴더: {fullPath}\n파일 권한: {((access == "read-only") ? "읽기 전용" : "이 폴더 안에서 파일 수정")}\n실제 작업 한도: {result}초\n\nOnly Chat이 현재 프로필 인증을 확인한 뒤 실제 AI 작업을 시작합니다.", "Only Chat 작업 전송 확인", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
			{
				return;
			}
			_busy = true;
			UpdateButtons();
			_runState.Text = "Only Chat에 요청을 전달하고 있습니다…";
			try
			{
				OnlyChatManagerClient.RunReceipt runReceipt = (_receipt = await _client.StartRunAsync(profile.Id, _objective.Text, fullPath, result, access));
				try
				{
					_runStore.Save(runReceipt, _project.ProjectId, _objective.Text, profile.Id, profile.Name, access);
				}
				catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) ? 1 : 0) != 0)
				{
					_runState.Text = "Only Chat 작업은 시작됐지만 프로젝트 기록 저장에 실패했습니다. 실행 ID: " + runReceipt.RunId + "\n" + ex.Message;
				}
				if (string.IsNullOrWhiteSpace(_runState.Text) || !_runState.Text.Contains(runReceipt.RunId, StringComparison.Ordinal))
				{
					_runState.Text = $"Only Chat 작업 시작됨 · {runReceipt.Status}\n실행 ID: {runReceipt.RunId}\n실제 파일 변경은 선택한 작업 폴더에서 확인하세요. 이 창을 닫아도 Only Chat 실행은 계속됩니다.";
				}
				_poll.Start();
				_stop.IsEnabled = true;
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is InvalidDataException || ex2 is UnauthorizedAccessException || ex2 is InvalidOperationException || ex2 is ArgumentException || ex2 is HttpRequestException || ex2 is TaskCanceledException || ex2 is JsonException) ? 1 : 0) != 0)
			{
				_runState.Text = "Only Chat이 작업을 시작하지 않았습니다: " + ex2.Message;
				await RefreshAsync();
			}
			finally
			{
				_busy = false;
				UpdateButtons();
			}
		}
	}

	private async Task PollRunAsync()
	{
		if (_busy || (object)_receipt == null)
		{
			return;
		}
		try
		{
			OnlyChatManagerClient.RunSnapshot runSnapshot = await _client.GetRunAsync(_receipt.RunId);
			if ((object)runSnapshot == null)
			{
				_runState.Text = "실행 상태를 찾을 수 없습니다. Only Chat에서 확인하세요. 실행 ID: " + _receipt.RunId;
				_poll.Stop();
				return;
			}
			_runState.Text = $"Only Chat 작업 상태: {runSnapshot.Status}\n{runSnapshot.Progress ?? "최근 진행 정보 없음"}{(string.IsNullOrWhiteSpace(runSnapshot.LastError) ? "" : ("\n확인 필요: " + runSnapshot.LastError))}\n실행 ID: {runSnapshot.RunId}";
			_runStore.UpdateStatus(runSnapshot.RunId, runSnapshot.Status, runSnapshot.Progress);
			bool flag;
			switch (runSnapshot.Status)
			{
			case "queued":
			case "running":
			case "stopping":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				_poll.Stop();
				_stop.IsEnabled = false;
				await RefreshAsync();
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is InvalidDataException || ex is InvalidOperationException || ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException) ? 1 : 0) != 0)
		{
			_runState.Text = "Only Chat 상태 확인을 재시도합니다: " + ex.Message;
		}
	}

	private async Task StopAsync()
	{
		if (_busy || (object)_receipt == null)
		{
			return;
		}
		_busy = true;
		UpdateButtons();
		try
		{
			await _client.StopRunAsync(_receipt.RunId);
			_runState.Text = "중지 요청을 Only Chat에 보냈습니다. 안전하게 멈추는 중입니다…";
			_runStore.UpdateStatus(_receipt.RunId, "stopping", "중지 요청됨");
		}
		catch (Exception ex) when (((ex is IOException || ex is InvalidOperationException || ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException) ? 1 : 0) != 0)
		{
			_runState.Text = "중지 요청 실패: " + ex.Message;
		}
		finally
		{
			_busy = false;
			UpdateButtons();
		}
	}

	private void ChooseWorkspace(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Only Chat이 작업할 프로젝트 또는 게임 폴더 선택",
			Multiselect = false,
			InitialDirectory = (Directory.Exists(_workspace.Text) ? _workspace.Text : _project.RootPath)
		};
		if (openFolderDialog.ShowDialog(this).GetValueOrDefault())
		{
			_workspace.Text = openFolderDialog.FolderName;
			UpdateButtons();
		}
	}

	private void UpdateButtons()
	{
		int num;
		if (_profiles.SelectedIndex >= 0)
		{
			OnlyChatManagerClient.ManagerStatus? status = _status;
			if ((object)status != null && status.ActiveRunCount == 0)
			{
				num = ((_status?.State == "active") ? 1 : 0);
				goto IL_0044;
			}
		}
		num = 0;
		goto IL_0044;
		IL_0044:
		bool flag = (byte)num != 0;
		_start.IsEnabled = !_busy && flag && Directory.Exists(_workspace.Text) && !string.IsNullOrWhiteSpace(_objective.Text);
		_stop.IsEnabled = !_busy && (object)_receipt != null;
	}

	private static void AddField(Panel panel, string label, Control control)
	{
		panel.Children.Add(new TextBlock
		{
			Text = label,
			Foreground = Brush("Theme.TextMuted"),
			Margin = new Thickness(0.0, 5.0, 0.0, 1.0)
		});
		panel.Children.Add(control);
	}

	private static Button Button(string text, bool primary = false)
	{
		return new Button
		{
			Content = text,
			Padding = new Thickness(10.0, 7.0, 10.0, 7.0),
			Margin = new Thickness(4.0, 0.0, 0.0, 0.0),
			Background = (primary ? Brush("Theme.Accent") : Brush("Theme.SurfaceInset")),
			Foreground = (primary ? Brushes.White : Brush("Theme.TextStrong")),
			BorderThickness = (primary ? new Thickness(0.0) : new Thickness(1.0)),
			Cursor = Cursors.Hand
		};
	}

	private static Brush Brush(string key)
	{
		return (Application.Current.TryFindResource(key) as Brush) ?? Brushes.White;
	}
}
