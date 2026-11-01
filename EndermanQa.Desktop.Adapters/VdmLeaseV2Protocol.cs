using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndermanQa.Desktop.Adapters;

internal static class VdmLeaseV2Protocol
{
	internal sealed record Request(string Action, Guid RequestId, string Nonce, Guid RunId, int WorkbenchPid, Guid ClientSessionId, string InstanceId, string? LeaseId, byte[] Utf8);

	internal sealed record LeaseProof(Guid RequestId, string LeaseId, Guid RunId, int WorkbenchPid, Guid ClientSessionId, string InstanceId, string DevicePath, string TopologyGeneration, IReadOnlyList<string> Scope, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc, int RevocationGeneration);

	internal sealed record Denial(string ErrorCode);

	internal const int Version = 2;

	internal const int MaximumFrameBytes = 8192;

	internal const int MaximumTtlSeconds = 120;

	internal const string ConsumerId = "enderman-qa-workbench";

	internal const string PipeName = "OnlyChatAiTaskDisplayLease.v2";

	internal static readonly string[] RequiredScope = new string[3] { "launch-client", "client-input", "framebuffer-capture" };

	private static readonly HashSet<string> AllowedScope = new HashSet<string>(StringComparer.Ordinal) { "launch-client", "client-input", "framebuffer-capture" };

	private static readonly HashSet<string> AcquireRequestFields = new HashSet<string>(StringComparer.Ordinal)
	{
		"version", "request_id", "nonce", "action", "consumer_id", "run_id", "workbench_pid", "client_session_id", "instance_id", "requested_scope",
		"requested_ttl_seconds"
	};

	private static readonly HashSet<string> LeaseRequestFields = new HashSet<string>(StringComparer.Ordinal) { "version", "request_id", "nonce", "action", "consumer_id", "run_id", "workbench_pid", "client_session_id", "instance_id", "lease_id" };

	private static readonly HashSet<string> LeaseReplyFields = new HashSet<string>(StringComparer.Ordinal)
	{
		"version", "request_id", "nonce", "state", "lease_id", "run_id", "workbench_pid", "client_session_id", "instance_id", "display",
		"capture_scope", "issued_at_utc", "expires_at_utc", "revocation_generation"
	};

	internal static Request CreateAcquire(Guid runId, int workbenchPid, Guid clientSessionId, string instanceId, int ttlSeconds = 120)
	{
		if (runId == Guid.Empty || clientSessionId == Guid.Empty || workbenchPid <= 0)
		{
			throw new ArgumentException("Lease owner identifiers must be initialized.");
		}
		if ((ttlSeconds < 1 || ttlSeconds > 120) ? true : false)
		{
			throw new ArgumentOutOfRangeException("ttlSeconds");
		}
		string normalizedInstanceId = WorkbenchInstancePaths.ValidateInstanceId(instanceId);
		Guid requestId = Guid.NewGuid();
		string text = Base64Url(RandomNumberGenerator.GetBytes(32));
		Dictionary<string, object> payload = new Dictionary<string, object>
		{
			["version"] = 2,
			["request_id"] = requestId.ToString("D"),
			["nonce"] = text,
			["action"] = "acquire",
			["consumer_id"] = "enderman-qa-workbench",
			["run_id"] = runId.ToString("D"),
			["workbench_pid"] = workbenchPid,
			["client_session_id"] = clientSessionId.ToString("D"),
			["instance_id"] = normalizedInstanceId,
			["requested_scope"] = RequiredScope,
			["requested_ttl_seconds"] = ttlSeconds
		};
		return new Request("acquire", requestId, text, runId, workbenchPid, clientSessionId, normalizedInstanceId, null, SerializeBounded(payload));
	}

	internal static Request CreateLeaseAction(string action, Guid runId, int workbenchPid, Guid clientSessionId, string instanceId, string leaseId)
	{
		bool flag;
		switch (action)
		{
		case "validate":
		case "renew":
		case "release":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentOutOfRangeException("action");
		}
		if (runId == Guid.Empty || clientSessionId == Guid.Empty || workbenchPid <= 0 || !Guid.TryParseExact(leaseId, "D", out var _))
		{
			throw new ArgumentException("Lease owner identifiers or lease ID are invalid.");
		}
		string normalizedInstanceId = WorkbenchInstancePaths.ValidateInstanceId(instanceId);
		Guid requestId = Guid.NewGuid();
		string text = Base64Url(RandomNumberGenerator.GetBytes(32));
		Dictionary<string, object> payload = new Dictionary<string, object>
		{
			["version"] = 2,
			["request_id"] = requestId.ToString("D"),
			["nonce"] = text,
			["action"] = action,
			["consumer_id"] = "enderman-qa-workbench",
			["run_id"] = runId.ToString("D"),
			["workbench_pid"] = workbenchPid,
			["client_session_id"] = clientSessionId.ToString("D"),
			["instance_id"] = normalizedInstanceId,
			["lease_id"] = leaseId
		};
		return new Request(action, requestId, text, runId, workbenchPid, clientSessionId, normalizedInstanceId, leaseId, SerializeBounded(payload));
	}

