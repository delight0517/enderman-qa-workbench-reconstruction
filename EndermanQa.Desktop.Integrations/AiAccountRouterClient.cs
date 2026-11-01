using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Contracts;

namespace EndermanQa.Desktop.Integrations;

public sealed class AiAccountRouterClient
{
	[CompilerGenerated]
	private HttpClient _003Chttp_003EP;

	public const string BaseUrl = "http://127.0.0.1:18880";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	public static Uri DashboardUri { get; } = new Uri("http://127.0.0.1:18880/");


	public AiAccountRouterClient(HttpClient http)
	{
		_003Chttp_003EP = http;
		
	}

	public async Task<RouterDiscoveryStatus> ReadDiscoveryAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(3.0));
		try
		{
			using HttpResponseMessage response = await _003Chttp_003EP.GetAsync("http://127.0.0.1:18880/api/v1/discovery", timeout.Token);
			if (!response.IsSuccessStatusCode)
			{
				return new RouterDiscoveryStatus(Connected: true, ApiCompatible: false, null, "unknown", "", "unknown", $"로컬 서비스는 응답했지만 Router 검색 API가 HTTP {response.StatusCode}를 반환했습니다.");
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(await ReadBoundedAsync(response.Content, 32768, timeout.Token));
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				return new RouterDiscoveryStatus(Connected: true, ApiCompatible: false, null, "unknown", "", "unknown", "Router 검색 응답 형식이 올바르지 않습니다.");
			}
			int? apiVersion = Int(rootElement, "apiVersion");
			string text = String(rootElement, "service", "");
			string text2 = String(rootElement, "endpoint", "");
			JsonElement value;
			JsonElement value2;
			bool flag = text == "ai-account-router" && apiVersion.GetValueOrDefault() == 1 && text2 == "http://127.0.0.1:18880/v1" && rootElement.TryGetProperty("auth", out value) && value.ValueKind == JsonValueKind.Object && String(value, "mode", "") == "same-user-loopback" && value.TryGetProperty("credentialsExposed", out value2) && value2.ValueKind == JsonValueKind.False;
			string text3 = String(rootElement, "providerStatus", "unknown");
			string text4 = String(rootElement, "packageVersion", "unknown");
			string message = (flag ? $"AI Account Router 연결됨 · v{text4}. 공급자 설정 상태: {text3}." : "응답한 서비스가 지원되는 AI Account Router API v1과 일치하지 않아 연결을 사용하지 않습니다.");
			return new RouterDiscoveryStatus(Connected: true, flag, apiVersion, text4, text2, text3, message);
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			return new RouterDiscoveryStatus(Connected: false, ApiCompatible: false, null, "unknown", "", "unknown", "AI Account Router가 127.0.0.1:18880에서 응답하지 않습니다. 앱 시작 시 자동 실행은 하지 않았습니다.");
		}
	}

	public async Task<RouterConnectionStatus> ReadStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		RouterDiscoveryStatus routerDiscoveryStatus = await ReadDiscoveryAsync(cancellationToken);
		if (!routerDiscoveryStatus.Connected)
		{
			return new RouterConnectionStatus(Connected: false, null, "unavailable", "unknown", InferenceAvailable: false, routerDiscoveryStatus.Message, Array.Empty<string>());
		}
		if (!routerDiscoveryStatus.ApiCompatible)
		{
			return new RouterConnectionStatus(Connected: true, routerDiscoveryStatus.ApiVersion, "unsupported-api", "unknown", InferenceAvailable: false, routerDiscoveryStatus.Message, Array.Empty<string>());
		}
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(5.0));
		try
		{
			using HttpResponseMessage response = await _003Chttp_003EP.GetAsync("http://127.0.0.1:18880/api/v1/providers/status", timeout.Token);
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				return new RouterConnectionStatus(Connected: true, null, "unsupported-api", "unknown", InferenceAvailable: false, "Router에 공급자 상태 API가 없습니다. AI 추론은 사용할 수 없습니다.", Array.Empty<string>());
			}
			if (!response.IsSuccessStatusCode)
			{
				return new RouterConnectionStatus(Connected: true, null, "unavailable", "unknown", InferenceAvailable: false, $"Router 상태 조회 실패 (HTTP {response.StatusCode}).", Array.Empty<string>());
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(await ReadBoundedAsync(response.Content, 65536, timeout.Token));
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				return new RouterConnectionStatus(Connected: true, null, "invalid-api-response", "unknown", InferenceAvailable: false, "Router 상태 응답 형식이 올바르지 않습니다. AI 사용을 차단했습니다.", Array.Empty<string>());
			}
			int? num = Int(rootElement, "apiVersion");
			string text = String(rootElement, "providerState", "unknown");
			string text2 = String(rootElement, "engineMode", "unknown");
			JsonElement value;
			string[] array = ((rootElement.TryGetProperty("accounts", out value) && value.ValueKind == JsonValueKind.Array) ? (from x in value.EnumerateArray()
				where String(x, "availability", "") == "ready"
				select String(x, "name", "Provider profile")).ToArray() : Array.Empty<string>());
			bool flag = num.GetValueOrDefault() == 1 && text == "configured" && text2 == "live-capable" && array.Length != 0;
			return new RouterConnectionStatus(Connected: true, Int(rootElement, "apiVersion"), text, text2, flag, flag ? $"실제 공급자 프로필 준비됨 ({array.Length}개). AI 초안 버튼을 눌렀을 때만 요청합니다. 토큰/계정 자체 검증은 Router가 수행하지 않았습니다." : ((num.GetValueOrDefault() != 1) ? ("지원하지 않는 Router API 버전(" + (num?.ToString() ?? "unknown") + ")입니다. AI 사용을 차단했습니다.") : "Router는 응답했지만 사용 가능한 실제 공급자 프로필이 없습니다."), array);
		}
		catch (Exception ex) when (((ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			return new RouterConnectionStatus(Connected: false, null, "unavailable", "unknown", InferenceAvailable: false, "AI Account Router가 127.0.0.1:18880에서 응답하지 않습니다. 자동 실행이나 로그인을 시도하지 않았습니다.", Array.Empty<string>());
		}
	}

	public async Task<AiRouterChatCompletion> CompleteChatAsync(IReadOnlyList<AiRouterChatMessage> messages, int maximumTokens = 900, double temperature = 0.25, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(messages, "messages");
		int count = messages.Count;
		if ((count < 1 || count > 12) ? true : false)
		{
			throw new ArgumentOutOfRangeException("messages", "Router chat accepts 1–12 messages per request.");
		}
		if ((maximumTokens < 1 || maximumTokens > 1600) ? true : false)
		{
			throw new ArgumentOutOfRangeException("maximumTokens");
		}
		bool flag = !double.IsFinite(temperature);
		if (!flag)
		{
			bool flag2 = ((temperature < 0.0 || temperature > 1.0) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new ArgumentOutOfRangeException("temperature");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal) { "system", "user", "assistant" };
		int num = 0;
		foreach (AiRouterChatMessage message in messages)
		{
			if ((object)message == null || !hashSet.Contains(message.Role))
			{
				throw new ArgumentException("Router chat accepts only system, user, and assistant messages.", "messages");
			}
			if (string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 8000)
			{
				throw new ArgumentException("Each Router chat message must contain 1–8,000 characters.", "messages");
			}
			num += message.Content.Length;
		}
		if (num > 28000)
		{
			throw new ArgumentException("Router chat context exceeded 28,000 characters.", "messages");
		}
		RouterConnectionStatus routerConnectionStatus = await ReadStatusAsync(cancellationToken);
		if (!routerConnectionStatus.InferenceAvailable)
		{
			throw new InvalidOperationException(routerConnectionStatus.Message);
		}
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(45.0));
		using HttpResponseMessage response = await _003Chttp_003EP.PostAsJsonAsync("http://127.0.0.1:18880/v1/chat/completions", new
		{
			messages = messages,
			temperature = temperature,
			max_tokens = maximumTokens
		}, JsonOptions, timeout.Token);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Router/provider request failed (HTTP {response.StatusCode}); no QA action was run.");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(await ReadBoundedAsync(response.Content, 65536, timeout.Token));
		if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object || !jsonDocument.RootElement.TryGetProperty("choices", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("Router response contained no assistant message.");
		}
		JsonElement jsonElement = value[0];
		if (jsonElement.ValueKind != JsonValueKind.Object || !jsonElement.TryGetProperty("message", out var value2) || value2.ValueKind != JsonValueKind.Object || !value2.TryGetProperty("content", out var value3) || value3.ValueKind != JsonValueKind.String)
		{
			throw new InvalidOperationException("Router response had an invalid assistant message shape.");
		}
		string text = value3.GetString()?.Trim();
		if (string.IsNullOrWhiteSpace(text) || text.Length > 8000)
		{
			throw new InvalidOperationException("Router assistant message was empty or exceeded 8,000 characters.");
		}
		return new AiRouterChatCompletion(text, String(jsonDocument.RootElement, "model", "Router-selected provider"));
	}

	public async Task<AiScenarioDraft> DraftScenarioAsync(string objective, string target, string gameVersion, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(objective))
		{
			throw new ArgumentException("테스트할 목표를 먼저 입력하세요.");
		}
		RouterConnectionStatus routerConnectionStatus = await ReadStatusAsync(cancellationToken);
		if (!routerConnectionStatus.InferenceAvailable)
		{
			throw new InvalidOperationException(routerConnectionStatus.Message);
		}
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(45.0));
		string content = "You draft manual QA cases. Return only a compact JSON object with string preconditions, string[] steps, and string expected. Never claim a test was run. Never propose commands or actions that mutate a production world. Prefer a disposable test world. Keep each field concise.";
		string content2 = $"Create a human-executable test draft for this objective: {objective}\nTarget: {target}\nGame version: {gameVersion}\nThis is only a suggestion, not execution. Include steps a person can follow and observable expected results.";
		using HttpResponseMessage response = await _003Chttp_003EP.PostAsJsonAsync("http://127.0.0.1:18880/v1/chat/completions", new
		{
			messages = new[]
			{
				new
				{
					role = "system",
					content = content
				},
				new
				{
					role = "user",
					content = content2
				}
			},
			temperature = 0.2,
			max_tokens = 900
		}, JsonOptions, timeout.Token);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Router/provider request failed (HTTP {response.StatusCode}); no plan was run.");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(await ReadBoundedAsync(response.Content, 65536, timeout.Token));
		if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object || !jsonDocument.RootElement.TryGetProperty("choices", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("Router response contained no assistant suggestion.");
		}
		if (value[0].ValueKind != JsonValueKind.Object || !value[0].TryGetProperty("message", out var value2) || value2.ValueKind != JsonValueKind.Object || !value2.TryGetProperty("content", out var value3) || value3.ValueKind != JsonValueKind.String)
		{
			throw new InvalidOperationException("Router response had an invalid assistant message shape.");
		}
		string @string = value3.GetString();
		if (string.IsNullOrWhiteSpace(@string) || @string.Length > 16000)
		{
			throw new InvalidOperationException("AI suggestion is empty or too large.");
		}
		using JsonDocument jsonDocument2 = JsonDocument.Parse(@string);
		JsonElement rootElement = jsonDocument2.RootElement;
		string preconditions = BoundedString(rootElement, "preconditions", 2000);
		string expected = BoundedString(rootElement, "expected", 3000);
		JsonElement value4;
		bool flag = !rootElement.TryGetProperty("steps", out value4) || value4.ValueKind != JsonValueKind.Array;
		if (!flag)
		{
			int arrayLength = value4.GetArrayLength();
			bool flag2 = ((arrayLength < 1 || arrayLength > 15) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("AI suggestion must contain 1–15 reviewable steps.");
		}
		string[] array = (from x in value4.EnumerateArray()
			select (x.ValueKind == JsonValueKind.String) ? (x.GetString()?.Trim() ?? "") : "").ToArray();
		if (array.Any(delegate(string x)
		{
			int length = x.Length;
			return (length < 1 || length > 500) ? true : false;
		}))
		{
			throw new InvalidOperationException("AI suggestion contains an invalid step.");
		}
		string providerModel = String(jsonDocument.RootElement, "model", "Router-selected provider");
		return new AiScenarioDraft(preconditions, array, expected, providerModel);
	}

	public async Task<AiExecutableScenarioDraft> DraftExecutableScenarioAsync(string objective, GameTarget target, AdapterCapabilities capabilities, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(objective))
		{
			throw new ArgumentException("테스트 목표를 먼저 입력하세요.");
		}
		RouterConnectionStatus routerConnectionStatus = await ReadStatusAsync(cancellationToken);
		if (!routerConnectionStatus.InferenceAvailable)
		{
			throw new InvalidOperationException(routerConnectionStatus.Message);
		}
		string[] allowed = capabilities.Actions.OrderBy<string, string>((string x) => x, StringComparer.Ordinal).ToArray();
		string content = "You draft bounded game QA scenarios as JSON only. Schema: {id,title,objective,requiredCapabilities:string[],steps:[{id,action,arguments:{string:string}}],timeoutSeconds}. Use only action names in the supplied adapter allowlist. Never include shell/console commands, file paths, arbitrary code, external network actions, or production-world mutations. Keep to 1-10 steps and a timeout <=120 seconds. This is a proposal; do not claim execution.";
		string content2 = JsonSerializer.Serialize(new
		{
			objective = objective,
			target = new { target.TargetId, target.GameId, target.Edition, target.GameVersion, target.RuntimeKind, target.Ownership },
			adapterCapabilities = allowed
		});
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(45.0));
		using HttpResponseMessage response = await _003Chttp_003EP.PostAsJsonAsync("http://127.0.0.1:18880/v1/chat/completions", new
		{
			messages = new[]
			{
				new
				{
					role = "system",
					content = content
				},
				new
				{
					role = "user",
					content = content2
				}
			},
			temperature = 0.1,
			max_tokens = 1200
		}, JsonOptions, timeout.Token);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Router/provider request failed (HTTP {response.StatusCode}); no game action was run.");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(await ReadBoundedAsync(response.Content, 65536, timeout.Token));
		if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object || !jsonDocument.RootElement.TryGetProperty("choices", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("Router response contained no assistant suggestion.");
		}
		if (value[0].ValueKind != JsonValueKind.Object || !value[0].TryGetProperty("message", out var value2) || value2.ValueKind != JsonValueKind.Object || !value2.TryGetProperty("content", out var value3) || value3.ValueKind != JsonValueKind.String)
		{
			throw new InvalidOperationException("Router response had an invalid assistant message shape.");
		}
		string @string = value3.GetString();
		if (string.IsNullOrWhiteSpace(@string) || @string.Length > 16000)
		{
			throw new InvalidOperationException("AI scenario is empty or too large.");
		}
		using JsonDocument jsonDocument2 = JsonDocument.Parse(@string);
		JsonElement rootElement = jsonDocument2.RootElement;
		string text = BoundedString(rootElement, "id", 90);
		if (!Regex.IsMatch(text, "^enderman\\.[a-z0-9._-]{1,80}$"))
		{
			throw new InvalidOperationException("AI scenario id is outside the Enderman namespace.");
		}
		string title = BoundedString(rootElement, "title", 120);
		string objective2 = BoundedString(rootElement, "objective", 1500);
		if (!rootElement.TryGetProperty("timeoutSeconds", out var value4) || !value4.TryGetInt32(out var value5) || value5 < 1 || value5 > 120)
		{
			throw new InvalidOperationException("AI scenario timeout must be 1–120 seconds.");
		}
		int timeoutSeconds = value5;
		IReadOnlyList<string> requiredCapabilities = ReadStringArray(rootElement, "requiredCapabilities", 10, 100);
		bool flag = !rootElement.TryGetProperty("steps", out var value6) || value6.ValueKind != JsonValueKind.Array;
		if (!flag)
		{
			int arrayLength = value6.GetArrayLength();
			bool flag2 = ((arrayLength < 1 || arrayLength > 10) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("AI scenario must contain 1–10 actions.");
		}
		List<ScenarioStep> list = new List<ScenarioStep>();
		foreach (JsonElement item in value6.EnumerateArray())
		{
			string id = BoundedString(item, "id", 80);
			string text2 = BoundedString(item, "action", 80);
			if (!allowed.Contains<string>(text2, StringComparer.Ordinal))
			{
				throw new InvalidOperationException("AI scenario action is unsupported by the selected adapter: " + text2);
			}
			if (!item.TryGetProperty("arguments", out var value7) || value7.ValueKind != JsonValueKind.Object || value7.EnumerateObject().Count() > 10)
			{
				throw new InvalidOperationException("AI action arguments must be a small string map.");
			}
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (JsonProperty item2 in value7.EnumerateObject())
			{
				int arrayLength = item2.Name.Length;
				flag = ((arrayLength < 1 || arrayLength > 80) ? true : false);
				if (flag || item2.Value.ValueKind != JsonValueKind.String || (item2.Value.GetString()?.Length ?? 0) > 300)
				{
					throw new InvalidOperationException("AI action arguments must contain short string values only.");
				}
				dictionary.Add(item2.Name, item2.Value.GetString() ?? "");
			}
			list.Add(new ScenarioStep(id, text2, dictionary));
		}
		string providerModel = String(jsonDocument.RootElement, "model", "Router-selected provider");
		return new AiExecutableScenarioDraft(new QaScenario(text, 1, title, objective2, requiredCapabilities, list, timeoutSeconds), providerModel);
	}

	private static string String(JsonElement root, string key, string fallback)
	{
		if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return fallback;
		}
		return value.GetString() ?? fallback;
	}

	private static int? Int(JsonElement root, string key)
	{
		if (!root.TryGetProperty(key, out var value) || !value.TryGetInt32(out var value2))
		{
			return null;
		}
		return value2;
	}

	private static IReadOnlyList<string> ReadStringArray(JsonElement root, string key, int maxCount, int maxLength)
	{
		if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maxCount)
		{
			throw new InvalidOperationException($"AI field '{key}' must be an array with at most {maxCount} entries.");
		}
		string[] array = (from item in value.EnumerateArray()
			select (item.ValueKind == JsonValueKind.String) ? (item.GetString()?.Trim() ?? "") : "").ToArray();
		if (array.Any((string item) => item.Length < 1 || item.Length > maxLength))
		{
			throw new InvalidOperationException("AI field '" + key + "' contains an invalid value.");
		}
		return array;
	}

	private static string BoundedString(JsonElement root, string key, int limit)
	{
		if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
		{
			throw new InvalidOperationException("AI suggestion is missing '" + key + "'.");
		}
		string text = (value.GetString() ?? "").Trim();
		if (text.Length > limit)
		{
			throw new InvalidOperationException("AI field '" + key + "' is too long.");
		}
		return text;
	}

	private static async Task<string> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
	{
		long? contentLength = content.Headers.ContentLength;
		if (contentLength.HasValue && contentLength.Value > maximumBytes)
		{
			throw new InvalidOperationException("Router response exceeded the 64 KB safety limit.");
		}
		Stream stream = await content.ReadAsStreamAsync(cancellationToken);
		string @string;
		try
		{
			using MemoryStream buffer = new MemoryStream();
			byte[] chunk = new byte[8192];
			while (true)
			{
				int num = await stream.ReadAsync(chunk, cancellationToken);
				if (num == 0)
				{
					break;
				}
				if (buffer.Length + num > maximumBytes)
				{
					throw new InvalidOperationException("Router response exceeded the 64 KB safety limit.");
				}
				buffer.Write(chunk, 0, num);
			}
			@string = Encoding.UTF8.GetString(buffer.ToArray());
		}
		finally
		{
			if (stream != null)
			{
				await stream.DisposeAsync();
			}
		}
		return @string;
	}
}
