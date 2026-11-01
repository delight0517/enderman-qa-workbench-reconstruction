using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

public sealed class FabricClientBridgeClient : IDisposable
{
	private sealed record ClientDescriptor(int ApiVersion, string AdapterId, string GameVersion, string Host, int Port, string Token, string TargetId, string InstanceId, long ProcessId, string CreatedAt);

	private const int MaxDescriptorBytes = 16384;

	private const int MaxResponseBytes = 65536;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;

	private readonly Uri _baseUri;

	private readonly string _token;

	private readonly string _targetId;

	private readonly string _instanceId;

	private readonly long _processId;

	private readonly string _javaExecutable;

	private bool _disposed;

	private FabricClientBridgeClient(HttpClient http, Uri baseUri, string token, string targetId, string instanceId, long processId, string javaExecutable)
	{
		_http = http;
		_baseUri = baseUri;
		_token = token;
		_targetId = targetId;
		_instanceId = instanceId;
		_processId = processId;
		_javaExecutable = javaExecutable;
	}

	public static async Task<FabricClientBridgeClient> ConnectOwnedAsync(string descriptorPath, string sessionRoot, string instanceRoot, string instanceId, Process process, string expectedJavaExecutable, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(process, "process");
		if (process.HasExited)
		{
			throw new InvalidOperationException("Owned Minecraft client has exited.");
		}
		string fullPath = Path.GetFullPath(instanceRoot);
		string fullPath2 = Path.GetFullPath(sessionRoot);
		string relativePath = Path.GetRelativePath(fullPath, fullPath2);
		if (!Path.IsPathRooted(relativePath) && !(relativePath == ".."))
		{
			ReadOnlySpan<char> readOnlySpan = "..";
			char reference = Path.DirectorySeparatorChar;
			if (!relativePath.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.Ordinal))
			{
				string fullPath3 = Path.GetFullPath(Path.Combine(fullPath2, "endermanqa-client-bridge.json"));
				if (!Path.GetFullPath(descriptorPath).Equals(fullPath3, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("Client bridge descriptor must be inside the exact owned session directory.");
				}
				string actualJava = Path.GetFullPath(process.MainModule?.FileName ?? process.StartInfo.FileName);
				if (!actualJava.Equals(Path.GetFullPath(expectedJavaExecutable), StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("Minecraft client executable does not match the selected Java runtime.");
				}
				if (process.Id <= 0 || !Directory.Exists(fullPath2) || !File.Exists(fullPath3))
				{
					throw new InvalidOperationException("Owned client process, session directory, or descriptor is unavailable.");
				}
				long length = new FileInfo(fullPath3).Length;
				if ((length <= 0 || length > 16384) ? true : false)
				{
					throw new InvalidDataException("Client bridge descriptor is empty or exceeds 16 KiB.");
				}
				using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(fullPath3, cancellationToken));
				ClientDescriptor descriptor = json.RootElement.Deserialize<ClientDescriptor>(JsonOptions) ?? throw new InvalidDataException("Client bridge descriptor is malformed.");
				string requestedInstance = instanceId.ToLowerInvariant();
				bool flag = descriptor.ApiVersion != 1 || descriptor.AdapterId != "minecraft-java-fabric-client" || descriptor.GameVersion != "26.3" || descriptor.ProcessId != process.Id || descriptor.InstanceId != requestedInstance || descriptor.Host != "127.0.0.1";
				if (!flag)
				{
					int port = descriptor.Port;
					bool flag2 = ((port < 1 || port > 65535) ? true : false);
					flag = flag2;
				}
				if (flag || !IsHex(descriptor.Token, 64) || !IsHex(descriptor.TargetId, 32))
				{
					throw new InvalidDataException("Client bridge descriptor identity, version or loopback endpoint did not validate.");
				}
				HttpClient http = new HttpClient(new HttpClientHandler
				{
					AllowAutoRedirect = false,
					UseCookies = false,
					UseProxy = false
				}, disposeHandler: true)
				{
					Timeout = TimeSpan.FromSeconds(5.0)
				};
				FabricClientBridgeClient bridge = new FabricClientBridgeClient(http, new Uri($"http://127.0.0.1:{descriptor.Port}/api/v1/"), descriptor.Token, descriptor.TargetId, descriptor.InstanceId, descriptor.ProcessId, actualJava);
				try
				{
					FabricClientBridgeStatus fabricClientBridgeStatus = await bridge.GetStatusAsync(cancellationToken);
					if (fabricClientBridgeStatus.ApiVersion != 1 || fabricClientBridgeStatus.AdapterId != descriptor.AdapterId || fabricClientBridgeStatus.ProcessId != process.Id || fabricClientBridgeStatus.TargetId != descriptor.TargetId || fabricClientBridgeStatus.InstanceId != requestedInstance || fabricClientBridgeStatus.GameVersion != "26.3")
					{
						throw new InvalidOperationException("Live client status does not match the owned process and descriptor.");
					}
					if (process.HasExited)
					{
						throw new InvalidOperationException("Minecraft client exited while connecting to its bridge.");
					}
					return bridge;
				}
				catch
				{
					bridge.Dispose();
					throw;
				}
			}
		}
		throw new InvalidOperationException("Client bridge session directory is outside the selected Workbench instance.");
	}

