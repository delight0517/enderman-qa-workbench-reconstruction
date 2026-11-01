using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Adapters;

public sealed class MinecraftServerMacroProvider : IGameMacroAdapterProvider
{
	[CompilerGenerated]
	private Func<OwnedJavaServerProcess?> _003CcurrentServer_003EP;

	[CompilerGenerated]
	private Func<JavaServerWorkspace?> _003CcurrentWorkspace_003EP;

	private static readonly IReadOnlySet<string> Actions = new HashSet<string>(StringComparer.Ordinal)
	{
		"entity.spawn", "entity.apply_damage", "entity.player_attack", "entity.player_attack.profile.v1", "entity.sweep_attack", "entity.sweep_attack.v1", "environment.configure", "projectile.throw", "projectile.throw_volley", "observation.request",
		"time.advance"
	};

	private static readonly IReadOnlySet<string> Observations = new HashSet<string>(StringComparer.Ordinal)
	{
		"entity.uuid", "entity.type", "entity.lifeState", "entity.alive", "entity.health", "entity.maxHealth", "entity.damageCause", "entity.gameTick", "entity.dimension", "entity.x",
		"entity.y", "entity.z", "entity.provenance", "entity.frozenTicks", "entity.fireTicks", "entity.damageEventCount", "entity.attackerUuid", "entity.positionTraceCount", "action.succeeded", "action.detail"
	};

	public GameToolDescriptor Descriptor { get; }

	public MinecraftServerMacroProvider(Func<OwnedJavaServerProcess?> currentServer, Func<JavaServerWorkspace?> currentWorkspace)
	{
		_003CcurrentServer_003EP = currentServer;
		_003CcurrentWorkspace_003EP = currentWorkspace;
		Descriptor = new GameToolDescriptor("minecraft-java.owned-server-macro", 1, "minecraft-java", "Minecraft Java · 격리 서버 매크로", "Minecraft Fabric server", "ready", RequiresOwnedProcess: true, RequiresUnmirroredDisplay: false, Actions, Observations);
		
	}

	public bool Supports(GameAutomationTarget target)
	{
		OwnedJavaServerProcess ownedJavaServerProcess = _003CcurrentServer_003EP();
		JavaServerWorkspace javaServerWorkspace = _003CcurrentWorkspace_003EP();
		if (target.GameId == Descriptor.GameId && target.RuntimeKind == "desktop" && target.Ownership == "owned" && ownedJavaServerProcess != null && !ownedJavaServerProcess.HasExited && (object)javaServerWorkspace != null && ownedJavaServerProcess.Workspace.InstanceId == javaServerWorkspace.InstanceId)
		{
			return target.TargetId == javaServerWorkspace.InstanceId;
		}
		return false;
	}

	public async Task<IGameMacroAdapter> ConnectAsync(GameAutomationTarget target, CancellationToken cancellationToken)
	{
		if (!Supports(target))
		{
			throw new InvalidOperationException("Start the exact app-owned isolated Minecraft server first and use its instance ID as the macro target.");
		}
		OwnedJavaServerProcess server = _003CcurrentServer_003EP();
		JavaServerWorkspace workspace = _003CcurrentWorkspace_003EP();
		Process process = Process.GetProcessById(server.ProcessId);
		try
		{
			if (process.HasExited || !PathEquals(process.MainModule?.FileName, server.JavaExecutable))
			{
				throw new InvalidOperationException("Live Java process identity does not match the Workbench-owned server.");
			}
			string imagePath = Path.GetFullPath(process.MainModule.FileName);
			FileStream stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan);
			IGameMacroAdapter result;
			try
			{
				string processImageSha = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
				DateTimeOffset dateTimeOffset = new DateTimeOffset(process.StartTime.ToUniversalTime());
				string evidenceSha = GameMacroTargetSafety.ComputeProcessAttestationHash(target.GameId, target.TargetId, process.Id, imagePath, processImageSha, dateTimeOffset);
				GameTargetVerification verification = new GameTargetVerification(IsVerified: true, target.GameId, target.TargetId, "workbench-owned-fabric-server", DateTimeOffset.UtcNow, IsTaskOwned: true, IsUnmirrored: false, process.Id, evidenceSha)
				{
					ProcessImagePath = imagePath,
					ProcessImageSha256 = processImageSha,
					ProcessStartedAtUtc = dateTimeOffset
				};
				FabricJavaGameAdapter fabricJavaGameAdapter = await server.ConnectGameAdapterAsync(cancellationToken);
				if (!fabricJavaGameAdapter.Target.RuntimeKind.Contains("server", StringComparison.OrdinalIgnoreCase))
				{
					await fabricJavaGameAdapter.DisposeAsync();
					throw new InvalidOperationException("Connected Fabric adapter did not identify a dedicated server runtime.");
				}
				if (server.HasExited || server.Workspace.InstanceId != workspace.InstanceId)
				{
					await fabricJavaGameAdapter.DisposeAsync();
					throw new InvalidOperationException("Owned server changed while the macro adapter connected.");
				}
				result = new MinecraftServerMacroAdapter(Descriptor, target, verification, fabricJavaGameAdapter);
			}
			finally
			{
				if (stream != null)
				{
					await stream.DisposeAsync();
				}
			}
			return result;
		}
		finally
		{
			process.Dispose();
		}
	}

	private static bool PathEquals(string? left, string right)
	{
		if (left != null)
		{
			return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}
}
