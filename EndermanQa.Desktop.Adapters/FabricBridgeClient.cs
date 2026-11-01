using System;
using System.Collections.Generic;
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

public sealed class FabricBridgeClient : IGameObserver, IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;

	private readonly Uri _baseUri;

	private readonly string _token;

	private FabricBridgeClient(HttpClient http, Uri baseUri, string token)
	{
		_http = http;
		_baseUri = baseUri;
		_token = token;
	}

	public static FabricBridgeClient FromDescriptor(string descriptorPath)
	{
		return FromDescriptorCore(descriptorPath, null);
	}

	internal static FabricBridgeClient FromDescriptorForTesting(string descriptorPath, HttpMessageHandler handler)
	{
		return FromDescriptorCore(descriptorPath, handler);
	}

	private static FabricBridgeClient FromDescriptorCore(string descriptorPath, HttpMessageHandler? injectedHandler)
	{
		if (!string.Equals(Path.GetFileName(descriptorPath), "endermanqa-bridge.json", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Select the bridge descriptor from an Enderman QA Fabric server directory.");
		}
		FileInfo fileInfo = new FileInfo(descriptorPath);
		if (!fileInfo.Exists || fileInfo.Length > 16384)
		{
			throw new InvalidOperationException("Fabric bridge descriptor is missing or larger than 16 KB.");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(descriptorPath));
		JsonElement rootElement = jsonDocument.RootElement;
		if (rootElement.ValueKind != JsonValueKind.Object || GetInt(rootElement, "apiVersion") != 1 || GetString(rootElement, "adapterId") != "minecraft-java.fabric-bridge")
		{
			throw new InvalidOperationException("Fabric bridge descriptor API/adapter identity is unsupported.");
		}
		string @string = GetString(rootElement, "host");
		int @int = GetInt(rootElement, "port");
		string string2 = GetString(rootElement, "token");
		bool flag = @string != "127.0.0.1";
		if (!flag)
		{
			bool flag2 = ((@int < 1 || @int > 65535) ? true : false);
			flag = flag2;
		}
		if (flag || string2.Length != 64 || !string2.All(Uri.IsHexDigit))
		{
			throw new InvalidOperationException("Bridge descriptor must use loopback, a valid port and a 256-bit session token.");
		}
		HttpClient httpClient = new HttpClient(injectedHandler ?? new SocketsHttpHandler
		{
			UseProxy = false,
			AllowAutoRedirect = false,
			ConnectTimeout = TimeSpan.FromSeconds(3.0)
		});
		httpClient.Timeout = TimeSpan.FromSeconds(90.0);
		httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string2);
		return new FabricBridgeClient(httpClient, new Uri($"http://127.0.0.1:{@int}/"), string2);
	}

	public async Task<FabricObservationSnapshot> ObserveAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return new FabricObservationSnapshot(await GetStatusAsync(cancellationToken), await GetCollectionAsync<FabricEndermanObservation>("api/v1/endermen", "entities", 2097152, cancellationToken), await GetCollectionAsync<FabricDeathFact>("api/v1/deaths", "events", 2097152, cancellationToken), DateTimeOffset.UtcNow, "Read-only server telemetry. This client did not verify that the target process or world is task-owned/disposable and cannot execute actions or capture game pixels.");
	}

	public async Task<FabricBridgeStatus> GetStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		FabricBridgeStatus fabricBridgeStatus = await GetAsync<FabricBridgeStatus>("api/v1/status", 65536, cancellationToken);
		if (fabricBridgeStatus.ApiVersion != 1 || fabricBridgeStatus.AdapterId != "minecraft-java.fabric-bridge" || fabricBridgeStatus.Ownership != "unverified")
		{
			throw new InvalidOperationException("Bridge status did not match the supported observe-only identity.");
		}
		return fabricBridgeStatus;
	}

	public Task<IReadOnlyList<FabricProjectileObservation>> GetProjectilesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return GetCollectionAsync<FabricProjectileObservation>("api/v1/projectiles", "projectiles", 262144, cancellationToken);
	}

	public async Task<FabricClientArrowSessionSnapshot> BeginClientArrowSessionAsync(string sessionId, string ownerUuid, string targetUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Guid.TryParse(sessionId, out var result) || !Guid.TryParse(ownerUuid, out var result2) || !Guid.TryParse(targetUuid, out result2))
		{
			throw new ArgumentException("Arrow observation requires a pre-recorded session ID and exact client/Enderman UUIDs.");
		}
		sessionId = result.ToString("D");
		string content2 = JsonSerializer.Serialize(new { sessionId, ownerUuid, targetUuid }, JsonOptions);
		using StringContent content = new StringContent(content2, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _http.PostAsync(new Uri(_baseUri, "api/v1/client-arrow-sessions"), content, cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw new InvalidOperationException("Fabric bridge rejected the current session token.");
		}
		HttpStatusCode statusCode = response.StatusCode;
		if ((uint)(statusCode - 201) > 1u)
		{
			throw new InvalidOperationException($"Client arrow observer start was rejected (HTTP {response.StatusCode}).");
		}
		FabricClientArrowSessionSnapshot? obj = JsonSerializer.Deserialize<FabricClientArrowSessionSnapshot>(await ReadBoundedAsync(response.Content, 1048576, cancellationToken), JsonOptions) ?? throw new InvalidDataException("Client arrow session start response was empty.");
		ValidateClientArrowSession(obj, sessionId, ownerUuid, targetUuid);
		return obj;
	}

	public async Task<FabricClientArrowSessionSnapshot> GetClientArrowSessionAsync(string sessionId, string expectedOwnerUuid, string expectedTargetUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Guid.TryParse(sessionId, out var parsed))
		{
			throw new ArgumentException("Arrow session ID must be a GUID.", "sessionId");
		}
		using HttpResponseMessage response = await _http.GetAsync(new Uri(_baseUri, $"api/v1/client-arrow-sessions?sessionId={parsed:D}"), cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw new InvalidOperationException("Fabric bridge rejected the current session token.");
		}
		response.EnsureSuccessStatusCode();
		FabricClientArrowSessionSnapshot? obj = JsonSerializer.Deserialize<FabricClientArrowSessionSnapshot>(await ReadBoundedAsync(response.Content, 1048576, cancellationToken), JsonOptions) ?? throw new InvalidDataException("Client arrow session response was empty.");
		ValidateClientArrowSession(obj, parsed.ToString("D"), expectedOwnerUuid, expectedTargetUuid);
		return obj;
	}

	public async Task<FabricClientArrowSessionSnapshot> EndClientArrowSessionAsync(string sessionId, string expectedOwnerUuid, string expectedTargetUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Guid.TryParse(sessionId, out var parsed))
		{
			throw new ArgumentException("Arrow session ID must be a GUID.", "sessionId");
		}
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseUri, $"api/v1/client-arrow-sessions?sessionId={parsed:D}"));
		using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw new InvalidOperationException("Fabric bridge rejected the current session token.");
		}
		HttpStatusCode statusCode = response.StatusCode;
		if ((statusCode != HttpStatusCode.OK && statusCode != HttpStatusCode.Accepted) || 1 == 0)
		{
			throw new InvalidOperationException($"Client arrow observer stop was rejected (HTTP {response.StatusCode}).");
		}
		FabricClientArrowSessionSnapshot? obj = JsonSerializer.Deserialize<FabricClientArrowSessionSnapshot>(await ReadBoundedAsync(response.Content, 1048576, cancellationToken), JsonOptions) ?? throw new InvalidDataException("Client arrow session stop response was empty.");
		ValidateClientArrowSession(obj, parsed.ToString("D"), expectedOwnerUuid, expectedTargetUuid);
		return obj;
	}

	private static void ValidateClientArrowSession(FabricClientArrowSessionSnapshot snapshot, string sessionId, string ownerUuid, string targetUuid)
	{
		Guid result;
		bool flag = snapshot.ApiVersion != 1 || snapshot.Schema != "client-player-arrow-session-v1" || !string.Equals(snapshot.SessionId, sessionId, StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(snapshot.OwnerUuid, out result) || !Guid.TryParse(snapshot.TargetUuid, out result) || !string.Equals(snapshot.OwnerUuid, ownerUuid, StringComparison.OrdinalIgnoreCase) || !string.Equals(snapshot.TargetUuid, targetUuid, StringComparison.OrdinalIgnoreCase) || snapshot.ProjectileCount != snapshot.Projectiles.Count;
		if (!flag)
		{
			int projectileCount = snapshot.ProjectileCount;
			bool flag2 = ((projectileCount < 0 || projectileCount > 16) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			bool flag2;
			switch (snapshot.State)
			{
			case "active":
			case "closed":
			case "expired":
			case "owner-disconnected":
			case "start_pending":
			case "stop_pending":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag3 = !flag2;
		}
		if (flag3)
		{
			throw new InvalidDataException("Client arrow session identity, state, or bounds failed validation.");
		}
		foreach (FabricClientArrowObservation projectile in snapshot.Projectiles)
		{
			if (!Guid.TryParse(projectile.ProjectileUuid, out result) || projectile.ProjectileType != "minecraft:arrow" || !string.Equals(projectile.OwnerUuid, snapshot.OwnerUuid, StringComparison.OrdinalIgnoreCase) || projectile.Observations.Count > 256 || projectile.Observations.Zip(projectile.Observations.Skip(1), (FabricClientArrowSample a, FabricClientArrowSample b) => b.ServerTick > a.ServerTick).Any((bool ok) => !ok))
			{
				throw new InvalidDataException("Client arrow projectile identity, owner, or sample ordering failed validation.");
			}
		}
	}

	public async Task<FabricProjectileVolleyReceipt> ThrowProjectileVolleyAsync(string entityUuid, int projectileCount, CancellationToken cancellationToken = default(CancellationToken))
	{
		Guid result;
		bool flag = !Guid.TryParse(entityUuid, out result);
		if (!flag)
		{
			bool flag2 = ((projectileCount < 2 || projectileCount > 8) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new ArgumentException("A volley requires a managed Enderman UUID and 2–8 arrows.");
		}
		string requestId = Guid.NewGuid().ToString("D");
		string content2 = JsonSerializer.Serialize(new
		{
			action = "throw_test_projectile_volley",
			requestId = requestId,
			entityUuid = entityUuid,
			itemId = "arrow",
			projectileCount = projectileCount
		}, JsonOptions);
		using StringContent content = new StringContent(content2, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _http.PostAsync(new Uri(_baseUri, "api/v1/actions"), content, cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw new InvalidOperationException("Fabric bridge rejected the current session token.");
		}
		string text = await ReadBoundedAsync(response.Content, 65536, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Fabric projectile volley was rejected (HTTP {response.StatusCode}): {text}");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(text);
		FabricProjectileVolleyReceipt fabricProjectileVolleyReceipt = jsonDocument.RootElement.Deserialize<FabricProjectileVolleyReceipt>(JsonOptions) ?? throw new InvalidDataException("Fabric projectile volley receipt was empty.");
		if (fabricProjectileVolleyReceipt.ApiVersion != 1 || fabricProjectileVolleyReceipt.FixtureContract != "projectile-volley-fixture-v1" || fabricProjectileVolleyReceipt.Action != "throw_test_projectile_volley" || !fabricProjectileVolleyReceipt.RequestId.Equals(requestId, StringComparison.OrdinalIgnoreCase) || !fabricProjectileVolleyReceipt.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase) || fabricProjectileVolleyReceipt.ProjectileType != "arrow" || fabricProjectileVolleyReceipt.ProjectileCount != projectileCount || !fabricProjectileVolleyReceipt.EntityAdded || fabricProjectileVolleyReceipt.ServerTick < 0 || fabricProjectileVolleyReceipt.ProjectileUuids.Count != projectileCount || fabricProjectileVolleyReceipt.ProjectileUuids.Any((string id) => !Guid.TryParse(id, out var _)) || fabricProjectileVolleyReceipt.ProjectileUuids.Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != projectileCount || fabricProjectileVolleyReceipt.LaunchPositions.Count != projectileCount || fabricProjectileVolleyReceipt.InitialVelocities.Count != projectileCount || !float.IsFinite(fabricProjectileVolleyReceipt.TargetHealthAtLaunch) || fabricProjectileVolleyReceipt.TargetHealthAtLaunch <= 0f || fabricProjectileVolleyReceipt.LatencyCoverage != "none; server-origin fixture bypasses client transport")
		{
			throw new InvalidDataException("Fabric projectile volley receipt failed identity, bounds or coverage validation.");
		}
		return fabricProjectileVolleyReceipt;
	}

	public Task<IReadOnlyList<FabricDeathArtifactObservation>> GetDeathArtifactsAsync(string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Guid.TryParse(entityUuid, out var result))
		{
			throw new ArgumentException("A managed Enderman UUID is required for artifact observation.", "entityUuid");
		}
		return GetCollectionAsync<FabricDeathArtifactObservation>("api/v1/death-artifacts?entityUuid=" + Uri.EscapeDataString(result.ToString()), "artifacts", 524288, cancellationToken);
	}

	public async Task<FabricActionResponse> TeleportManagedEndermanForTc014Async(string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!Guid.TryParse(entityUuid, out var parsedEntity))
		{
			throw new ArgumentException("A managed Enderman UUID is required.", "entityUuid");
		}
		string entityUuid2 = parsedEntity.ToString("D");
		string requestId = Guid.NewGuid().ToString("D");
		string content2 = JsonSerializer.Serialize(new
		{
			action = "teleport_test_enderman",
			requestId = requestId,
			entityUuid = entityUuid2
		}, JsonOptions);
		using StringContent content = new StringContent(content2, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _http.PostAsync(new Uri(_baseUri, "api/v1/actions"), content, cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw new InvalidOperationException("Fabric bridge rejected the current session token.");
		}
		string text = await ReadBoundedAsync(response.Content, 65536, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"TC-014 relocation was rejected (HTTP {response.StatusCode}): {text}");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(text);
		FabricActionResponse fabricActionResponse = jsonDocument.RootElement.Deserialize<FabricActionResponse>(JsonOptions) ?? throw new InvalidDataException("TC-014 relocation receipt was empty.");
		FabricPositionCoordinates from = fabricActionResponse.From;
		FabricPositionCoordinates to = fabricActionResponse.To;
		if (fabricActionResponse.Action != "teleport_test_enderman" || fabricActionResponse.FixtureContract != "tc014-relocation-v1" || !string.Equals(fabricActionResponse.RequestId, requestId, StringComparison.OrdinalIgnoreCase) || !Guid.TryParse(fabricActionResponse.EntityUuid, out var result) || result != parsedEntity || fabricActionResponse.EntityType != "minecraft:enderman" || !fabricActionResponse.AliveAfterAction.GetValueOrDefault() || !fabricActionResponse.Verified.GetValueOrDefault() || !float.IsFinite(fabricActionResponse.BeforeHealth.GetValueOrDefault(float.NaN)) || !float.IsFinite(fabricActionResponse.AfterHealth.GetValueOrDefault(float.NaN)) || fabricActionResponse.BeforeHealth <= 0f || fabricActionResponse.AfterHealth != fabricActionResponse.BeforeHealth || !fabricActionResponse.DamageEventsBefore.HasValue || fabricActionResponse.DamageEventsAfter != fabricActionResponse.DamageEventsBefore || string.IsNullOrWhiteSpace(fabricActionResponse.DimensionBefore) || fabricActionResponse.DimensionBefore != fabricActionResponse.DimensionAfter || fabricActionResponse.SafePadBlocks.GetValueOrDefault() != 9 || !fabricActionResponse.ServerTickBefore.HasValue || fabricActionResponse.ServerTick < fabricActionResponse.ServerTickBefore || (object)from == null || (object)to == null || !Finite(from) || !Finite(to) || !double.IsFinite(fabricActionResponse.DistanceBlocks.GetValueOrDefault(double.NaN)) || fabricActionResponse.DistanceBlocks < 8.0)
		{
			throw new InvalidDataException("TC-014 relocation receipt failed identity, same-entity, safe-pad or health/readback validation.");
		}
		double num = to.X - from.X;
		double num2 = to.Y - from.Y;
		double num3 = to.Z - from.Z;
		if (Math.Abs(Math.Sqrt(num * num + num2 * num2 + num3 * num3) - fabricActionResponse.DistanceBlocks.Value) > 0.05)
		{
			throw new InvalidDataException("TC-014 relocation distance did not match the reported coordinates.");
		}
		return fabricActionResponse;
	}

	private static bool Finite(FabricPositionCoordinates position)
	{
		if (double.IsFinite(position.X) && double.IsFinite(position.Y))
		{
			return double.IsFinite(position.Z);
		}
		return false;
	}

	public async Task<FabricActionResponse> ExecuteAllowlistedActionAsync(string action, string? entityUuid = null, string? hazard = null, string? itemId = null, int hitCount = 1, string? attackProfile = null, CancellationToken cancellationToken = default(CancellationToken), bool teleportAfterHit = false, string? environmentDamageSource = null, bool disableTargetAi = false, bool teleportTargetAfterLaunch = false, string? secondItemId = null, bool secondTargetAfterTeleport = false)
	{
		bool flag;
		switch (action)
		{
		case "configure_hazard":
		case "prepare_qa_suite":
		case "spawn_test_enderman":
		case "throw_test_projectile":
		case "throw_test_projectile_second_target":
		case "throw_test_potion_pair":
		case "player_attack_test_enderman":
		case "sweep_attack_test_endermen":
		case "kill_test_enderman":
		case "release_test_fixture":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new InvalidOperationException("Fabric bridge action is outside the allowlist.");
		}
		if (hazard != null && !FabricBridgeContract.SupportsHazard(hazard))
		{
			throw new InvalidOperationException("Fabric test hazard is outside the allowlist.");
		}
		if (itemId != null)
		{
			switch (itemId)
			{
			case "bare_hand":
			case "stone_axe":
			case "diamond_axe":
			case "stone_sword":
			case "iron_sword":
			case "wooden_axe":
			case "diamond_sword":
			case "flaming_arrow":
			case "netherite_axe":
			case "iron_axe":
			case "snowball":
			case "splash_water_inside":
			case "splash_water_potion":
			case "splash_harming_potion":
			case "splash_healing_potion":
			case "wooden_sword":
			case "netherite_sword":
			case "egg":
			case "arrow":
			case "crossbow_arrow":
			case "firework_crossbow":
			case "trident":
			case "splash_water_outside":
				break;
			default:
				goto IL_04a7;
			}
		}
		flag = true;
		goto IL_04aa;
		IL_092b:
		if (!flag)
		{
			throw new InvalidOperationException("Fabric environmental damage source is outside the v1 allowlist.");
		}
		if (environmentDamageSource != null && action != "player_attack_test_enderman")
		{
			throw new InvalidOperationException("Environmental damage source is only valid with player_attack_test_enderman.");
		}
		if (environmentDamageSource != null && (hitCount != 1 || teleportAfterHit))
		{
			throw new InvalidOperationException("Environmental damage pairing requires one attack and no controlled teleport.");
		}
		flag = disableTargetAi;
		if (flag)
		{
			string text = action;
			bool flag2 = ((text == "player_attack_test_enderman" || text == "sweep_attack_test_endermen") ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("Target AI may only be disabled for a bounded Player.attack test.");
		}
		if (action == "sweep_attack_test_endermen" && (itemId != "iron_sword" || attackProfile != "sweeping1" || hitCount != 1 || !disableTargetAi || teleportAfterHit || environmentDamageSource != null))
		{
			throw new InvalidOperationException("Sweep attack requires its fixed single-hit Sweeping Edge I fixture.");
		}
		flag = teleportTargetAfterLaunch;
		if (flag)
		{
			string text = action;
			bool flag2 = ((text == "throw_test_projectile" || text == "throw_test_projectile_second_target") ? true : false);
			flag = !flag2 || itemId != "arrow";
		}
		if (flag)
		{
			throw new InvalidOperationException("Post-launch teleport is only available for a managed vanilla arrow fixture.");
		}
		if (secondTargetAfterTeleport && (action != "throw_test_projectile_second_target" || itemId != "arrow" || !teleportTargetAfterLaunch))
		{
			throw new InvalidOperationException("The second-target collision fixture is limited to a managed arrow with a same-action post-launch teleport.");
		}
		if (action == "throw_test_projectile_second_target" && !secondTargetAfterTeleport)
		{
			throw new InvalidOperationException("The second-target arrow action requires its fixed two-target fixture mode.");
		}
		if ((hitCount < 1 || hitCount > 64) ? true : false)
		{
			throw new InvalidOperationException("Player attack repeat count must be between 1 and 64.");
		}
		string content2 = ((action == "throw_test_potion_pair") ? JsonSerializer.Serialize(new
		{
			action = action,
			entityUuid = (entityUuid ?? ""),
			itemId = itemId,
			secondItemId = secondItemId,
			hitCount = 1
		}, JsonOptions) : ((attackProfile == null && !teleportAfterHit && environmentDamageSource == null && !disableTargetAi && !teleportTargetAfterLaunch && !secondTargetAfterTeleport) ? JsonSerializer.Serialize(new
		{
			action = action,
			entityUuid = (entityUuid ?? ""),
			hazard = (hazard ?? "none"),
			itemId = (itemId ?? "bare_hand"),
			hitCount = hitCount
		}, JsonOptions) : JsonSerializer.Serialize(new
		{
			action = action,
			entityUuid = (entityUuid ?? ""),
			hazard = (hazard ?? "none"),
			itemId = (itemId ?? "bare_hand"),
			hitCount = hitCount,
			attackProfile = (attackProfile ?? "standard"),
			teleportAfterHit = teleportAfterHit,
			environmentDamageSource = (environmentDamageSource ?? "none"),
			disableTargetAi = disableTargetAi,
			teleportTargetAfterLaunch = teleportTargetAfterLaunch,
			secondTargetAfterTeleport = secondTargetAfterTeleport
		}, JsonOptions)));
		using (StringContent content = new StringContent(content2, Encoding.UTF8, "application/json"))
		{
			using HttpResponseMessage response = await _http.PostAsync(new Uri(_baseUri, "api/v1/actions"), content, cancellationToken);
			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				throw new InvalidOperationException("Fabric bridge rejected the current session token.");
			}
			string text2 = await ReadBoundedAsync(response.Content, 65536, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				throw new InvalidOperationException($"Fabric bridge action was rejected (HTTP {response.StatusCode}): {text2}");
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(text2);
			return jsonDocument.RootElement.Deserialize<FabricActionResponse>(JsonOptions) ?? throw new InvalidOperationException("Fabric bridge action response was malformed.");
		}
		IL_0894:
		flag = false;
		goto IL_0897;
		IL_0897:
		if (!flag)
		{
			throw new InvalidOperationException("Fabric attack profile is outside the v1 allowlist.");
		}
		flag = attackProfile != null;
		if (flag)
		{
			string text = action;
			bool flag2 = ((text == "player_attack_test_enderman" || text == "sweep_attack_test_endermen") ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("Fabric attack profiles are only valid for allowlisted player attack actions.");
		}
		if (environmentDamageSource != null)
		{
			string text = environmentDamageSource;
			if (!(text == "inFire") && !(text == "lava"))
			{
				flag = false;
				goto IL_092b;
			}
		}
		flag = true;
		goto IL_092b;
		IL_04a7:
		flag = false;
		goto IL_04aa;
		IL_04ed:
		if (!flag || (secondItemId != null && action != "throw_test_potion_pair"))
		{
			throw new InvalidOperationException("Second projectile is outside the paired-potion allowlist.");
		}
		if (action == "throw_test_potion_pair" && (itemId != "splash_water_potion" || secondItemId != "splash_harming_potion"))
		{
			throw new InvalidOperationException("Paired potion test requires the fixed water-then-Harming pair.");
		}
		if (attackProfile != null)
		{
			switch (attackProfile)
			{
			case "standard":
			case "critical":
			case "sneaking":
			case "looting1":
			case "strength1":
			case "sweeping1":
			case "weakness1":
			case "sprinting":
			case "smite1":
			case "speed1":
			case "fireAspect1":
			case "protection4":
			case "absorption1":
			case "customName1":
			case "knockback1":
			case "sharpness1":
			case "offhandShield":
			case "oneDurability":
			case "attackDamage6":
			case "bane1":
			case "offhandOther":
			case "criticalJumpBoost1":
				break;
			default:
				goto IL_0894;
			}
		}
		flag = true;
		goto IL_0897;
		IL_04aa:
		if (!flag)
		{
			throw new InvalidOperationException("Fabric test item is outside the allowlist.");
		}
		if (secondItemId != null)
		{
			string text = secondItemId;
			if (!(text == "splash_water_potion") && !(text == "splash_harming_potion"))
			{
				flag = false;
				goto IL_04ed;
			}
		}
		flag = true;
		goto IL_04ed;
	}

	private async Task<T> GetAsync<T>(string path, int maxBytes, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await _http.GetAsync(new Uri(_baseUri, path), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw new InvalidOperationException("Fabric bridge rejected its session descriptor; reconnect from the current server directory.");
		}
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await ReadBoundedAsync(response.Content, maxBytes, cancellationToken));
		T val = jsonDocument.RootElement.Deserialize<T>(JsonOptions);
		if (val == null)
		{
			throw new InvalidOperationException("Fabric bridge returned an empty response.");
		}
		return val;
	}

	private async Task<IReadOnlyList<T>> GetCollectionAsync<T>(string path, string property, int maxBytes, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await _http.GetAsync(new Uri(_baseUri, path), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (response.StatusCode == HttpStatusCode.Unauthorized)
		{
			throw new InvalidOperationException("Fabric bridge rejected its session descriptor; reconnect from the current server directory.");
		}
		response.EnsureSuccessStatusCode();
		using JsonDocument jsonDocument = JsonDocument.Parse(await ReadBoundedAsync(response.Content, maxBytes, cancellationToken));
		if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object || !jsonDocument.RootElement.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 5000)
		{
			throw new InvalidOperationException("Fabric bridge " + property + " response is malformed or exceeds the 5,000-item limit.");
		}
		return value.EnumerateArray().Select(delegate(JsonElement item)
		{
			T val = item.Deserialize<T>(JsonOptions);
			if (val == null)
			{
				throw new InvalidOperationException("Fabric bridge contains an invalid record.");
			}
			return val;
		}).ToArray();
	}

	private static async Task<string> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
	{
		long? contentLength = content.Headers.ContentLength;
		if (contentLength.HasValue)
		{
			long valueOrDefault = contentLength.GetValueOrDefault();
			if (valueOrDefault > maximumBytes)
			{
				throw new InvalidOperationException("Fabric bridge response exceeded its byte limit.");
			}
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
					throw new InvalidOperationException("Fabric bridge response exceeded its byte limit.");
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

	private static string GetString(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static int GetInt(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var value2))
		{
			return 0;
		}
		return value2;
	}

	public void Dispose()
	{
		_http.Dispose();
		GC.KeepAlive(_token);
	}
}
