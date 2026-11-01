using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Integrations;

public sealed class OnlyChatRuntimeLocator
{
	private const int MaximumEndpointBytes = 16384;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly string _endpointPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnlyChat", "runtime", "endpoint.json");

	private readonly string _installedExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OnlyChat-Electron-1.0.5", "Only Chat.exe");

	public async Task<Uri> ConnectAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		Uri uri = await TryReadVerifiedEndpointAsync(cancellationToken);
		if ((object)uri != null)
		{
			return uri;
		}
		if (!File.Exists(_installedExe))
		{
			throw new FileNotFoundException("설치된 Only Chat 1.0.5를 찾을 수 없습니다.", _installedExe);
		}
		if (!string.Equals(FileVersionInfo.GetVersionInfo(_installedExe).ProductVersion, "1.0.5", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("등록된 설치본과 다른 Only Chat 버전은 자동 실행하지 않습니다.");
		}
		Process.Start(new ProcessStartInfo(_installedExe)
		{
			UseShellExecute = true
		});
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(18.0);
		while (DateTimeOffset.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Uri uri2 = await TryReadVerifiedEndpointAsync(cancellationToken);
			if ((object)uri2 != null)
			{
				return uri2;
			}
			await Task.Delay(350, cancellationToken);
		}
		throw new TimeoutException("Only Chat을 열었지만 로컬 채팅 서버가 18초 안에 준비되지 않았습니다. Only Chat 상태를 확인한 뒤 다시 연결하세요.");
	}

	private async Task<Uri?> TryReadVerifiedEndpointAsync(CancellationToken cancellationToken)
	{
		bool flag = !File.Exists(_endpointPath);
		if (!flag)
		{
			long length = new FileInfo(_endpointPath).Length;
			bool flag2 = ((length <= 0 || length > 16384) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return null;
		}
		using JsonDocument endpointJson = JsonDocument.Parse(await File.ReadAllTextAsync(_endpointPath, cancellationToken));
		JsonElement rootElement = endpointJson.RootElement;
		int pid = ReadInt(rootElement, "pid");
		string owner = ReadString(rootElement, "ownerId");
		string text = ReadString(rootElement, "host");
		int num = ReadInt(rootElement, "port");
		string uriString = ReadString(rootElement, "url");
		flag = ReadInt(rootElement, "schemaVersion") != 1 || ReadString(rootElement, "version") != "1.0.5" || string.IsNullOrWhiteSpace(owner) || pid <= 0 || text != "127.0.0.1";
		if (!flag)
		{
			bool flag2 = ((num < 1024 || num > 65535) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return null;
		}
		Uri expected = new Uri($"http://127.0.0.1:{num}");
		if (!Uri.TryCreate(uriString, UriKind.Absolute, out Uri result) || result != expected)
		{
			return null;
		}
		try
		{
			using Process process = Process.GetProcessById(pid);
			if (!Path.GetFullPath(process.MainModule?.FileName ?? string.Empty).Equals(Path.GetFullPath(_installedExe), StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			using HttpClient http = new HttpClient(new SocketsHttpHandler
			{
				AllowAutoRedirect = false,
				ConnectTimeout = TimeSpan.FromSeconds(2.0)
			})
			{
				Timeout = TimeSpan.FromSeconds(3.0)
			};
			using HttpResponseMessage response = await http.GetAsync(new Uri(expected, "/api/runtime"), cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			using JsonDocument runtime = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
			JsonElement rootElement2 = runtime.RootElement;
			if (ReadInt(rootElement2, "pid") != pid || ReadString(rootElement2, "ownerId") != owner)
			{
				return null;
			}
			using HttpResponseMessage httpResponseMessage = await http.GetAsync(expected, cancellationToken);
			if (!httpResponseMessage.IsSuccessStatusCode || httpResponseMessage.Content.Headers.ContentType?.MediaType != "text/html")
			{
				return null;
			}
			return expected;
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is InvalidOperationException || ex is HttpRequestException || ex is IOException || ex is JsonException || ex is Win32Exception || ex is TaskCanceledException) ? 1 : 0) != 0)
		{
			if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			return null;
		}
	}

	private static string? ReadString(JsonElement value, string name)
	{
		if (!value.TryGetProperty(name, out var value2) || value2.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value2.GetString();
	}

	private static int ReadInt(JsonElement value, string name)
	{
		if (!value.TryGetProperty(name, out var value2) || !value2.TryGetInt32(out var value3))
		{
			return 0;
		}
		return value3;
	}
}
