using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Integrations;

public sealed class OnlyChatManagerClient : IDisposable
{
	public sealed record ProviderProfile(string Id, string Name, string Provider, string Model, bool Enabled, bool CoolingDown);

	public sealed record ManagerStatus(bool Available, string Message, string? Version, string? State, int ActiveRunCount, IReadOnlyList<ProviderProfile> Profiles);

	public sealed record RunReceipt(string RunId, string? SessionId, string Status, string WorkspacePath, DateTimeOffset StartedAt);

	public sealed record RunSnapshot(string RunId, string Status, string? Progress, string? LastError);

	private sealed record Endpoint(int SchemaVersion, string OwnerId, int Pid, string Host, int Port, string Url, string? Version, string DataDir);

	private const int MaximumEndpointBytes = 16384;

	private const int MaximumResponseBytes = 2097152;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly HttpClient _http = new HttpClient(new SocketsHttpHandler
	{
		UseCookies = false,
		AllowAutoRedirect = false,
		ConnectTimeout = TimeSpan.FromSeconds(2.0)
	})
	{
		Timeout = TimeSpan.FromSeconds(8.0)
	};

	public async Task<ManagerStatus> GetStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		Endpoint endpoint = ReadEndpoint();
		JsonDocument jsonDocument = await GetJsonAsync(endpoint, "/api/runtime", cancellationToken);
		if (GetInt(jsonDocument.RootElement, "pid") != endpoint.Pid || GetString(jsonDocument.RootElement, "ownerId") != endpoint.OwnerId)
		{
			throw new InvalidOperationException("Only Chat runtime owner/PID가 endpoint 기록과 달라 연결을 중단했습니다.");
		}
		JsonDocument manager = await GetJsonAsync(endpoint, "/api/admin-manager/status", cancellationToken);
		JsonDocument profilesDoc = await GetJsonAsync(endpoint, "/api/codex-work-queue/profiles", cancellationToken);
		JsonDocument jsonDocument2 = await GetJsonAsync(endpoint, "/api/manager-runs", cancellationToken);
		JsonElement value;
		string state = (manager.RootElement.TryGetProperty("manager", out value) ? GetString(value, "state") : null);
		List<ProviderProfile> list = new List<ProviderProfile>();
		if (profilesDoc.RootElement.TryGetProperty("profiles", out var value2) && value2.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value2.EnumerateArray())
			{
				ProviderProfile providerProfile = new ProviderProfile(GetString(item, "id") ?? "", GetString(item, "name") ?? "Provider profile", GetString(item, "provider") ?? "unknown", GetString(item, "model") ?? "unknown", GetBool(item, "enabled"), GetBool(item, "coolingDown"));
				if (providerProfile.Id.Length > 0 && providerProfile.Id.Length <= 120 && providerProfile.Name.Length <= 120 && providerProfile.Provider.Length <= 60 && providerProfile.Model.Length <= 120)
				{
					list.Add(providerProfile);
				}
			}
		}
		int activeRunCount = 0;
		if (jsonDocument2.RootElement.TryGetProperty("runs", out var value3) && value3.ValueKind == JsonValueKind.Array)
		{
			activeRunCount = value3.EnumerateArray().Count(delegate(JsonElement run)
			{
				switch (GetString(run, "status") ?? "")
				{
				case "queued":
				case "running":
				case "stopping":
					return true;
				default:
					return false;
				}
			});
		}
		return new ManagerStatus(Available: true, "Only Chat 로컬 Manager API v1에 연결됨. 공급자 인증은 실행을 요청할 때 Only Chat이 다시 확인합니다.", endpoint.Version, state, activeRunCount, list);
	}

	public async Task<RunReceipt> StartRunAsync(string profileId, string objective, string workspacePath, int durationSeconds, string sandboxPolicy, CancellationToken cancellationToken = default(CancellationToken))
	{
		string profileId2 = profileId;
		if (string.IsNullOrWhiteSpace(objective) || objective.Trim().Length > 12000)
		{
			throw new ArgumentException("작업 설명은 1~12,000자여야 합니다.");
		}
		if ((durationSeconds < 1 || durationSeconds > 604800) ? true : false)
		{
			throw new ArgumentOutOfRangeException("durationSeconds");
		}
		if ((!(sandboxPolicy == "read-only") && !(sandboxPolicy == "workspace-write")) || 1 == 0)
		{
			throw new ArgumentException("허용되지 않은 Only Chat 접근 범위입니다.");
		}
		string workspace = Path.GetFullPath(workspacePath);
		if (!Directory.Exists(workspace))
		{
			throw new DirectoryNotFoundException("선택한 작업 폴더가 없습니다.");
		}
		ManagerStatus managerStatus = await GetStatusAsync(cancellationToken);
		if (!managerStatus.Available || managerStatus.State != "active")
		{
			throw new InvalidOperationException("Only Chat 관리자 모듈이 활성 상태가 아닙니다.");
		}
		if (managerStatus.ActiveRunCount > 0)
		{
			throw new InvalidOperationException("Only Chat에서 이미 진행 중인 관리자 작업이 있어 새 작업을 보내지 않았습니다.");
		}
		ProviderProfile providerProfile = managerStatus.Profiles.FirstOrDefault(delegate(ProviderProfile x)
		{
			bool flag = x.Id == profileId2 && x.Enabled && !x.CoolingDown;
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
		});
		if ((object)providerProfile == null)
		{
			throw new InvalidOperationException("선택한 Only Chat Codex 프로필을 사용할 수 없습니다. 프로필 상태를 새로 고쳐주세요.");
		}
		Endpoint endpoint = ReadEndpoint();
		using HttpRequestMessage request = CreateRequest(endpoint, HttpMethod.Post, "/api/manager-runs");
		request.Content = new StringContent(JsonSerializer.Serialize(new
		{
			confirmed = true,
			objective = objective.Trim(),
			workspaceDir = workspace,
			profileId = providerProfile.Id,
			durationSeconds = durationSeconds,
			sandboxPolicy = sandboxPolicy
		}, JsonOptions), Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
		using JsonDocument jsonDocument = await ReadJsonAsync(response, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(ExtractError(jsonDocument.RootElement, "Only Chat 작업을 시작하지 못했습니다."));
		}
		if (jsonDocument.RootElement.TryGetProperty("run", out var value))
		{
			string @string = GetString(value, "id");
			if (@string != null && @string.Length == 36 && Guid.TryParse(@string, out var _))
			{
				JsonElement value2;
				string sessionId = ((jsonDocument.RootElement.TryGetProperty("item", out value2) && value2.ValueKind == JsonValueKind.Object) ? GetString(value2, "internalSessionId") : null);
				return new RunReceipt(@string, sessionId, GetString(value, "status") ?? "queued", workspace, DateTimeOffset.UtcNow);
			}
		}
		throw new InvalidDataException("Only Chat이 새 작업 ID를 반환하지 않아 시작 여부를 확인할 수 없습니다.");
	}

	public async Task<RunSnapshot?> GetRunAsync(string runId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Guid.TryParse(runId, out var _))
		{
			throw new ArgumentException("Only Chat 작업 ID가 올바르지 않습니다.", "runId");
		}
		Endpoint endpoint = ReadEndpoint();
		if (!(await GetJsonAsync(endpoint, "/api/manager-runs", cancellationToken)).RootElement.TryGetProperty("runs", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return null;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (string.Equals(GetString(item, "id"), runId, StringComparison.OrdinalIgnoreCase))
			{
				return new RunSnapshot(runId, GetString(item, "status") ?? "unknown", GetString(item, "lastProgress"), GetString(item, "lastError"));
			}
		}
		return null;
	}

	public async Task StopRunAsync(string runId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Guid.TryParse(runId, out var _))
		{
			throw new ArgumentException("Only Chat 작업 ID가 올바르지 않습니다.", "runId");
		}
		Endpoint endpoint = ReadEndpoint();
		using HttpRequestMessage request = CreateRequest(endpoint, HttpMethod.Post, "/api/manager-runs/" + runId + "/stop");
		request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
		using JsonDocument jsonDocument = await ReadJsonAsync(response, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(ExtractError(jsonDocument.RootElement, "Only Chat 관리자 작업을 중지하지 못했습니다."));
		}
	}

	private async Task<JsonDocument> GetJsonAsync(Endpoint endpoint, string path, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = CreateRequest(endpoint, HttpMethod.Get, path);
		using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
		return await ReadJsonAsync(response, cancellationToken);
	}

	private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		byte[] obj = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (obj.Length > 2097152)
		{
			throw new InvalidDataException("Only Chat API 응답이 허용 크기를 초과했습니다.");
		}
		JsonDocument result = JsonDocument.Parse(obj);
		_ = response.IsSuccessStatusCode;
		return result;
	}

	private HttpRequestMessage CreateRequest(Endpoint endpoint, HttpMethod method, string path)
	{
		HttpRequestMessage httpRequestMessage = new HttpRequestMessage(method, new Uri(new Uri(endpoint.Url), path));
		httpRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		httpRequestMessage.Headers.TryAddWithoutValidation("Origin", $"http://{endpoint.Host}:{endpoint.Port}");
		return httpRequestMessage;
	}

	private static Endpoint ReadEndpoint()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnlyChat", "runtime", "endpoint.json");
		if (!File.Exists(text))
		{
			throw new InvalidOperationException("Only Chat 로컬 런타임을 찾을 수 없습니다. 앱에서 먼저 Only Chat을 시작하세요.");
		}
		if ((File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0 || new FileInfo(text).Length > 16384)
		{
			throw new InvalidDataException("Only Chat endpoint 파일이 올바른 크기/경로가 아닙니다.");
		}
		Endpoint endpoint = JsonSerializer.Deserialize<Endpoint>(File.ReadAllText(text), JsonOptions) ?? throw new InvalidDataException("Only Chat endpoint 파일을 읽을 수 없습니다.");
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnlyChat");
		Guid result;
		bool flag = endpoint.SchemaVersion != 1 || !Guid.TryParse(endpoint.OwnerId, out result) || endpoint.Pid < 1;
		if (!flag)
		{
			int port = endpoint.Port;
			bool flag2 = ((port < 1 || port > 65535) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			string host = endpoint.Host;
			bool flag2 = ((host == "127.0.0.1" || host == "localhost") ? true : false);
			flag3 = !flag2;
		}
		if (flag3 || endpoint.Url != $"http://{endpoint.Host}:{endpoint.Port}" || !Path.GetFullPath(endpoint.DataDir).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Only Chat endpoint가 지원되는 loopback runtime v1과 일치하지 않습니다.");
		}
		try
		{
			using Process process = Process.GetProcessById(endpoint.Pid);
			string text2 = process.MainModule?.FileName;
			string value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OnlyChat-Electron-");
			if (!string.Equals(process.ProcessName, "Only Chat", StringComparison.OrdinalIgnoreCase) || text2 == null || !Path.GetFileName(text2).Equals("Only Chat.exe", StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(text2).StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("endpoint PID가 등록된 Only Chat 설치 프로세스가 아니어서 연결을 차단했습니다.");
			}
			return endpoint;
		}
		catch (ArgumentException)
		{
			throw new InvalidOperationException("Only Chat endpoint PID가 실행 중이 아닙니다.");
		}
		catch (Win32Exception)
		{
			throw new InvalidOperationException("Only Chat 프로세스 신원을 확인할 수 없습니다.");
		}
	}

	private static string? GetString(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static int GetInt(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var value2))
		{
			return -1;
		}
		return value2;
	}

	private static bool GetBool(JsonElement element, string name)
	{
		if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	private static string ExtractError(JsonElement element, string fallback)
	{
		string @string = GetString(element, "error");
		if (@string != null)
		{
			int length = @string.Length;
			if (length > 0 && length <= 300)
			{
				return @string;
			}
		}
		return fallback;
	}

	public void Dispose()
	{
		_http.Dispose();
	}
}
