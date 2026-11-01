using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EndermanQa.Desktop.Modules;

public class ModuleManagerView : UserControl, IComponentConnector
{
	private readonly ModuleHostV1<TextFileModuleV1> _host = new ModuleHostV1<TextFileModuleV1>();

	private string? _candidatePath;

	private string? _candidateHash;

	private string? _sourcePath;

	private string? _projectRoot;

	private string _appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndermanQaWorkbench");

	private (string Path, string Hash, string Version)? _activeSnapshot;

	private (string Path, string Hash)? _previous;

	private FileSystemWatcher? _watcher;

	private readonly DispatcherTimer _debounce = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(650.0)
	};

	private long _watchGeneration;

	private long _pendingWatchGeneration;

	private bool _busy;

	private LiveThemeModuleV1? _liveTheme;

	internal TextBlock ThemeModuleStatus;

	internal Button ThemeWatchButton;

	internal Button WatchButton;

	internal TextBox ContentBox;

	internal TextBlock StatusText;

	private bool _contentLoaded;

	public ModuleManagerView()
	{
		InitializeComponent();
		_debounce.Tick += Debounce_Tick;
		base.IsVisibleChanged += delegate
		{
			if (base.IsVisible)
			{
				RefreshAutoReview();
			}
		};
		base.Unloaded += delegate
		{
			StopWatcher("화면 닫힘 · 감시 중지");
		};
	}

	public void SetProjectRoot(string path)
	{
		StopWatcher("프로젝트 변경 · 감시 중지");
		_projectRoot = Path.GetFullPath(path);
		_candidatePath = null;
		_candidateHash = null;
		_sourcePath = null;
		ContentBox.Clear();
		StatusText.Text = "프로젝트 변경됨 · 자동 생성 SOP 검토 파일을 불러오는 중";
		RefreshAutoReview(path);
	}

	public void SetAppDataRoot(string path)
	{
		_appDataRoot = Path.GetFullPath(path);
	}

	public void SetLiveTheme(LiveThemeModuleV1 runtime)
	{
		if (_liveTheme != null)
		{
			_liveTheme.StatusChanged -= ThemeStatusChanged;
		}
		_liveTheme = runtime;
		_liveTheme.StatusChanged += ThemeStatusChanged;
		ThemeStatusChanged(runtime.Status);
	}

	public void ClearLiveTheme()
	{
		if (_liveTheme != null)
		{
			_liveTheme.StatusChanged -= ThemeStatusChanged;
		}
		_liveTheme = null;
	}

	public void ShowThemeError(string message)
	{
		ThemeModuleStatus.Text = message;
	}

	private void ThemeStatusChanged(string status)
	{
		string status2 = status;
		if (!base.Dispatcher.CheckAccess())
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				ThemeStatusChanged(status2);
			});
			return;
		}
		ThemeModuleStatus.Text = status2;
		Button themeWatchButton = ThemeWatchButton;
		LiveThemeModuleV1? liveTheme = _liveTheme;
		themeWatchButton.Content = ((liveTheme != null && liveTheme.IsWatching) ? "실시간 감시 중지" : "실시간 감시 시작");
	}

	private void ThemeWatch_Click(object sender, RoutedEventArgs e)
	{
		if (_liveTheme == null)
		{
			ShowThemeError("테마 모듈이 준비되지 않았습니다.");
			return;
		}
		try
		{
			if (_liveTheme.IsWatching)
			{
				_liveTheme.StopWatching();
			}
			else
			{
				_liveTheme.StartWatching();
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) ? 1 : 0) != 0)
		{
			ShowThemeError("감시 상태 변경 실패 · " + ex.Message);
		}
	}

	private void ThemeRollback_Click(object sender, RoutedEventArgs e)
	{
		if (_liveTheme == null || !_liveTheme.Rollback())
		{
			ShowThemeError(_liveTheme?.Status ?? "테마 모듈이 준비되지 않았습니다.");
		}
	}

	private void OpenThemeFolder_Click(object sender, RoutedEventArgs e)
	{
		if (_liveTheme == null)
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetDirectoryName(_liveTheme.SourcePath) + "\"")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex) when (((ex is InvalidOperationException || ex is Win32Exception) ? 1 : 0) != 0)
		{
			ShowThemeError("테마 폴더를 열지 못했습니다 · " + ex.Message);
		}
	}

	private void RefreshAutoReview()
	{
		RefreshAutoReview(null);
	}

	private void RefreshAutoReview(string? rootOverride)
	{
		if (!_busy && !string.IsNullOrWhiteSpace(rootOverride))
		{
			string path = Path.Combine(rootOverride, "suite-runs");
			string text = (Directory.Exists(path) ? Directory.GetDirectories(path).SelectMany((string d) => new string[1] { Path.Combine(d, "SOP-REVIEW.md") }).Where(File.Exists)
				.OrderByDescending(File.GetLastWriteTimeUtc)
				.FirstOrDefault() : null);
			if (text != null)
			{
				LoadCandidate(text);
			}
			else
			{
				StatusText.Text = "현재 프로젝트에 SOP 검토 파일이 없습니다. QA 실행 결과가 저장되면 자동 생성됩니다.";
			}
		}
	}

	private void Choose_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "SOP review (*.md)|*.md|Text files (*.txt)|*.txt",
			CheckFileExists = true
		};
		if (openFileDialog.ShowDialog().GetValueOrDefault())
		{
			LoadCandidate(openFileDialog.FileName);
		}
	}

	private void LoadCandidate(string path)
	{
		string fullPath = Path.GetFullPath(path);
		if (_projectRoot == null || !IsUnderRoot(fullPath, _projectRoot))
		{
			StatusText.Text = "적용 거부 · 선택 파일은 현재 QA 프로젝트 폴더 안에 있어야 합니다.";
			return;
		}
		byte[] array = File.ReadAllBytes(fullPath);
		if (array.Length > 1048576)
		{
			StatusText.Text = "1 MiB를 넘는 파일은 모듈로 적용할 수 없습니다.";
			return;
		}
		if (((IEnumerable<byte>)array).Contains((byte)0))
		{
			StatusText.Text = "적용 거부 · 텍스트 파일에 NUL 문자가 있습니다.";
			return;
		}
		StopWatcher("후보 파일 변경 · 감시 중지");
		_sourcePath = fullPath;
		string text = (_candidateHash = Convert.ToHexString(SHA256.HashData(array)).ToLowerInvariant());
		_candidatePath = SaveImmutableSnapshot(array, text);
		ContentBox.Text = Encoding.UTF8.GetString(array);
		StatusText.Text = "검토 대기 · " + Path.GetFileName(fullPath) + " · SHA-256 " + text;
	}

	private void Apply_Click(object sender, RoutedEventArgs e)
	{
		ApplyCandidate(automatic: false);
	}

	private void ApplyCandidate(bool automatic)
	{
		if (_busy || _candidatePath == null || _candidateHash == null)
		{
			return;
		}
		_busy = true;
		try
		{
			string text = "content-" + _candidateHash.Substring(0, 12);
			if (_activeSnapshot?.Hash == _candidateHash)
			{
				if (automatic)
				{
					StatusText.Text = "변경 감지 · 내용 동일 · 현재 모듈 유지 · " + text;
				}
				return;
			}
			TextFileModuleFactoryV1 textFileModuleFactoryV = new TextFileModuleFactoryV1(_candidatePath, text);
			if (!_host.Apply(textFileModuleFactoryV, textFileModuleFactoryV.ModuleId, textFileModuleFactoryV.Version, _candidateHash))
			{
				StatusText.Text = "적용 거부 · " + _host.LastError;
				return;
			}
			TextFileModuleV1 active = _host.Active;
			if (active != null)
			{
				(string, string, string)? activeSnapshot = _activeSnapshot;
				if (activeSnapshot.HasValue)
				{
					(string, string, string) valueOrDefault = activeSnapshot.GetValueOrDefault();
					_previous = (valueOrDefault.Item1, valueOrDefault.Item2);
				}
				_activeSnapshot = (_candidatePath, active.SourceHash, text);
				ContentBox.Text = active.Content;
				StatusText.Text = $"{(automatic ? "실시간 적용됨" : "적용됨")} · {active.ModuleId} v{text} · SHA-256 {active.SourceHash} · SOP 문서 모듈";
			}
		}
		finally
		{
			_busy = false;
		}
	}

	private void Watch_Click(object sender, RoutedEventArgs e)
	{
		if (_watcher != null)
		{
			StopWatcher("파일 감시 중지됨");
			return;
		}
		if (_sourcePath == null || _candidatePath == null)
		{
			StatusText.Text = "감시를 시작하려면 현재 프로젝트 안의 SOP 검토 파일을 먼저 선택하세요.";
			return;
		}
		try
		{
			long generation = Interlocked.Increment(ref _watchGeneration);
			string directoryName = Path.GetDirectoryName(_sourcePath);
			string fileName = Path.GetFileName(_sourcePath);
			_watcher = new FileSystemWatcher(directoryName, fileName)
			{
				NotifyFilter = (NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite),
				IncludeSubdirectories = false,
				EnableRaisingEvents = true
			};
			FileSystemEventHandler value = delegate
			{
				QueueDebounce(generation);
			};
			RenamedEventHandler value2 = delegate
			{
				QueueDebounce(generation);
			};
			_watcher.Changed += value;
			_watcher.Created += value;
			_watcher.Renamed += value2;
			WatchButton.Content = "파일 변경 자동 적용 중지";
			StatusText.Text = "감시 중 · " + fileName + " · 현재 모듈 v" + (_activeSnapshot?.Version ?? "미적용");
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			StopWatcher("감시 시작 실패 · " + ex.Message);
		}
	}

	private void QueueDebounce(long generation)
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (_watcher != null && generation == Volatile.Read(ref _watchGeneration))
			{
				_debounce.Stop();
				_pendingWatchGeneration = generation;
				_debounce.Start();
			}
		});
	}

	private void Debounce_Tick(object? sender, EventArgs e)
	{
		_debounce.Stop();
		long pendingWatchGeneration = _pendingWatchGeneration;
		if (_watcher == null || _sourcePath == null || pendingWatchGeneration != Volatile.Read(ref _watchGeneration))
		{
			return;
		}
		try
		{
			LoadCandidateForWatch(_sourcePath);
			ApplyCandidate(automatic: true);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException) ? 1 : 0) != 0)
		{
			StatusText.Text = "자동 적용 실패 · 이전 모듈 유지 · " + ex.Message;
		}
	}

	private void LoadCandidateForWatch(string sourcePath)
	{
		byte[] array = File.ReadAllBytes(sourcePath);
		if (array.Length > 1048576 || ((IEnumerable<byte>)array).Contains((byte)0))
		{
			throw new InvalidDataException("후보 텍스트가 크기 또는 형식 제한을 넘었습니다.");
		}
		string text = Convert.ToHexString(SHA256.HashData(array)).ToLowerInvariant();
		if (!(text == _candidateHash))
		{
			_candidateHash = text;
			_candidatePath = SaveImmutableSnapshot(array, text);
			ContentBox.Text = Encoding.UTF8.GetString(array);
		}
	}

	private void StopWatcher(string message)
	{
		Interlocked.Increment(ref _watchGeneration);
		_debounce.Stop();
		_watcher?.Dispose();
		_watcher = null;
		if (WatchButton != null)
		{
			WatchButton.Content = "파일 변경 자동 적용 시작";
		}
		if (message.Length > 0 && StatusText != null)
		{
			StatusText.Text = message;
		}
	}

	private string SaveImmutableSnapshot(byte[] bytes, string hash)
	{
		string text = Path.Combine(_appDataRoot, "module-cache", "qa.sop-review");
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, hash + ".md");
		if (!File.Exists(text2))
		{
			string text3 = text2 + ".tmp-" + Guid.NewGuid().ToString("N");
			File.WriteAllBytes(text3, bytes);
			try
			{
				File.Move(text3, text2, overwrite: false);
			}
			catch (IOException) when (File.Exists(text2))
			{
				File.Delete(text3);
			}
		}
		return text2;
	}

	private static bool IsUnderRoot(string path, string root)
	{
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string value = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		return path.StartsWith(value, StringComparison.OrdinalIgnoreCase);
	}

	private void Rollback_Click(object sender, RoutedEventArgs e)
	{
		if (!_busy)
		{
			(string, string)? previous = _previous;
			if (previous.HasValue)
			{
				(string, string) valueOrDefault = previous.GetValueOrDefault();
				_busy = true;
				try
				{
					TextFileModuleFactoryV1 textFileModuleFactoryV = new TextFileModuleFactoryV1(valueOrDefault.Item1, "content-" + valueOrDefault.Item2.Substring(0, 12));
					if (!_host.Restore(textFileModuleFactoryV, textFileModuleFactoryV.ModuleId, textFileModuleFactoryV.Version, valueOrDefault.Item2))
					{
						StatusText.Text = "복원 실패 · 이전 모듈 유지 · " + _host.LastError;
						return;
					}
					(string, string, string)? activeSnapshot = _activeSnapshot;
					if (activeSnapshot.HasValue)
					{
						(string, string, string) valueOrDefault2 = activeSnapshot.GetValueOrDefault();
						_previous = (valueOrDefault2.Item1, valueOrDefault2.Item2);
					}
					_activeSnapshot = (valueOrDefault.Item1, valueOrDefault.Item2, textFileModuleFactoryV.Version);
					ContentBox.Text = _host.Active?.Content ?? string.Empty;
					StatusText.Text = "복원됨 · v" + _host.Active?.Version + " · SHA-256 " + _host.Active?.SourceHash;
					return;
				}
				finally
				{
					_busy = false;
				}
			}
		}
		StatusText.Text = "복원할 직전 모듈이 없습니다.";
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.28.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			EndermanQa.Desktop.XamlLoader.BamlHelper.LoadBaml(this, "modules/modulemanagerview.baml");
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
			ThemeModuleStatus = (TextBlock)target;
			break;
		case 2:
			ThemeWatchButton = (Button)target;
			ThemeWatchButton.Click += ThemeWatch_Click;
			break;
		case 3:
			((Button)target).Click += OpenThemeFolder_Click;
			break;
		case 4:
			((Button)target).Click += ThemeRollback_Click;
			break;
		case 5:
			((Button)target).Click += Choose_Click;
			break;
		case 6:
			((Button)target).Click += Apply_Click;
			break;
		case 7:
			WatchButton = (Button)target;
			WatchButton.Click += Watch_Click;
			break;
		case 8:
			((Button)target).Click += Rollback_Click;
			break;
		case 9:
			ContentBox = (TextBox)target;
			break;
		case 10:
			StatusText = (TextBlock)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
