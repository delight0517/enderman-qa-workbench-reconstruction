using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Adapters;

namespace EndermanQa.Desktop.Scenarios;

public static class Tc014RelocationSmokeRunner
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	public static async Task<Tc014RelocationSmokeResult> RunAsync(JavaServerWorkspace workspace, OwnedJavaServerProcess serverOwner, string instancesRoot, string projectRoot, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(workspace, "workspace");
		ArgumentNullException.ThrowIfNull(serverOwner, "serverOwner");
		string normalizedRoot = Path.GetFullPath(projectRoot);
		string fullPath = WorkbenchInstancePaths.ResolveInstanceRoot(workspace.InstanceId, workspace.RootPath, instancesRoot);
		if (!Path.GetFullPath(workspace.WorldPath).Equals(Path.Combine(fullPath, "qa-world"), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(workspace.WorldPath) || serverOwner.HasExited || serverOwner.ProcessId <= 0 || !Path.GetFullPath(serverOwner.Workspace.RootPath).Equals(fullPath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("TC-014 fixture smoke requires the exact running Workbench server and its existing qa-world.");
		}
		DateTimeOffset started = DateTimeOffset.UtcNow;
		string targetId = null;
		string entityUuid = null;
		FabricActionResponse relocation = null;
		bool released = false;
		Exception failure = null;
		string failureStage = null;
		string stage = "bridge-status";
		FabricJavaGameAdapter adapter = await serverOwner.ConnectGameAdapterAsync(cancellationToken);
		Tc014RelocationSmokeResult result2;
		try
		{
			using FabricBridgeClient bridge = FabricBridgeClient.FromDescriptor(Path.Combine(workspace.RootPath, "endermanqa-bridge.json"));
			try
			{
				
				try
				{
					FabricBridgeStatus fabricBridgeStatus = await bridge.GetStatusAsync(cancellationToken);
					targetId = fabricBridgeStatus.TargetId;
					if (fabricBridgeStatus.ServerPid != serverOwner.ProcessId || fabricBridgeStatus.InstanceId != workspace.InstanceId || fabricBridgeStatus.TargetId != adapter.Target.TargetId || !fabricBridgeStatus.WorldMutable)
					{
						throw new InvalidOperationException("TC-014 bridge identity does not match the app-owned QA server/world.");
					}
					stage = "suite-prepare";
					await adapter.PrepareSuiteAsync(cancellationToken);
					stage = "spawn-managed-enderman";
					FabricActionResponse fabricActionResponse = await bridge.ExecuteAllowlistedActionAsync("spawn_test_enderman", null, null, null, 1, null, cancellationToken);
					if (Guid.TryParse(fabricActionResponse.EntityUuid, out var result))
					{
						entityUuid = result.ToString("D");
					}
					if (entityUuid == null || !fabricActionResponse.EntityAdded || !fabricActionResponse.EndermanTypeMatches || fabricActionResponse.EntityType != "minecraft:enderman")
					{
						throw new InvalidDataException("TC-014 smoke did not receive a verified managed Enderman spawn receipt.");
					}
					stage = "relocate-managed-enderman";
					relocation = await bridge.TeleportManagedEndermanForTc014Async(entityUuid, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException))
				{
					failure = ex;
					failureStage = stage;
				}
			}
			finally
			{
				if (entityUuid != null)
				{
					stage = "release-managed-fixture";
					try
					{
						FabricActionResponse fabricActionResponse2 = await bridge.ExecuteAllowlistedActionAsync("release_test_fixture", entityUuid, null, null, 1, null, CancellationToken.None);
						released = fabricActionResponse2.Action == "release_test_fixture" && fabricActionResponse2.EntityUuid.Equals(entityUuid, StringComparison.OrdinalIgnoreCase);
						if (!released && failure == null)
						{
							failure = new InvalidDataException("TC-014 managed fixture cleanup receipt did not match its UUID.");
						}
					}
					catch (Exception ex2) when (!(ex2 is OutOfMemoryException) && !(ex2 is StackOverflowException))
					{
						if (failure == null)
						{
							failure = new InvalidOperationException("TC-014 managed fixture cleanup needs reconciliation.", ex2);
						}
						if (failureStage == null)
						{
							failureStage = stage;
						}
					}
				}
			}
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			Tc014RelocationSmokeReceipt receipt = new Tc014RelocationSmokeReceipt("tc014-relocation-smoke-v1", (failure == null && (object)relocation != null) ? "completed" : "failed", "server-fixture-only; no client cached-target attack, no client display, no screenshot; not a TC-014 gameplay verdict", workspace.InstanceId, serverOwner.ProcessId, targetId, entityUuid, relocation, released, failure?.GetType().Name, failureStage, started, utcNow);
			string text = Path.Combine(normalizedRoot, "adapter-checks", "tc014-relocation-smoke");
			Directory.CreateDirectory(text);
			string evidencePath = Path.Combine(text, $"{started:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}.json");
			byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
			await File.WriteAllBytesAsync(evidencePath, bytes, CancellationToken.None);
			string evidenceSha = Convert.ToHexString(SHA256.HashData(bytes));
			if (failure != null)
			{
				throw new InvalidOperationException("TC-014 server-fixture smoke failed; evidence saved at " + evidencePath + ".", failure);
			}
			result2 = new Tc014RelocationSmokeResult(evidencePath, evidenceSha, receipt);
		}
		finally
		{
			if (adapter != null)
			{
				await adapter.DisposeAsync();
			}
		}
		return result2;
	}
}
