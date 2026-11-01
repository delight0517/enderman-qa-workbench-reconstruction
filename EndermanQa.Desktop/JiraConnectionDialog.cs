using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EndermanQa.Desktop.Integrations;

namespace EndermanQa.Desktop;

internal sealed class JiraConnectionDialog : Window
{
	private readonly TextBox _site = new TextBox
	{
		Text = "https://",
		Padding = new Thickness(7.0),
		Margin = new Thickness(0.0, 3.0, 0.0, 8.0)
	};

	private readonly TextBox _email = new TextBox
	{
		Padding = new Thickness(7.0),
		Margin = new Thickness(0.0, 3.0, 0.0, 8.0)
	};

	private readonly ComboBox _project = new ComboBox
	{
		IsEditable = true,
		Padding = new Thickness(5.0),
		Margin = new Thickness(0.0, 3.0, 0.0, 8.0)
	};

	private readonly ComboBox _issueType = new ComboBox
	{
		Padding = new Thickness(5.0),
		Margin = new Thickness(0.0, 3.0, 0.0, 8.0)
	};

	private readonly PasswordBox _token = new PasswordBox
	{
		Padding = new Thickness(7.0),
		Margin = new Thickness(0.0, 3.0, 0.0, 3.0)
	};

	private readonly TextBlock _tokenHint = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Brushes.SlateGray,
		Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
	};

	private readonly TextBlock _status = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(0.0, 5.0, 0.0, 5.0)
	};

	private readonly Button _save = new Button
	{
		Content = "DPAPI 보호 저장",
		Padding = new Thickness(12.0, 7.0, 12.0, 7.0),
		IsEnabled = false
	};

	private readonly JiraConnectionSettings? _existing;

	private bool _verified;

	public JiraConnectionSettings? Result { get; private set; }

	private JiraConnectionDialog(Window owner, JiraConnectionSettings? existing)
	{
		base.Owner = owner;
		_existing = existing;
		base.Title = "Jira Cloud 연결 설정";
		base.Width = 530.0;
		base.Height = 620.0;
		base.MinWidth = 500.0;
		base.MinHeight = 580.0;
		base.ResizeMode = ResizeMode.CanResize;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Background = new SolidColorBrush(Color.FromRgb(245, 247, 251));
		if ((object)existing != null)
		{
			_site.Text = existing.Site;
			_email.Text = existing.Email;
			_project.Text = existing.ProjectKey;
			_issueType.Text = existing.IssueType;
			_tokenHint.Text = "기존 API 토큰은 이 Windows 사용자 계정의 DPAPI로 보호되어 있습니다. 바꾸려면 새 토큰을 입력하세요.";
		}
		else
		{
			_tokenHint.Text = "API 토큰은 Jira Atlassian 계정에서 만든 뒤 이 입력창에 직접 넣으세요. 채팅이나 로그에는 토큰을 입력하지 마세요.";
		}
		_issueType.Items.Add("Task");
		_issueType.Items.Add("Workstream");
		_issueType.Items.Add("Bug");
		if (string.IsNullOrWhiteSpace(_issueType.Text))
		{
			_issueType.SelectedItem = "Task";
		}
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Jira Cloud 연결",
			FontSize = 22.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "계정 확인과 프로젝트 선택 후 연결 정보를 보호 저장합니다. 아직 프로젝트가 없다면 연결 정보만 저장하고, Jira에서 프로젝트를 만든 뒤 다시 프로젝트 목록을 불러오면 됩니다.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		});
		AddField(stackPanel, "사이트 주소 (예: https://team.atlassian.net)", _site);
		AddField(stackPanel, "계정 이메일", _email);
		AddField(stackPanel, "API 토큰", _token);
		stackPanel.Children.Add(_tokenHint);
		AddField(stackPanel, "Jira 프로젝트 키 (목록에서 선택하거나 입력)", _project);
		AddField(stackPanel, "이슈 유형 (기본 Bug; QA 미확정은 Task 권장)", _issueType);
		stackPanel.Children.Add(_status);
		WrapPanel wrapPanel = new WrapPanel
		{
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		Button button = new Button
		{
			Content = "연결 확인 / 프로젝트 불러오기",
			Padding = new Thickness(12.0, 7.0, 12.0, 7.0),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		Button element = new Button
		{
			Content = "취소",
			Padding = new Thickness(12.0, 7.0, 12.0, 7.0),
			IsCancel = true,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		_save.IsDefault = true;
		button.Click += async delegate
		{
			await VerifyAsync();
		};
		_save.Click += delegate
		{
			string text = CurrentToken();
			if (text == null)
			{
				SetStatus("API 토큰을 입력하세요.", false);
			}
			else
			{
				Result = new JiraConnectionSettings(_site.Text.Trim().TrimEnd('/'), _email.Text.Trim(), SelectedProjectKey(), text, _issueType.Text.Trim());
				base.DialogResult = true;
			}
		};
		wrapPanel.Children.Add(button);
		wrapPanel.Children.Add(element);
		wrapPanel.Children.Add(_save);
		stackPanel.Children.Add(wrapPanel);
		base.Content = stackPanel;
		_site.TextChanged += delegate
		{
			Invalidate();
		};
		_email.TextChanged += delegate
		{
			Invalidate();
		};
		_project.SelectionChanged += delegate
		{
			if (_verified)
			{
				_save.IsEnabled = true;
			}
		};
		_project.KeyUp += delegate
		{
			if (_verified)
			{
				_save.IsEnabled = true;
			}
		};
		_issueType.SelectionChanged += delegate
		{
			if (_verified)
			{
				_save.IsEnabled = true;
			}
		};
		_token.PasswordChanged += delegate
		{
			Invalidate();
		};
	}

	public static JiraConnectionSettings? Show(Window owner, JiraConnectionSettings? existing)
	{
		JiraConnectionDialog jiraConnectionDialog = new JiraConnectionDialog(owner, existing);
		if (!jiraConnectionDialog.ShowDialog().GetValueOrDefault())
		{
			return null;
		}
		return jiraConnectionDialog.Result;
	}

	private async Task VerifyAsync()
	{
		_verified = false;
		_save.IsEnabled = false;
		if (!Uri.TryCreate(_site.Text.Trim(), UriKind.Absolute, out Uri result) || result.Scheme != Uri.UriSchemeHttps || !result.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase))
		{
			SetStatus("https://사이트이름.atlassian.net 형식의 Jira Cloud 주소를 확인하세요.", false);
			return;
		}
		string text = CurrentToken();
		if (string.IsNullOrWhiteSpace(_email.Text) || string.IsNullOrWhiteSpace(text))
		{
			SetStatus("계정 이메일과 API 토큰을 입력하세요.", false);
			return;
		}
		try
		{
			SetStatus("Jira 계정 및 접근 가능한 프로젝트를 확인 중…", null);
			using HttpClient http = new HttpClient(new HttpClientHandler
			{
				AllowAutoRedirect = false
			})
			{
				Timeout = TimeSpan.FromSeconds(20.0)
			};
			IReadOnlyList<JiraCloudClient.JiraProject> readOnlyList = await new JiraCloudClient(http).ListProjectsAsync(result, _email.Text.Trim(), text);
			string previous = _project.Text.Trim();
			_project.Items.Clear();
			foreach (JiraCloudClient.JiraProject item in readOnlyList)
			{
				_project.Items.Add(item.Key + " — " + item.Name);
			}
			JiraCloudClient.JiraProject jiraProject = readOnlyList.FirstOrDefault((JiraCloudClient.JiraProject x) => x.Key.Equals(previous, StringComparison.OrdinalIgnoreCase));
			if ((object)jiraProject != null)
			{
				_project.SelectedItem = jiraProject.Key + " — " + jiraProject.Name;
			}
			else
			{
				_project.Text = previous;
			}
			_verified = true;
			_save.IsEnabled = true;
			SetStatus((readOnlyList.Count == 0) ? "계정 연결은 확인됐지만 접근 가능한 Jira 프로젝트가 없습니다. 연결 정보만 저장할 수 있습니다. 프로젝트 생성/권한 부여 후 목록을 다시 불러오세요." : $"연결 확인 완료 · 프로젝트 {readOnlyList.Count}개 조회 · 프로젝트를 선택하고 보호 저장하세요.", true);
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is ArgumentException || ex is JsonException) ? 1 : 0) != 0)
		{
			SetStatus("연결 확인 실패: " + ex.Message, false);
		}
	}

	private string? CurrentToken()
	{
		if (!string.IsNullOrEmpty(_token.Password))
		{
			return _token.Password;
		}
		return _existing?.ApiToken;
	}

	private string SelectedProjectKey()
	{
		string text = (_project.SelectedItem as string) ?? _project.Text;
		int num = text.IndexOf(" — ", StringComparison.Ordinal);
		return ((num >= 0) ? text.Substring(0, num) : text).Trim();
	}

	private void Invalidate()
	{
		_verified = false;
		_save.IsEnabled = false;
	}

	private void SetStatus(string text, bool? success)
	{
		_status.Text = text;
		_status.Foreground = (success.GetValueOrDefault() ? Brushes.DarkGreen : ((success == false) ? Brushes.Firebrick : Brushes.DarkSlateBlue));
	}

	private static void AddField(Panel panel, string label, Control control)
	{
		panel.Children.Add(new TextBlock
		{
			Text = label
		});
		panel.Children.Add(control);
	}
}
