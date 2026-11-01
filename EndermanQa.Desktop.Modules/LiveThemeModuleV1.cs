using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndermanQa.Desktop.Modules;

public sealed class LiveThemeModuleV1 : IDisposable
{
	private sealed record ThemeDocument(string Version, IReadOnlyDictionary<string, string> Colors);

	private static readonly string[] ColorNames = new string[15]
	{
		"Canvas", "Surface", "SurfaceAlt", "SurfaceInset", "Accent", "AccentMuted", "AccentMutedAlt", "TextPrimary", "TextStrong", "TextSecondary",
		"TextMuted", "TextAccent", "Border", "Positive", "Warning"
	};

	private readonly string _root;

	private readonly string _sourcePath;

	private readonly string _snapshotRoot;

	private readonly string _watchSettingsPath;

	private readonly Dispatcher _dispatcher;

	private readonly DispatcherTimer _debounce = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(450.0)
	};

	private FileSystemWatcher? _watcher;

	private long _generation;

	private long _pendingGeneration;

	private bool _disposed;

	private bool _applying;

	private const string DefaultThemeJson = "{\r\n  \"apiVersion\": 1,\r\n  \"moduleId\": \"ui.theme\",\r\n  \"version\": \"2.2.0-jira-light\",\r\n  \"colors\": {\r\n    \"Canvas\": \"#F4F5F7\", \"Surface\": \"#FFFFFF\", \"SurfaceAlt\": \"#F7F8F9\", \"SurfaceInset\": \"#FAFBFC\",\r\n    \"Accent\": \"#0C66E4\", \"AccentMuted\": \"#E9F2FF\", \"AccentMutedAlt\": \"#DEEBFF\",\r\n    \"TextPrimary\": \"#44546F\", \"TextStrong\": \"#172B4D\", \"TextSecondary\": \"#626F86\", \"TextMuted\": \"#8590A2\",\r\n    \"TextAccent\": \"#0C66E4\", \"Border\": \"#DFE1E6\", \"Positive\": \"#216E4E\", \"Warning\": \"#974F0C\"\r\n  }\r\n}";

	public string ModuleId => "ui.theme";

	public int ApiVersion => 1;

	public string? LoadedVersion { get; private set; }

	public string? LoadedHash { get; private set; }

	public bool IsWatching => _watcher != null;

	public string SourcePath => _sourcePath;

	public string Status { get; private set; } = "테마 모듈 초기화 대기";


	public event Action<string>? StatusChanged;

	public LiveThemeModuleV1(string? localAppDataRoot = null, Dispatcher? dispatcher = null)
	{
		_root = Path.GetFullPath(localAppDataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndermanQaWorkbench"));
		_sourcePath = Path.Combine(_root, "live-modules", "ui-theme.json");
		_snapshotRoot = Path.Combine(_root, "module-cache", ModuleId);
		_watchSettingsPath = Path.Combine(_root, "settings", "live-modules.json");
		_dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
		_debounce.Tick += Debounce_Tick;
	}

	public void Initialize()
	{
		ThrowIfDisposed();
		Directory.CreateDirectory(Path.GetDirectoryName(_sourcePath));
		Directory.CreateDirectory(_snapshotRoot);
		Directory.CreateDirectory(Path.GetDirectoryName(_watchSettingsPath));
		if (!File.Exists(_sourcePath))
		{
			WriteNewFile(_sourcePath, Encoding.UTF8.GetBytes("{\r\n  \"apiVersion\": 1,\r\n  \"moduleId\": \"ui.theme\",\r\n  \"version\": \"2.2.0-jira-light\",\r\n  \"colors\": {\r\n    \"Canvas\": \"#F4F5F7\", \"Surface\": \"#FFFFFF\", \"SurfaceAlt\": \"#F7F8F9\", \"SurfaceInset\": \"#FAFBFC\",\r\n    \"Accent\": \"#0C66E4\", \"AccentMuted\": \"#E9F2FF\", \"AccentMutedAlt\": \"#DEEBFF\",\r\n    \"TextPrimary\": \"#44546F\", \"TextStrong\": \"#172B4D\", \"TextSecondary\": \"#626F86\", \"TextMuted\": \"#8590A2\",\r\n    \"TextAccent\": \"#0C66E4\", \"Border\": \"#DFE1E6\", \"Positive\": \"#216E4E\", \"Warning\": \"#974F0C\"\r\n  }\r\n}"));
		}
		byte[] bytes = File.ReadAllBytes(_sourcePath);
		try
		{
			Apply(bytes, Hash(bytes), "초기 로드");
		}
		catch (Exception ex) when (((ex is IOException || ex is InvalidDataException || ex is JsonException || ex is FormatException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			SetStatus(FormatStatus("초기 테마 거부 · 기본 테마 유지 · " + ex.Message, LoadedVersion, LoadedHash));
		}
		bool flag;
		try
		{
			flag = ReadWatchEnabled();
		}
		catch (Exception ex2) when (((ex2 is IOException || ex2 is InvalidDataException || ex2 is JsonException || ex2 is InvalidOperationException) ? 1 : 0) != 0)
		{
			flag = false;
			SetStatus("감시 설정 오류 · 안전을 위해 자동 적용 중지 · " + ex2.Message);
		}
		if (flag)
		{
			StartWatching(persist: false);
		}
		else if (Status == "테마 모듈 초기화 대기")
		{
			SetStatus(FormatStatus("감시 중지 상태", LoadedVersion, LoadedHash));
		}
	}

	public void StartWatching()
	{
		ThrowIfDisposed();
		PersistWatchEnabled(enabled: true);
		StartWatching(persist: false);
	}

	public void StopWatching()
	{
		PersistWatchEnabled(enabled: false);
		StopWatcherCore();
		SetStatus(FormatStatus("자동 적용 중지됨", LoadedVersion, LoadedHash));
	}

	public bool Rollback()
	{
		ThrowIfDisposed();
		if (!Directory.Exists(_snapshotRoot))
		{
			return false;
		}
		string text = (from path in Directory.EnumerateFiles(_snapshotRoot, "*.json", SearchOption.TopDirectoryOnly)
			where !Path.GetFileNameWithoutExtension(path).Equals(LoadedHash, StringComparison.OrdinalIgnoreCase)
			select path).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
		if (text == null)
		{
			return false;
		}
		byte[] bytes = File.ReadAllBytes(text);
		string text2 = Hash(bytes);
		if (!Path.GetFileNameWithoutExtension(text).Equals(text2, StringComparison.OrdinalIgnoreCase))
		{
			SetStatus("복원 거부 · 스냅샷 SHA-256 불일치");
			return false;
		}
		byte[] bytes2 = File.ReadAllBytes(_sourcePath);
		try
		{
			ParseAndValidate(bytes);
			ReplaceFile(_sourcePath, bytes);
			Apply(bytes, text2, "직전 검증 모듈 복원");
			return true;
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is JsonException || ex is FormatException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			try
			{
				ReplaceFile(_sourcePath, bytes2);
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				SetStatus("롤백 실패 · 원본 설정 복원도 실패 · " + ex2.Message);
				return false;
			}
			SetStatus("롤백 실패 · 기존 테마와 원본 설정 유지 · " + ex.Message);
			return false;
		}
	}

	private void StartWatching(bool persist)
	{
		if (persist)
		{
			PersistWatchEnabled(enabled: true);
		}
		if (_watcher == null)
		{
			long generation = Interlocked.Increment(ref _generation);
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
				QueueApply(generation);
			};
			RenamedEventHandler value2 = delegate
			{
				QueueApply(generation);
			};
			_watcher.Changed += value;
			_watcher.Created += value;
			_watcher.Renamed += value2;
			SetStatus(FormatStatus("실시간 테마 감시 중", LoadedVersion, LoadedHash));
		}
	}

	private void QueueApply(long generation)
	{
		if (_disposed)
		{
			return;
		}
		_dispatcher.BeginInvoke((Action)delegate
		{
			if (_watcher != null && generation == Volatile.Read(ref _generation))
			{
				_debounce.Stop();
				_pendingGeneration = generation;
				_debounce.Start();
			}
		});
	}

	private async void Debounce_Tick(object? sender, EventArgs e)
	{
		_debounce.Stop();
		long generation = _pendingGeneration;
		if (_watcher == null || generation != Volatile.Read(ref _generation) || _applying)
		{
			return;
		}
		try
		{
			byte[] array = File.ReadAllBytes(_sourcePath);
			if (array.Length > 16384)
			{
				throw new InvalidDataException("테마 설정은 16 KiB 이하여야 합니다.");
			}
			string firstHash = Hash(array);
			await Task.Delay(180);
			if (_watcher != null && generation == Volatile.Read(ref _generation))
			{
				byte[] bytes = File.ReadAllBytes(_sourcePath);
				if (!firstHash.Equals(Hash(bytes), StringComparison.Ordinal))
				{
					_pendingGeneration = generation;
					_debounce.Start();
				}
				else if (!firstHash.Equals(LoadedHash, StringComparison.OrdinalIgnoreCase))
				{
					Apply(bytes, firstHash, "파일 변경 실시간 적용");
				}
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is JsonException || ex is FormatException) ? 1 : 0) != 0)
		{
			SetStatus(FormatStatus("후보 거부 · 기존 테마 유지 · " + ex.Message, LoadedVersion, LoadedHash));
		}
	}

	private void Apply(byte[] bytes, string expectedSourceHash, string reason)
	{
		if (bytes.Length > 16384)
		{
			throw new InvalidDataException("테마 설정은 16 KiB 이하여야 합니다.");
		}
		string text = Hash(bytes);
		if (!text.Equals(expectedSourceHash, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("후보 파일이 적용 전에 바뀌었습니다.");
		}
		ThemeDocument themeDocument = ParseAndValidate(bytes);
		string path = Path.Combine(_snapshotRoot, text + ".json");
		if (!File.Exists(path))
		{
			WriteNewFile(path, bytes);
		}
		Dictionary<string, object> dictionary = ColorNames.ToDictionary((string name) => "Theme." + name, delegate(string name)
		{
			Application current = Application.Current;
			return (current == null || !current.Resources.Contains("Theme." + name)) ? null : Application.Current.Resources["Theme." + name];
		});
		_applying = true;
		try
		{
			if (!_dispatcher.CheckAccess())
			{
				throw new InvalidOperationException("테마는 WPF UI Dispatcher에서만 적용할 수 있습니다.");
			}
			string[] colorNames = ColorNames;
			foreach (string text2 in colorNames)
			{
				Color color = (Color)ColorConverter.ConvertFromString(themeDocument.Colors[text2]);
				Application.Current.Resources["Theme." + text2] = new SolidColorBrush(color);
			}
			LoadedVersion = themeDocument.Version;
			LoadedHash = text;
			SetStatus(FormatStatus(reason + " 완료", LoadedVersion, LoadedHash));
		}
		catch
		{
			string[] colorNames = ColorNames;
			foreach (string text3 in colorNames)
			{
				string key = "Theme." + text3;
				object obj = dictionary[key];
				if (obj != null)
				{
					Application.Current.Resources[key] = obj;
				}
			}
			throw;
		}
		finally
		{
			_applying = false;
		}
	}

	private static ThemeDocument ParseAndValidate(byte[] bytes)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(bytes, new JsonDocumentOptions
		{
			AllowTrailingCommas = false,
			CommentHandling = JsonCommentHandling.Disallow,
			MaxDepth = 8
		});
		JsonElement rootElement = jsonDocument.RootElement;
		RequireObjectKeys(rootElement, new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "apiVersion", "moduleId", "version", "colors" }));
		if (rootElement.GetProperty("apiVersion").GetInt32() != 1)
		{
			throw new InvalidDataException("지원하지 않는 테마 API 버전입니다.");
		}
		if (rootElement.GetProperty("moduleId").GetString() != "ui.theme")
		{
			throw new InvalidDataException("테마 모듈 ID가 다릅니다.");
		}
		string @string = rootElement.GetProperty("version").GetString();
		if (string.IsNullOrWhiteSpace(@string) || @string.Length > 48 || !Regex.IsMatch(@string, "^[A-Za-z0-9][A-Za-z0-9._+-]*$"))
		{
			throw new InvalidDataException("테마 버전 표기가 올바르지 않습니다.");
		}
		JsonElement property = rootElement.GetProperty("colors");
		RequireObjectKeys(property, (IReadOnlyCollection<string>)(object)ColorNames);
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		string[] colorNames = ColorNames;
		foreach (string text in colorNames)
		{
			string string2 = property.GetProperty(text).GetString();
			if (string2 == null || !Regex.IsMatch(string2, "^#[0-9a-fA-F]{6}$"))
			{
				throw new InvalidDataException("색상 토큰 " + text + "은 #RRGGBB 형식이어야 합니다.");
			}
			dictionary.Add(text, string2);
		}
		return new ThemeDocument(@string, dictionary);
	}

	private static void RequireObjectKeys(JsonElement element, IReadOnlyCollection<string> required)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException("테마 구조에 JSON 객체가 필요합니다.");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonProperty item in element.EnumerateObject())
		{
			if (!hashSet.Add(item.Name))
			{
				throw new InvalidDataException("중복 JSON 키는 허용되지 않습니다.");
			}
		}
		if (!hashSet.SetEquals(required))
		{
			throw new InvalidDataException("허용되지 않았거나 누락된 테마 속성이 있습니다.");
		}
	}

	private bool ReadWatchEnabled()
	{
		if (!File.Exists(_watchSettingsPath))
		{
			PersistWatchEnabled(enabled: true);
			return true;
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllBytes(_watchSettingsPath));
		JsonElement rootElement = jsonDocument.RootElement;
		RequireObjectKeys(rootElement, new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "apiVersion", "enabled" }));
		if (rootElement.GetProperty("apiVersion").GetInt32() != 1)
		{
			throw new InvalidDataException("실시간 동기화 설정 API가 다릅니다.");
		}
		return rootElement.GetProperty("enabled").GetBoolean();
	}

	private void PersistWatchEnabled(bool enabled)
	{
		string text = _watchSettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
		Directory.CreateDirectory(Path.GetDirectoryName(_watchSettingsPath));
		File.WriteAllText(text, JsonSerializer.Serialize(new
		{
			apiVersion = 1,
			enabled = enabled
		}));
		File.Move(text, _watchSettingsPath, overwrite: true);
	}

	private static void ReplaceFile(string path, byte[] bytes)
	{
		string text = path + ".tmp-" + Guid.NewGuid().ToString("N");
		File.WriteAllBytes(text, bytes);
		File.Move(text, path, overwrite: true);
	}

	private void StopWatcherCore()
	{
		Interlocked.Increment(ref _generation);
		_debounce.Stop();
		_watcher?.Dispose();
		_watcher = null;
	}

	private void SetStatus(string value)
	{
		Status = value;
		try
		{
			string text = Path.Combine(_root, "settings", "live-module-status.json");
			string text2 = text + ".tmp-" + Guid.NewGuid().ToString("N");
			Directory.CreateDirectory(Path.GetDirectoryName(text));
			File.WriteAllText(text2, JsonSerializer.Serialize(new
			{
				apiVersion = 1,
				moduleId = ModuleId,
				loadedVersion = LoadedVersion,
				loadedHash = LoadedHash,
				watching = IsWatching,
				status = value,
				updatedAtUtc = DateTimeOffset.UtcNow
			}));
			File.Move(text2, text, overwrite: true);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
		}
		this.StatusChanged?.Invoke(value);
	}

	private static string FormatStatus(string prefix, string? version, string? hash)
	{
		return $"{prefix} · ui.theme {version ?? "미로드"} · SHA-256 {((hash == null) ? "미로드" : hash.Substring(0, 12))}";
	}

	private static string Hash(byte[] bytes)
	{
		return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
	}

	private static void WriteNewFile(string path, byte[] bytes)
	{
		string text = path + ".tmp-" + Guid.NewGuid().ToString("N");
		File.WriteAllBytes(text, bytes);
		try
		{
			File.Move(text, path, overwrite: false);
		}
		catch (IOException) when (File.Exists(path))
		{
			File.Delete(text);
		}
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException("LiveThemeModuleV1");
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			StopWatcherCore();
			_debounce.Tick -= Debounce_Tick;
			_disposed = true;
		}
	}
}
