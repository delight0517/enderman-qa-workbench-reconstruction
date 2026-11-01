using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EndermanQa.Desktop.Integrations;

public class OnlyChatEmbeddedView : UserControl, IComponentConnector
{
	private readonly OnlyChatRuntimeLocator _runtime = new OnlyChatRuntimeLocator();

	private bool _connecting;

	private Uri? _trustedOrigin;

	internal TextBlock StatusText;

	internal WebView2 Browser;

	private bool _contentLoaded;

	public OnlyChatEmbeddedView()
	{
		InitializeComponent();
	}

	private async void Connect_Click(object sender, RoutedEventArgs e)
	{
		await ConnectAsync();
	}

	public async Task ConnectAsync()
	{
		if (_connecting)
		{
			return;
		}
		_connecting = true;
		try
		{
			StatusText.Text = "설치된 Only Chat 로컬 서버 확인 중…";
			Uri endpoint = await _runtime.ConnectAsync();
			string? configuredProfile = Environment.GetEnvironmentVariable("ENDERMAN_QA_WEBVIEW2_USER_DATA_ROOT");
			string text = string.IsNullOrWhiteSpace(configuredProfile)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndermanQaWorkbench", "webview2", "only-chat")
				: Path.GetFullPath(configuredProfile);
			Directory.CreateDirectory(text);
			CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, text);
			await Browser.EnsureCoreWebView2Async(environment);
			_trustedOrigin = endpoint;
			Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
			Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
			Browser.CoreWebView2.NavigationStarting -= OnNavigationStarting;
			Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
			Browser.CoreWebView2.NewWindowRequested -= OnNewWindowRequested;
			Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
			Browser.CoreWebView2.ProcessFailed -= OnProcessFailed;
			Browser.CoreWebView2.ProcessFailed += OnProcessFailed;
			Browser.Source = endpoint;
			Browser.Visibility = Visibility.Visible;
			StatusText.Text = $"Only Chat 1.0.5 로컬 채팅 화면에 연결됨 · {endpoint.Host}:{endpoint.Port}. Only Chat의 기존 프로필과 로그인 흐름을 사용하세요.";
		}
		catch (Exception ex)
		{
			StatusText.Text = "Only Chat 연결 실패 · " + ex.Message;
			MessageBox.Show(Window.GetWindow(this), ex.Message, "Only Chat 연결", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_connecting = false;
		}
	}

	public void Shutdown()
	{
		try
		{
			Browser.Dispose();
		}
		catch
		{
		}
	}

	private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
	{
		if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri result))
		{
			e.Cancel = true;
		}
		else
		{
			if ((object)_trustedOrigin != null && result.Scheme == Uri.UriSchemeHttp && result.Host == _trustedOrigin.Host && result.Port == _trustedOrigin.Port)
			{
				return;
			}
			if (result.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(result.UserInfo))
			{
				e.Cancel = true;
				try
				{
					Process.Start(new ProcessStartInfo(result.AbsoluteUri)
					{
						UseShellExecute = true
					});
					return;
				}
				catch
				{
					StatusText.Text = "로그인/외부 페이지를 기본 브라우저에서 열지 못했습니다.";
					return;
				}
			}
			e.Cancel = true;
			StatusText.Text = "보안 정책에 따라 Only Chat 로컬 화면과 HTTPS 로그인 주소만 허용됩니다.";
		}
	}

	private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
	{
		e.Handled = true;
		if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri result) && result.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(result.UserInfo))
		{
			try
			{
				Process.Start(new ProcessStartInfo(result.AbsoluteUri)
				{
					UseShellExecute = true
				});
			}
			catch
			{
				StatusText.Text = "기본 브라우저에서 링크를 열지 못했습니다.";
			}
		}
	}

	private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
	{
		StatusText.Text = $"Only Chat 웹 화면이 종료됨 · {e.ProcessFailedKind}. 연결 버튼으로 다시 로드하세요.";
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "integrations/onlychatembeddedview.baml");
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
			((Button)target).Click += Connect_Click;
			break;
		case 2:
			StatusText = (TextBlock)target;
			break;
		case 3:
			Browser = (WebView2)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