	internal static object ParseReply(ReadOnlySpan<byte> utf8, Request request, DateTimeOffset nowUtc, int? previousRevocationGeneration = null)
	{
		int length = utf8.Length;
		if ((length <= 0 || length > 8192) ? true : false)
		{
			throw new InvalidDataException("VDM lease reply size is outside the 8 KiB frame limit.");
		}
		RejectDuplicateProperties(utf8);
		using JsonDocument jsonDocument = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
		{
			AllowTrailingCommas = false,
			CommentHandling = JsonCommentHandling.Disallow,
			MaxDepth = 12
		});
		JsonElement rootElement = jsonDocument.RootElement;
		if (rootElement.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException("VDM lease reply must be a JSON object.");
		}
		VerifyCorrelation(rootElement, request);
		string text = RequiredString(rootElement, "state");
		if (text == "denied")
		{
			RequireExactProperties(rootElement, new _003C_003Ez__ReadOnlyArray<string>(new string[5] { "version", "request_id", "nonce", "state", "error_code" }));
			return new Denial(RequiredString(rootElement, "error_code"));
		}
		if (request.Action == "release")
		{
			RequireExactProperties(rootElement, new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "version", "request_id", "nonce", "state", "lease_id", "run_id" }));
			if (text != "released" || RequiredString(rootElement, "lease_id") != request.LeaseId || RequiredString(rootElement, "run_id") != request.RunId.ToString("D"))
			{
				throw new InvalidDataException("VDM release reply does not match the exact lease owner.");
			}
			return new Denial("RELEASED");
		}
		RequireExactProperties(rootElement, LeaseReplyFields);
		if (text != ((request.Action == "acquire") ? "acquired" : "valid"))
		{
			throw new InvalidDataException("VDM lease reply state does not match the request action.");
		}
		string text2 = RequiredString(rootElement, "lease_id");
		if (!Guid.TryParseExact(text2, "D", out var _) || (request.LeaseId != null && !text2.Equals(request.LeaseId, StringComparison.Ordinal)))
		{
			throw new InvalidDataException("VDM returned a malformed or unexpected lease ID.");
		}
		if (RequiredString(rootElement, "run_id") != request.RunId.ToString("D") || RequiredInt32(rootElement, "workbench_pid") != request.WorkbenchPid || RequiredString(rootElement, "client_session_id") != request.ClientSessionId.ToString("D") || RequiredString(rootElement, "instance_id") != request.InstanceId)
		{
			throw new InvalidDataException("VDM lease reply owner/run/session/instance binding mismatch.");
		}
		JsonElement element = RequiredObject(rootElement, "display");
		RequireExactProperties(element, new _003C_003Ez__ReadOnlyArray<string>(new string[5] { "device_path", "active", "mirrored", "visible_on_physical_output", "topology_generation" }));
		string text3 = RequiredString(element, "device_path");
		if (!text3.StartsWith("\\\\?\\DISPLAY#", StringComparison.OrdinalIgnoreCase) || !RequiredBoolean(element, "active") || RequiredBoolean(element, "mirrored") || RequiredBoolean(element, "visible_on_physical_output"))
		{
			throw new InvalidDataException("VDM did not prove one active, unmirrored, nonphysical display path.");
		}
		string topologyGeneration = RequiredString(element, "topology_generation");
		string[] scope = RequiredArray(rootElement, "capture_scope").EnumerateArray().Select(delegate(JsonElement item)
		{
			if (item.ValueKind != JsonValueKind.String)
			{
				throw new InvalidDataException("VDM lease scope contains a non-string value.");
			}
			return item.GetString();
		}).ToArray();
		if (scope.Length != RequiredScope.Length || scope.Distinct<string>(StringComparer.Ordinal).Count() != scope.Length || scope.Any((string item) => !AllowedScope.Contains(item)) || RequiredScope.Any((string item) => !scope.Contains<string>(item, StringComparer.Ordinal)))
		{
			throw new InvalidDataException("VDM lease scope does not exactly cover the requested client actions.");
		}
		DateTimeOffset dateTimeOffset = ParseUtc(RequiredString(rootElement, "issued_at_utc"));
		DateTimeOffset dateTimeOffset2 = ParseUtc(RequiredString(rootElement, "expires_at_utc"));
		TimeSpan timeSpan = dateTimeOffset2 - dateTimeOffset;
		if (timeSpan <= TimeSpan.Zero || timeSpan > TimeSpan.FromSeconds(120.0) || dateTimeOffset > nowUtc.AddSeconds(2.0) || dateTimeOffset2 <= nowUtc)
		{
			throw new InvalidDataException("VDM lease is expired, future-dated, or exceeds the 120-second maximum TTL.");
		}
		int num = RequiredInt32(rootElement, "revocation_generation");
		if (num <= 0 || (previousRevocationGeneration.HasValue && num < previousRevocationGeneration.Value))
		{
			throw new InvalidDataException("VDM revocation generation moved backward or is invalid.");
		}
		return new LeaseProof(request.RequestId, text2, request.RunId, request.WorkbenchPid, request.ClientSessionId, request.InstanceId, text3, topologyGeneration, scope, dateTimeOffset, dateTimeOffset2, num);
	}

	internal static object ParseReply(byte[] utf8, Request request, DateTimeOffset nowUtc, int? previousRevocationGeneration = null)
	{
		ArgumentNullException.ThrowIfNull(utf8);
		return ParseReply((ReadOnlySpan<byte>)utf8, request, nowUtc, previousRevocationGeneration);
	}

	private static byte[] SerializeBounded(Dictionary<string, object> payload)
	{
		byte[] array = JsonSerializer.SerializeToUtf8Bytes(payload);
		if (array.Length + 1 > 8192)
		{
			throw new InvalidDataException("VDM lease request exceeds 8 KiB.");
		}
		byte[] array2 = new byte[array.Length + 1];
		array.CopyTo(array2, 0);
		array2[^1] = 10;
		return array2;
	}

	private static string Base64Url(byte[] value)
	{
		return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');
	}

	private static DateTimeOffset ParseUtc(string value)
	{
		if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result) || !value.EndsWith('Z'))
		{
			throw new InvalidDataException("VDM lease timestamps must be UTC ISO-8601 values ending in Z.");
		}
		return result;
	}

	private static void VerifyCorrelation(JsonElement root, Request request)
	{
		if (RequiredInt32(root, "version") != 2 || RequiredString(root, "request_id") != request.RequestId.ToString("D") || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(RequiredString(root, "nonce")), Encoding.ASCII.GetBytes(request.Nonce)))
		{
			throw new InvalidDataException("VDM lease reply correlation, nonce, or protocol version mismatch.");
		}
	}

	private static string RequiredString(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			throw new InvalidDataException("VDM lease reply is missing string '" + name + "'.");
		}
		return value.GetString();
	}

	private static int RequiredInt32(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var value2))
		{
			throw new InvalidDataException("VDM lease reply is missing integer '" + name + "'.");
		}
		return value2;
	}

	private static bool RequiredBoolean(JsonElement element, string name)
	{
		JsonElement value;
		bool flag = element.TryGetProperty(name, out value);
		if (flag)
		{
			JsonValueKind valueKind = value.ValueKind;
			bool flag2 = valueKind - 5 <= JsonValueKind.Object;
			flag = flag2;
		}
		if (!flag)
		{
			throw new InvalidDataException("VDM lease reply is missing boolean '" + name + "'.");
		}
		return value.GetBoolean();
	}

	private static JsonElement RequiredObject(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException("VDM lease reply is missing object '" + name + "'.");
		}
		return value;
	}

	private static JsonElement RequiredArray(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("VDM lease reply is missing array '" + name + "'.");
		}
		return value;
	}

	private static void RequireExactProperties(JsonElement element, IEnumerable<string> expected)
	{
		if (!(from property in element.EnumerateObject()
			select property.Name).ToHashSet<string>(StringComparer.Ordinal).SetEquals(expected))
		{
			throw new InvalidDataException("VDM lease reply has missing or unexpected fields.");
		}
	}

	private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8)
	{
		Utf8JsonReader utf8JsonReader = new Utf8JsonReader(utf8, new JsonReaderOptions
		{
			MaxDepth = 12
		});
		Stack<HashSet<string>> stack = new Stack<HashSet<string>>();
		while (utf8JsonReader.Read())
		{
			if (utf8JsonReader.TokenType == JsonTokenType.StartObject)
			{
				stack.Push(new HashSet<string>(StringComparer.Ordinal));
			}
			else if (utf8JsonReader.TokenType == JsonTokenType.EndObject)
			{
				stack.Pop();
			}
			else if (utf8JsonReader.TokenType == JsonTokenType.PropertyName && (stack.Count == 0 || !stack.Peek().Add(utf8JsonReader.GetString())))
			{
				throw new InvalidDataException("VDM lease reply contains a duplicate JSON property.");
			}
		}
	}
}
