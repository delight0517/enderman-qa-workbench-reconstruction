using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using EndermanQa.Desktop.Integrations;

namespace EndermanQa.Desktop.AiSetup;

public class AiRouterSetupWindow : Window, IComponentConnector
{
	private readonly AiAccountRouterClient _router = new AiAccountRouterClient(new HttpClient());

	private bool _busy;

	private bool _compatible;

	internal TextBlock RouterStatusText;

	internal TextBlock ProviderStatusText;

	internal ProgressBar BusyBar;

	internal TextBlock ActionNoticeText;

	internal Button StartRouterButton;

	internal Button DashboardButton;

	internal Button RefreshStatusButton;

	private bool _contentLoaded;

	public AiRouterSetupWindow()
	{
		InitializeComponent();
		base.Loaded += async delegate
		{
			await RefreshStatusAsync();
		};
	}

	private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
	{
		await RefreshStatusAsync();
	}

	private async Task RefreshStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (_busy)
		{
			return;
		}
		SetBusy(busy: true);
		try
		{
			RouterDiscoveryStatus routerDiscoveryStatus = await _router.ReadDiscoveryAsync(cancellationToken);
			_compatible = routerDiscoveryStatus.Connected && routerDiscoveryStatus.ApiCompatible;
			DashboardButton.IsEnabled = _compatible;
			RouterStatusText.Text = routerDiscoveryStatus.Message;
			if (!_compatible)
			{
				ProviderStatusText.Text = (routerDiscoveryStatus.Connected ? "공급자 상태는 확인할 수 없습니다. 지원되는 Router API v1이 필요합니다." : "공급자 설정 상태: Router 연결 전");
				return;
			}
			RouterConnectionStatus routerConnectionStatus = await _router.ReadStatusAsync(cancellationToken);
			ProviderStatusText.Text = (routerConnectionStatus.InferenceAvailable ? $"공급자 상태: 준비 프로필 {routerConnectionStatus.RedactedProfiles.Count}개 · 첫 실제 요청 전까지 키 유효성은 미확인" : ("공급자 상태: " + routerConnectionStatus.Message));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex2)
		{
			RouterStatusText.Text = "Router 상태 확인 실패: " + ex2.Message;
		}
		finally
		{
			if (base.IsLoaded)
			{
				SetBusy(busy: false);
			}
		}
	}

	private async void StartRouter_Click(object sender, RoutedEventArgs e)
	{
		if (_busy)
		{
			return;
		}
		SetBusy(busy: true);
		ActionNoticeText.Text = "";
		try
		{
			RouterDiscoveryStatus routerDiscoveryStatus = await _router.ReadDiscoveryAsync();
			if (routerDiscoveryStatus.Connected)
			{
				_compatible = routerDiscoveryStatus.ApiCompatible;
				DashboardButton.IsEnabled = _compatible;
				RouterStatusText.Text = routerDiscoveryStatus.Message;
				if (!_compatible)
				{
					ActionNoticeText.Text = "18880 포트에 다른 서비스가 응답했습니다. 중복으로 Router를 시작하지 않았습니다.";
				}
				else
				{
					ActionNoticeText.Text = "Router가 이미 실행 중입니다. 새 서버 프로세스는 시작하지 않았습니다.";
				}
				if (_compatible)
				{
					RouterConnectionStatus routerConnectionStatus = await _router.ReadStatusAsync();
					ProviderStatusText.Text = (routerConnectionStatus.InferenceAvailable ? $"공급자 상태: 준비 프로필 {routerConnectionStatus.RedactedProfiles.Count}개 · 첫 실제 요청 전까지 키 유효성은 미확인" : ("공급자 상태: " + routerConnectionStatus.Message));
				}
				return;
			}
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AI Account Router.lnk");
			if (!File.Exists(text))
			{
				ActionNoticeText.Text = "Router 바로가기를 찾지 못했습니다: " + text;
				return;
			}
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true
			});
			ActionNoticeText.Text = "지정된 Router 바로가기를 실행했습니다. 로컬 API 응답을 기다립니다…";
			DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(18.0);
			do
			{
				await Task.Delay(600);
				routerDiscoveryStatus = await _router.ReadDiscoveryAsync();
			}
			while (!routerDiscoveryStatus.Connected && DateTimeOffset.UtcNow < deadline);
			_compatible = routerDiscoveryStatus.Connected && routerDiscoveryStatus.ApiCompatible;
			DashboardButton.IsEnabled = _compatible;
			RouterStatusText.Text = routerDiscoveryStatus.Message;
			if (!_compatible)
			{
				ActionNoticeText.Text = (routerDiscoveryStatus.Connected ? "다른 서비스가 포트에 응답했습니다. Router를 추가 실행하지 않았습니다." : "18초 안에 Router API가 준비되지 않았습니다. Router 창과 포트 18880 상태를 확인하세요.");
				return;
			}
			RouterConnectionStatus routerConnectionStatus2 = await _router.ReadStatusAsync();
			ProviderStatusText.Text = (routerConnectionStatus2.InferenceAvailable ? $"공급자 상태: 준비 프로필 {routerConnectionStatus2.RedactedProfiles.Count}개 · 첫 실제 요청 전까지 키 유효성은 미확인" : ("공급자 상태: " + routerConnectionStatus2.Message));
			ActionNoticeText.Text = "Router가 준비됐습니다. 공급자 설정 열기를 눌러 프로필을 등록할 수 있습니다.";
		}
		catch (Exception ex)
		{
			ActionNoticeText.Text = "Router를 시작하지 못했습니다: " + ex.Message;
		}
		finally
		{
			if (base.IsLoaded)
			{
				SetBusy(busy: false);
			}
		}
	}

	private async void DashboardButton_Click(object sender, RoutedEventArgs e)
	{
		if (!_compatible || _busy)
		{
			return;
		}
		SetBusy(busy: true);
		try
		{
			RouterDiscoveryStatus routerDiscoveryStatus = await _router.ReadDiscoveryAsync();
			_compatible = routerDiscoveryStatus.Connected && routerDiscoveryStatus.ApiCompatible;
			DashboardButton.IsEnabled = _compatible;
			if (!_compatible)
			{
				RouterStatusText.Text = routerDiscoveryStatus.Message;
				ActionNoticeText.Text = "Router 연결이 바뀌어 설정 페이지를 열지 않았습니다. 상태를 새로 고쳐주세요.";
			}
			else
			{
				Process.Start(new ProcessStartInfo(AiAccountRouterClient.DashboardUri.AbsoluteUri)
				{
					UseShellExecute = true
				});
				ActionNoticeText.Text = "Router의 로컬 공급자 설정 페이지를 열었습니다. API 키는 Router에만 입력하세요.";
			}
		}
		catch (Exception ex)
		{
			ActionNoticeText.Text = "Router 설정 페이지를 열지 못했습니다: " + ex.Message;
		}
		finally
		{
			if (base.IsLoaded)
			{
				SetBusy(busy: false);
			}
		}
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		BusyBar.Visibility = ((!busy) ? Visibility.Collapsed : Visibility.Visible);
		StartRouterButton.IsEnabled = !busy;
		RefreshStatusButton.IsEnabled = !busy;
		DashboardButton.IsEnabled = !busy && _compatible;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "aisetup/airoutersetupwindow.baml");
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
			RouterStatusText = (TextBlock)target;
			break;
		case 2:
			ProviderStatusText = (TextBlock)target;
			break;
		case 3:
			BusyBar = (ProgressBar)target;
			break;
		case 4:
			ActionNoticeText = (TextBlock)target;
			break;
		case 5:
			StartRouterButton = (Button)target;
			StartRouterButton.Click += StartRouter_Click;
			break;
		case 6:
			DashboardButton = (Button)target;
			DashboardButton.Click += DashboardButton_Click;
			break;
		case 7:
			RefreshStatusButton = (Button)target;
			RefreshStatusButton.Click += RefreshStatus_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