	public async Task<FabricClientBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, "status"));
		using HttpResponseMessage response = await SendAsync(request, cancellationToken);
		return await ReadJsonAsync<FabricClientBridgeStatus>(response, cancellationToken);
	}

	public Task<FabricClientActionReceipt> ObserveEndermanAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendActionAsync("observe_enderman", entityId, entityUuid, cancellationToken);
	}

	public Task<FabricClientActionReceipt> AttackEndermanAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendActionAsync("attack_enderman", entityId, entityUuid, cancellationToken);
	}

	public Task<FabricClientActionReceipt> CacheEndermanTargetAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendActionAsync("cache_enderman_target", entityId, entityUuid, cancellationToken);
	}

	public Task<FabricClientActionReceipt> AttackCachedEndermanAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendActionAsync("attack_cached_enderman", entityId, entityUuid, cancellationToken);
	}

	public Task<FabricClientActionReceipt> UseMainHandAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendActionAsync("use_main_hand", entityId, entityUuid, cancellationToken);
	}

	public Task<FabricClientActionReceipt> ReleaseMainHandAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendActionAsync("release_main_hand", entityId, entityUuid, cancellationToken);
	}

	public Task<FabricClientActionReceipt> AimAtEndermanAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendActionAsync("aim_at_enderman", entityId, entityUuid, cancellationToken);
	}

	public async Task<FabricClientCaptureReceipt> CaptureFramebufferAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		string requestId = Guid.NewGuid().ToString("D");
		Uri requestUri = new Uri(_baseUri, "../v2/captures");
		FabricClientCaptureReceipt receipt = null;
		try
		{
			using HttpRequestMessage request2 = new HttpRequestMessage(HttpMethod.Post, requestUri)
			{
				Content = new StringContent(JsonSerializer.Serialize(new { requestId }, JsonOptions), Encoding.UTF8, "application/json")
			};
			using HttpResponseMessage response2 = await SendAsync(request2, cancellationToken);
			receipt = await ReadJsonAsync<FabricClientCaptureReceipt>(response2, cancellationToken);
			ValidateCaptureReceipt(receipt, requestId);
		}
		catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			receipt = new FabricClientCaptureReceipt(2, requestId, "unknown_reconcile", null, null, null, null, null, "capture-request-timeout; polling the same request ID", null);
		}
		if ((object)receipt == null)
		{
			throw new InvalidDataException("Capture request returned no receipt.");
		}
		if (IsTerminalCaptureState(receipt.State))
		{
			return receipt;
		}
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20.0);
		Uri pollUri = new Uri(_baseUri, "../v2/captures/" + Uri.EscapeDataString(requestId));
		while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(250.0), cancellationToken);
			try
			{
				using HttpRequestMessage request2 = new HttpRequestMessage(HttpMethod.Get, pollUri);
				using HttpResponseMessage response2 = await SendAsync(request2, cancellationToken);
				FabricClientCaptureReceipt fabricClientCaptureReceipt = await ReadJsonAsync<FabricClientCaptureReceipt>(response2, cancellationToken);
				ValidateCaptureReceipt(fabricClientCaptureReceipt, requestId);
				receipt = fabricClientCaptureReceipt;
				if (IsTerminalCaptureState(receipt.State))
				{
					return receipt;
				}
			}
			catch (HttpRequestException)
			{
			}
			catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			}
		}
		return receipt with
		{
			State = "unknown_reconcile"
		};
	}

	private static void ValidateCaptureReceipt(FabricClientCaptureReceipt receipt, string requestId)
	{
		bool flag = receipt.ApiVersion != 2 || !Guid.TryParse(receipt.RequestId, out var result) || !result.ToString("D").Equals(requestId, StringComparison.OrdinalIgnoreCase);
		bool flag2;
		if (!flag)
		{
			switch (receipt.State)
			{
			case "queued":
			case "capturing":
			case "completed":
			case "failed":
			case "rejected":
			case "unknown_reconcile":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = !flag2;
		}
		if (flag)
		{
			throw new InvalidDataException("Minecraft capture receipt failed API v2 identity/state validation.");
		}
		if (receipt.State != "completed")
		{
			return;
		}
		flag = receipt.FileName != "capture-" + requestId + ".png";
		bool flag3;
		if (!flag)
		{
			int? framebufferWidth = receipt.FramebufferWidth;
			if (framebufferWidth.HasValue)
			{
				int valueOrDefault = framebufferWidth.GetValueOrDefault();
				if (valueOrDefault < 1 || valueOrDefault > 8192)
				{
					flag3 = true;
					goto IL_0105;
				}
			}
			flag3 = false;
			goto IL_0105;
		}
		goto IL_0108;
		IL_013d:
		flag2 = flag3;
		goto IL_0140;
		IL_0140:
		bool flag4 = flag2;
		if (!flag4)
		{
			long? bytes = receipt.Bytes;
			if (bytes.HasValue)
			{
				long valueOrDefault2 = bytes.GetValueOrDefault();
				if (valueOrDefault2 < 1 || valueOrDefault2 > 52428800)
				{
					flag3 = true;
					goto IL_0179;
				}
			}
			flag3 = false;
			goto IL_0179;
		}
		goto IL_017d;
		IL_0108:
		flag2 = flag;
		if (!flag2)
		{
			int? framebufferWidth = receipt.FramebufferHeight;
			if (framebufferWidth.HasValue)
			{
				int valueOrDefault = framebufferWidth.GetValueOrDefault();
				if (valueOrDefault < 1 || valueOrDefault > 8192)
				{
					flag3 = true;
					goto IL_013d;
				}
			}
			flag3 = false;
			goto IL_013d;
		}
		goto IL_0140;
		IL_0179:
		flag4 = flag3;
		goto IL_017d;
		IL_0105:
		flag = flag3;
		goto IL_0108;
		IL_017d:
		if (flag4 || receipt.Sha256 == null || receipt.Sha256.Length != 64 || !receipt.Sha256.All(Uri.IsHexDigit))
		{
			throw new InvalidDataException("Completed Minecraft capture receipt exceeds bounds or has invalid metadata.");
		}
	}

	private static bool IsTerminalCaptureState(string state)
	{
		switch (state)
		{
		case "completed":
		case "failed":
		case "rejected":
			return true;
		default:
			return false;
		}
	}

	public async Task<FabricClientStopReceipt> RequestStopAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "lifecycle/stop"));
		using HttpResponseMessage response = await SendAsync(request, cancellationToken);
		FabricClientStopReceipt fabricClientStopReceipt = await ReadJsonAsync<FabricClientStopReceipt>(response, cancellationToken);
		if (!fabricClientStopReceipt.Accepted || fabricClientStopReceipt.ProcessId != _processId)
		{
			throw new InvalidDataException("Client stop receipt did not match the owned process.");
		}
		return fabricClientStopReceipt;
	}

	private async Task<FabricClientActionReceipt> SendActionAsync(string action, int entityId, string entityUuid, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (entityId <= 0 || !Guid.TryParse(entityUuid, out var _))
		{
			throw new ArgumentException("A positive entity ID and UUID are required.");
		}
		string requestId = Guid.NewGuid().ToString("D");
		string content = JsonSerializer.Serialize(new { action, requestId, entityId, entityUuid }, JsonOptions);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "actions"))
		{
			Content = new StringContent(content, Encoding.UTF8, "application/json")
		};
		using HttpResponseMessage response = await SendAsync(request, cancellationToken);
		return await ReadJsonAsync<FabricClientActionReceipt>(response, cancellationToken);
	}

	private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
		HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			response.Dispose();
			throw new InvalidOperationException("Minecraft client bridge rejected its private session token.");
		}
		if (!response.IsSuccessStatusCode)
		{
			int status = (int)response.StatusCode;
			string value = await ReadBodyAsync(response.Content, 65536, cancellationToken);
			response.Dispose();
			throw new InvalidOperationException($"Minecraft client action failed (HTTP {status}); reconcile its request ID/server receipt before retrying. {value}");
		}
		return response;
	}

	private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		T val = JsonSerializer.Deserialize<T>(await ReadBodyAsync(response.Content, 65536, cancellationToken), JsonOptions);
		if (val == null)
		{
			throw new InvalidDataException("Minecraft client bridge returned an empty response.");
		}
		return val;
	}

	private static async Task<string> ReadBodyAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
	{
		long? contentLength = content.Headers.ContentLength;
		if (contentLength.HasValue)
		{
			long valueOrDefault = contentLength.GetValueOrDefault();
			if (valueOrDefault > maximumBytes)
			{
				throw new InvalidDataException("Minecraft client response exceeded its byte limit.");
			}
		}
		Stream stream = await content.ReadAsStreamAsync(cancellationToken);
		string @string;
		try
		{
			using MemoryStream buffer = new MemoryStream();
			byte[] chunk = new byte[4096];
			while (true)
			{
				int num = await stream.ReadAsync(chunk, cancellationToken);
				if (num == 0)
				{
					break;
				}
				if (buffer.Length + num > maximumBytes)
				{
					throw new InvalidDataException("Minecraft client response exceeded its byte limit.");
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

	private static bool IsHex(string value, int length)
	{
		if (value.Length == length)
		{
			return value.All(Uri.IsHexDigit);
		}
		return false;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_http.Dispose();
			GC.KeepAlive(_token);
		}
	}
}
