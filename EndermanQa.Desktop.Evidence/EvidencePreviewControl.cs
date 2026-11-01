using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace EndermanQa.Desktop.Evidence;

public class EvidencePreviewControl : UserControl, IComponentConnector
{
	private const long MaximumPreviewBytes = 67108864L;

	private CancellationTokenSource? _loadWork;

	internal TextBlock StatusText;

	internal Image PreviewImage;

	internal MediaElement PreviewVideo;

	internal TextBox PreviewText;

	internal Button PlayButton;

	internal Button PauseButton;

	internal Button StopButton;

	private bool _contentLoaded;

	public EvidencePreviewControl()
	{
		InitializeComponent();
	}

	public async Task ShowEvidenceAsync(string path, string mediaType, string expectedSha256, CancellationToken cancellationToken = default(CancellationToken))
	{
		_loadWork?.Cancel();
		_loadWork?.Dispose();
		_loadWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationTokenSource work = _loadWork;
		ResetPreview();
		try
		{
			string fullPath = Path.GetFullPath(path);
			FileInfo info = new FileInfo(fullPath);
			if (!info.Exists)
			{
				throw new FileNotFoundException("증거 파일이 없습니다.", fullPath);
			}
			if (info.Length > 67108864)
			{
				throw new InvalidDataException("미리보기는 64 MB 이하 파일만 지원합니다. 원본 파일은 QA 기록에 유지됩니다.");
			}
			string text = await Task.Run(async delegate
			{
				FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
				string result;
				try
				{
					result = Convert.ToHexString(await SHA256.HashDataAsync(stream, work.Token).ConfigureAwait(continueOnCapturedContext: false));
				}
				finally
				{
					if (stream != null)
					{
						await stream.DisposeAsync();
					}
				}
				return result;
			}, work.Token);
			if (work.IsCancellationRequested || _loadWork != work)
			{
				return;
			}
			if (!string.Equals(text, expectedSha256, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("저장된 증거의 SHA-256이 기록과 다릅니다. 변경된 파일은 미리보기를 중단했습니다.");
			}
			string text2 = Path.GetExtension(fullPath).ToLowerInvariant();
			StatusText.Text = $"{Path.GetFileName(fullPath)} · {info.Length:N0} bytes · SHA-256 {text.Substring(0, 12)}…";
			bool flag = mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
			if (!flag)
			{
				bool flag2;
				switch (text2)
				{
				case ".png":
				case ".jpg":
				case ".jpeg":
				case ".webp":
				case ".gif":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag)
			{
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.DecodePixelWidth = 1600;
				bitmapImage.UriSource = new Uri(fullPath, UriKind.Absolute);
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				PreviewImage.Source = bitmapImage;
				PreviewImage.Visibility = Visibility.Visible;
				StatusText.Text = "이미지 확인됨 · " + StatusText.Text;
				return;
			}
			flag = mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
			if (!flag)
			{
				bool flag2;
				switch (text2)
				{
				case ".mp4":
				case ".mkv":
				case ".webm":
				case ".mov":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag)
			{
				PreviewVideo.Source = new Uri(fullPath, UriKind.Absolute);
				PreviewVideo.Visibility = Visibility.Visible;
				Button playButton = PlayButton;
				Button pauseButton = PauseButton;
				bool flag2 = (StopButton.IsEnabled = true);
				flag = (pauseButton.IsEnabled = flag2);
				playButton.IsEnabled = flag;
				StatusText.Text = "영상 준비됨 · 재생을 눌러 확인하세요. " + StatusText.Text;
				return;
			}
			switch (text2)
			{
			case ".txt":
			case ".json":
			case ".log":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				TextBox previewText = PreviewText;
				previewText.Text = await File.ReadAllTextAsync(fullPath, work.Token);
				if (!work.IsCancellationRequested && _loadWork == work)
				{
					PreviewText.Visibility = Visibility.Visible;
					StatusText.Text = "텍스트 증거 확인됨 · " + StatusText.Text;
				}
			}
			else
			{
				StatusText.Text = "무결성 확인 완료 · 미리보기 미지원 형식입니다. " + StatusText.Text;
			}
		}
		catch (OperationCanceledException) when (work.IsCancellationRequested)
		{
		}
		catch (Exception ex2) when (!(ex2 is OutOfMemoryException) && !(ex2 is StackOverflowException))
		{
			if (_loadWork == work)
			{
				StatusText.Text = "증거 미리보기 중단 · " + ex2.Message;
			}
		}
	}

	public void ShowUnavailable(string message)
	{
		_loadWork?.Cancel();
		ResetPreview();
		StatusText.Text = message;
	}

	public void ClearPreview()
	{
		_loadWork?.Cancel();
		ResetPreview();
		StatusText.Text = "수동 QA 기록에서 이미지·영상·로그를 선택하세요.";
	}

	private void ResetPreview()
	{
		PreviewVideo.Stop();
		PreviewVideo.Source = null;
		PreviewVideo.Visibility = Visibility.Collapsed;
		PreviewImage.Source = null;
		PreviewImage.Visibility = Visibility.Collapsed;
		PreviewText.Clear();
		PreviewText.Visibility = Visibility.Collapsed;
		Button playButton = PlayButton;
		Button pauseButton = PauseButton;
		bool flag2 = (StopButton.IsEnabled = false);
		bool isEnabled = (pauseButton.IsEnabled = flag2);
		playButton.IsEnabled = isEnabled;
		StatusText.Text = "증거 파일을 확인하는 중…";
	}

	private void PlayButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			PreviewVideo.Play();
		}
		catch (InvalidOperationException ex)
		{
			StatusText.Text = "영상 재생 실패 · " + ex.Message;
		}
	}

	private void PauseButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			PreviewVideo.Pause();
		}
		catch (InvalidOperationException ex)
		{
			StatusText.Text = "영상 일시정지 실패 · " + ex.Message;
		}
	}

	private void StopButton_Click(object sender, RoutedEventArgs e)
	{
		PreviewVideo.Stop();
	}

	private void PreviewVideo_Unloaded(object sender, RoutedEventArgs e)
	{
		try
		{
			PreviewVideo.Stop();
		}
		catch (InvalidOperationException)
		{
		}
	}

	private void PreviewVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
	{
		StatusText.Text = "현재 Windows 미디어 코덱에서 영상을 열 수 없습니다 · " + e.ErrorException.Message;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "evidence/evidencepreviewcontrol.baml");
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
			StatusText = (TextBlock)target;
			break;
		case 2:
			PreviewImage = (Image)target;
			break;
		case 3:
			PreviewVideo = (MediaElement)target;
			PreviewVideo.MediaFailed += PreviewVideo_MediaFailed;
			PreviewVideo.Unloaded += PreviewVideo_Unloaded;
			break;
		case 4:
			PreviewText = (TextBox)target;
			break;
		case 5:
			PlayButton = (Button)target;
			PlayButton.Click += PlayButton_Click;
			break;
		case 6:
			PauseButton = (Button)target;
			PauseButton.Click += PauseButton_Click;
			break;
		case 7:
			StopButton = (Button)target;
			StopButton.Click += StopButton_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
