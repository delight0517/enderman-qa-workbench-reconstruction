using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace EndermanQa.Desktop.Adapters;

public sealed class OwnedJavaClientProcess : IAsyncDisposable
{
	public sealed record CaptureEvidence(string RequestId, string PngPath, string SidecarPath, long ProcessId, string InstanceId, string SessionId, string TargetId, string GameVersion, string Dimension, long GameTick, int Width, int Height, long Bytes, string Sha256, DateTimeOffset CapturedAt, string CaptureSource);

	public sealed record ClientBowAttemptReceipt(FabricClientActionReceipt Aim, FabricClientActionReceipt DrawStart, FabricClientActionReceipt? Release, int ObservedDrawTicks, int RequestedDrawTicks, bool RequestedDrawDurationReached, string Disposition);

	private sealed record CaptureSidecar(string Format, string RequestId, long ProcessId, string InstanceId, string SessionId, string TargetId, string GameVersion, string Dimension, long GameTick, int FramebufferWidth, int FramebufferHeight, DateTimeOffset CapturedAt, string FileName, long Bytes, string Sha256, string CaptureSource);

	private sealed record ClientDescriptor(long ProcessId);

	private readonly Process _launcher;

	private Process _client;

	private FabricClientBridgeClient? _bridge;

	private readonly JavaServerWorkspace _workspace;

	private readonly VerifiedClientDisplayLease _displayLease;

	private readonly CancellationTokenSource _leaseMonitorCancellation = new CancellationTokenSource();

	private Task? _leaseMonitor;

	private Exception? _leaseLoss;

	private readonly string _sessionRoot;

	private readonly string _logPath;

	private readonly object _logGate = new object();

	private bool _stopped;

	public int ClientProcessId => _client.Id;

	public string SessionRoot => _sessionRoot;

	public string LogPath => _logPath;

	public bool HasExited => _client.HasExited;

	private OwnedJavaClientProcess(Process launcher, Process client, FabricClientBridgeClient bridge, JavaServerWorkspace workspace, string sessionRoot, string logPath, VerifiedClientDisplayLease displayLease)
	{
		_launcher = launcher;
		_client = client;
		_bridge = bridge;
		_workspace = workspace;
		_sessionRoot = sessionRoot;
		_logPath = logPath;
		_displayLease = displayLease;
	}

	public async Task<FabricClientBridgeStatus> GetBridgeStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await ValidateLeasedWindowAsync("client-input", cancellationToken);
		FabricClientBridgeStatus status = await RequireLiveBridge().GetStatusAsync(cancellationToken);
		await ValidateLeasedWindowAsync("client-input", cancellationToken);
		return status;
	}

	public async Task<FabricClientActionReceipt> ObserveEndermanAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		await ValidateLeasedWindowAsync("client-input", cancellationToken);
		FabricClientActionReceipt receipt = await RequireLiveBridge().ObserveEndermanAsync(entityId, entityUuid, cancellationToken);
		await ValidateLeasedWindowAsync("client-input", cancellationToken);
		return receipt;
	}

	public async Task<ClientBowAttemptReceipt> FireBowAtEndermanAsync(int entityId, string entityUuid, int requestedDrawTicks = 20, CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((requestedDrawTicks < 1 || requestedDrawTicks > 40) ? true : false)
		{
			throw new ArgumentOutOfRangeException("requestedDrawTicks");
		}
		FabricClientBridgeStatus fabricClientBridgeStatus = await GetBridgeStatusAsync(cancellationToken);
		if (!fabricClientBridgeStatus.ConnectedToServer || !fabricClientBridgeStatus.PlayerAvailable || !fabricClientBridgeStatus.WorldAvailable || fabricClientBridgeStatus.MainHandItem != "minecraft:bow" || !fabricClientBridgeStatus.HasNormalArrowAmmo)
		{
			throw new InvalidOperationException("Client bow preflight failed: require active server, normal bow in main hand, and normal arrow ammunition.");
		}
		FabricClientBridgeClient bridge = RequireLiveBridge();
		await ValidateLeasedWindowAsync("client-input", cancellationToken);
		FabricClientActionReceipt aim = await bridge.AimAtEndermanAsync(entityId, entityUuid, cancellationToken);
		await ValidateLeasedWindowAsync("client-input", cancellationToken);
		FabricClientActionReceipt drawStart;
		try
		{
			drawStart = await bridge.UseMainHandAsync(entityId, entityUuid, cancellationToken);
			await ValidateLeasedWindowAsync("client-input", cancellationToken);
			if (!drawStart.PlayerUsingItemAfter.GetValueOrDefault() || drawStart.MainHandItem != "minecraft:bow")
			{
				throw new InvalidOperationException("Vanilla client did not confirm a bow draw; no release was sent.");
			}
		}
		catch
		{
			try
			{
				await StopAsync(TimeSpan.FromSeconds(20.0)).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception)
			{
			}
			throw;
		}
		try
		{
			DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(6.0);
			FabricClientBridgeStatus fabricClientBridgeStatus2 = await GetBridgeStatusAsync(cancellationToken);
			while (fabricClientBridgeStatus2.PlayerUsingItem && fabricClientBridgeStatus2.MainHandItem == "minecraft:bow" && fabricClientBridgeStatus2.PlayerUsingItemTicks < requestedDrawTicks && DateTimeOffset.UtcNow < deadline)
			{
				await Task.Delay(50, cancellationToken);
				fabricClientBridgeStatus2 = await GetBridgeStatusAsync(cancellationToken);
			}
			int playerUsingItemTicks = fabricClientBridgeStatus2.PlayerUsingItemTicks;
			if (!fabricClientBridgeStatus2.PlayerUsingItem || fabricClientBridgeStatus2.MainHandItem != "minecraft:bow")
			{
				return new ClientBowAttemptReceipt(aim, drawStart, null, playerUsingItemTicks, requestedDrawTicks, playerUsingItemTicks >= requestedDrawTicks, "draw-ended-before-explicit-release; reconcile server projectile/damage facts");
			}
			bool reached = playerUsingItemTicks >= requestedDrawTicks;
			await ValidateLeasedWindowAsync("client-input", cancellationToken);
			FabricClientActionReceipt release = await bridge.ReleaseMainHandAsync(entityId, entityUuid, cancellationToken);
			await ValidateLeasedWindowAsync("client-input", cancellationToken);
			return new ClientBowAttemptReceipt(aim, drawStart, release, playerUsingItemTicks, requestedDrawTicks, reached, reached ? "client-bow-release-accepted; server outcome pending" : "bounded-draw-timeout; one partial release accepted; server outcome pending");
		}
		catch (OperationCanceledException)
		{
			try
			{
				await StopAsync(TimeSpan.FromSeconds(20.0)).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception)
			{
			}
			throw;
		}
		catch
		{
			try
			{
				await StopAsync(TimeSpan.FromSeconds(20.0)).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception)
			{
			}
			throw;
		}
	}

	public Task<FabricClientActionReceipt> CacheEndermanTargetAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return CacheEndermanTargetWithLeaseAsync(entityId, entityUuid, cancellationToken);
	}

	private async Task<FabricClientActionReceipt> CacheEndermanTargetWithLeaseAsync(int entityId, string entityUuid, CancellationToken cancellationToken)
	{
		await ValidateLeasedWindowAsync("client-input", cancellationToken);
		return await RequireLiveBridge().CacheEndermanTargetAsync(entityId, entityUuid, cancellationToken);
	}

	public Task<FabricClientActionReceipt> AttackCachedEndermanAsync(int entityId, string entityUuid, CancellationToken cancellationToken = default(CancellationToken))
	{
		return AttackCachedEndermanWithLeaseAsync(entityId, entityUuid, cancellationToken);
	}

	private async Task<FabricClientActionReceipt> AttackCachedEndermanWithLeaseAsync(int entityId, string entityUuid, CancellationToken cancellationToken)
	{
		await ValidateLeasedWindowAsync("client-input", cancellationToken);
		return await RequireLiveBridge().AttackCachedEndermanAsync(entityId, entityUuid, cancellationToken);
	}

	public async Task<CaptureEvidence> CaptureFramebufferAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		RejectReparsePoint(_workspace.RootPath);
		RejectReparsePoint(Path.Combine(_workspace.RootPath, "qa-world"));
		RejectReparsePoint(Path.Combine(_workspace.RootPath, "client-runs"));
		RejectReparsePoint(_sessionRoot);
		await ValidateLeasedWindowAsync("framebuffer-capture", cancellationToken);
		FabricClientBridgeClient bridge = RequireLiveBridge();
		ValidateCaptureSessionStatus(await bridge.GetStatusAsync(cancellationToken));
		await ValidateLeasedWindowAsync("framebuffer-capture", cancellationToken);
		RejectReparsePoint(_sessionRoot);
		FabricClientCaptureReceipt receipt = await bridge.CaptureFramebufferAsync(cancellationToken);
		if (receipt.State != "completed")
		{
			throw new InvalidOperationException($"Minecraft capture did not complete ({receipt.State}); request {receipt.RequestId} was not resubmitted.");
		}
		await ValidateLeasedWindowAsync("framebuffer-capture", cancellationToken);
		ValidateCaptureSessionStatus(await bridge.GetStatusAsync(cancellationToken));
		string expectedName = "capture-" + receipt.RequestId + ".png";
		if (!string.Equals(receipt.FileName, expectedName, StringComparison.Ordinal))
		{
			throw new InvalidDataException("Capture receipt filename does not match its request ID.");
		}
		string png = Path.GetFullPath(Path.Combine(_sessionRoot, expectedName));
		string sidecarPath = png + ".json";
		RejectReparsePoint(png);
		RejectReparsePoint(sidecarPath);
		if (!Path.GetDirectoryName(png).Equals(Path.GetFullPath(_sessionRoot), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Capture artifact escaped the exact owned session directory.");
		}
		await WaitForCaptureFilesAsync(png, sidecarPath, cancellationToken);
		RejectReparsePoint(png);
		RejectReparsePoint(sidecarPath);
		FileInfo info = new FileInfo(png);
		bool flag = info.Length != receipt.Bytes;
		if (!flag)
		{
			long length = info.Length;
			bool flag2 = ((length < 1 || length > 52428800) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new InvalidDataException("Capture file size does not match its bounded receipt.");
		}
		ValidatePngImage(png, receipt.FramebufferWidth ?? throw new InvalidDataException("Capture receipt is missing framebuffer width."), receipt.FramebufferHeight ?? throw new InvalidDataException("Capture receipt is missing framebuffer height."));
		FileStream stream = new FileStream(png, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		string actualHash;
		try
		{
			actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
		}
		finally
		{
			if (stream != null)
			{
				await stream.DisposeAsync();
			}
		}
		if (!actualHash.Equals(receipt.Sha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Capture file SHA-256 does not match its client receipt.");
		}
		CaptureSidecar sidecar = JsonSerializer.Deserialize<CaptureSidecar>(await File.ReadAllTextAsync(sidecarPath, cancellationToken), new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			PropertyNameCaseInsensitive = true
		}) ?? throw new InvalidDataException("Capture sidecar is empty.");
		FabricClientBridgeStatus fabricClientBridgeStatus = await bridge.GetStatusAsync(cancellationToken);
		if (sidecar.Format != "endermanqa-client-capture-v2" || !sidecar.RequestId.Equals(receipt.RequestId, StringComparison.OrdinalIgnoreCase) || sidecar.ProcessId != _client.Id || sidecar.InstanceId != _workspace.InstanceId || sidecar.SessionId != Path.GetFileName(_sessionRoot) || sidecar.TargetId != fabricClientBridgeStatus.TargetId || sidecar.GameVersion != "26.3" || sidecar.CaptureSource != "minecraft-client-main-framebuffer" || !sidecar.Dimension.Equals(fabricClientBridgeStatus.Dimension, StringComparison.Ordinal) || sidecar.GameTick < 0 || sidecar.FramebufferWidth != receipt.FramebufferWidth || sidecar.FramebufferHeight != receipt.FramebufferHeight || sidecar.Bytes != info.Length || !sidecar.Sha256.Equals(actualHash, StringComparison.OrdinalIgnoreCase) || !sidecar.FileName.Equals(expectedName, StringComparison.Ordinal))
		{
			throw new InvalidDataException("Capture sidecar identity or measurement does not match the live owned session.");
		}
		return new CaptureEvidence(receipt.RequestId, png, sidecarPath, sidecar.ProcessId, sidecar.InstanceId, sidecar.SessionId, sidecar.TargetId, sidecar.GameVersion, sidecar.Dimension, sidecar.GameTick, sidecar.FramebufferWidth, sidecar.FramebufferHeight, info.Length, actualHash, sidecar.CapturedAt, sidecar.CaptureSource);
	}

	private static void ValidatePngImage(string path, int expectedWidth, int expectedHeight)
	{
		Span<byte> buffer = stackalloc byte[24];
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		fileStream.ReadExactly(buffer);
		if (!buffer.Slice(0, 8).SequenceEqual(new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !buffer.Slice(12, 4).SequenceEqual("IHDR"u8))
		{
			throw new InvalidDataException("Client capture is not a valid PNG with an IHDR header.");
		}
		int num = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(16, 4));
		int num2 = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(20, 4));
		if (num < 1 || num2 < 1 || num > 8192 || num2 > 8192 || (long)num * (long)num2 > 16777216 || num != expectedWidth || num2 != expectedHeight)
		{
			throw new InvalidDataException("PNG dimensions are outside the capture bound or disagree with the client receipt.");
		}
		fileStream.Position = 0L;
		BitmapDecoder bitmapDecoder = BitmapDecoder.Create(fileStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		if (bitmapDecoder.Frames.Count != 1 || bitmapDecoder.Frames[0].PixelWidth != num || bitmapDecoder.Frames[0].PixelHeight != num2)
		{
			throw new InvalidDataException("PNG decoder dimensions do not match the capture header.");
		}
	}

	private void ValidateCaptureSessionStatus(FabricClientBridgeStatus status)
	{
		if (status.ApiVersion != 1 || status.ProcessId != _client.Id || status.InstanceId != _workspace.InstanceId || status.GameVersion != "26.3" || !status.ConnectedToServer || !status.PlayerAvailable || !status.WorldAvailable || string.IsNullOrWhiteSpace(status.Dimension))
		{
			throw new InvalidOperationException("Framebuffer capture requires the exact owned client in the selected active QA world.");
		}
	}

	private static async Task WaitForCaptureFilesAsync(string png, string sidecar, CancellationToken cancellationToken)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(3.0);
		while ((!File.Exists(png) || !File.Exists(sidecar)) && DateTimeOffset.UtcNow < deadline)
		{
			RejectReparsePoint(png);
			RejectReparsePoint(sidecar);
			await Task.Delay(100, cancellationToken);
		}
		if (!File.Exists(png) || !File.Exists(sidecar))
		{
			throw new FileNotFoundException("Completed client capture did not produce both PNG and sidecar.");
		}
	}

	private static void RejectReparsePoint(string path)
	{
		string current = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(current))
		{
			try
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new InvalidDataException($"Client session path contains a reparse point at '{current}'.");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
			current = parent;
		}
	}

	private FabricClientBridgeClient RequireLiveBridge()
	{
		ObjectDisposedException.ThrowIf(_stopped || _client.HasExited, this);
		Exception leaseLoss = _leaseLoss;
		if (leaseLoss != null)
		{
			throw new InvalidOperationException("VDM lease renewal failed; client interactions and captures are disabled.", leaseLoss);
		}
		return _bridge ?? throw new InvalidOperationException("The owned Minecraft client bridge has not finished connecting.");
	}

	private async Task ValidateLeasedWindowAsync(string requiredScope, CancellationToken cancellationToken)
	{
		await _displayLease.ValidateForSideEffectAsync(requiredScope, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		await WindowsLeasedWindowRouter.ValidateVisiblePlacementAsync(_client.Id, _displayLease, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private void StartLeaseMonitor()
	{
		_leaseMonitor = MonitorLeaseAsync(_leaseMonitorCancellation.Token);
	}

	private async Task MonitorLeaseAsync(CancellationToken cancellationToken)
	{
		_ = 2;
		try
		{
			using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(35.0));
			while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
			{
				try
				{
					await WindowsLeasedWindowRouter.ValidateVisiblePlacementAsync(_client.Id, _displayLease, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception value)
				{
					Interlocked.CompareExchange(ref _leaseLoss, value, null);
					await TerminateOwnedTreeAsync().ConfigureAwait(continueOnCapturedContext: false);
					return;
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	internal static async Task<OwnedJavaClientProcess> StartAsync(JavaServerWorkspace workspace, string instancesRoot, OwnedJavaServerProcess serverOwner, string javaExecutable, string clientModuleDirectory, VerifiedClientDisplayLease displayLease, CancellationToken cancellationToken = default(CancellationToken), OwnedLoopbackLatencyProxy? latencyProxy = null)
	{
		ArgumentNullException.ThrowIfNull(workspace, "workspace");
		ArgumentNullException.ThrowIfNull(serverOwner, "serverOwner");
		ArgumentNullException.ThrowIfNull(displayLease, "displayLease");
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Minecraft client window routing requires the Windows display APIs.");
		}
		if (!displayLease.IsEligible || !displayLease.InstanceId.Equals(workspace.InstanceId, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Minecraft client launch requires a fresh lease bound to this exact QA instance.");
		}
		if (serverOwner.HasExited || serverOwner.ProcessId <= 0)
		{
			throw new InvalidOperationException("The exact Workbench-owned QA server must be running before its client starts.");
		}
		if (latencyProxy != null && !latencyProxy.Matches(serverOwner))
		{
			throw new InvalidOperationException("Latency proxy belongs to a different Workbench server process or instance.");
		}
		if (!serverOwner.Workspace.InstanceId.Equals(workspace.InstanceId, StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(serverOwner.Workspace.RootPath).Equals(Path.GetFullPath(workspace.RootPath), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The server owner does not match the selected Workbench instance.");
		}
		string instanceRoot = Path.GetFullPath(workspace.RootPath);
		string fullPath = WorkbenchInstancePaths.ResolveInstanceRoot(workspace.InstanceId, instanceRoot, instancesRoot);
		foreach (string ownedPath in new[] { instancesRoot, instanceRoot, Path.Combine(instanceRoot, "eula.txt"), Path.Combine(instanceRoot, "server.properties"), Path.Combine(instanceRoot, "mods"), Path.Combine(instanceRoot, "fabric-server-launch.jar"), workspace.ModJarPath, workspace.WorldPath, Path.Combine(instanceRoot, "client-runs"), clientModuleDirectory, javaExecutable })
			RejectReparsePoint(ownedPath);
		if (!instanceRoot.Equals(fullPath, StringComparison.OrdinalIgnoreCase) || !workspace.WorldPath.Equals(Path.Combine(instanceRoot, "qa-world"), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(workspace.WorldPath))
		{
			throw new InvalidOperationException("Client target is not the selected Workbench-owned disposable qa-world instance.");
		}
		int serverPort = workspace.ServerPort;
		if ((serverPort < 1 || serverPort > 65535) ? true : false)
		{
			throw new InvalidOperationException("QA server port is invalid.");
		}
		string java = Path.GetFullPath(javaExecutable);
		if (!File.Exists(java) || !Path.GetFileName(java).Equals("java.exe", StringComparison.OrdinalIgnoreCase))
		{
			throw new FileNotFoundException("A Java 25 java.exe is required for the client.", java);
		}
		string module = Path.GetFullPath(clientModuleDirectory);
		RejectReparsePoint(module);
		string wrapperJar = Path.Combine(module, "gradle", "wrapper", "gradle-wrapper.jar");
		string path = Path.Combine(module, "gradle", "wrapper", "gradle-wrapper.properties");
		if (!File.Exists(Path.Combine(module, "build.gradle")) || !File.Exists(wrapperJar) || !File.Exists(path))
		{
			throw new DirectoryNotFoundException("The packaged client adapter module is incomplete.");
		}
		string text = Path.Combine(instanceRoot, "client-runs");
		RejectReparsePoint(text);
		Directory.CreateDirectory(text);
		RejectReparsePoint(text);
		string sessionRoot = Path.Combine(text, displayLease.ClientSessionId.ToString("D"));
		RejectReparsePoint(sessionRoot);
		if (Directory.Exists(sessionRoot) || File.Exists(sessionRoot))
		{
			throw new IOException("A newly allocated client session path already exists.");
		}
		Directory.CreateDirectory(sessionRoot);
		RejectReparsePoint(sessionRoot);
		var value = new
		{
			format = "endermanqa-client-session-v1",
			instanceId = workspace.InstanceId,
			sessionId = Path.GetFileName(sessionRoot),
			worldPath = workspace.WorldPath,
			serverPort = workspace.ServerPort,
			displayDevicePath = displayLease.DevicePath,
			createdAt = DateTimeOffset.UtcNow
		};
		string sessionMetadataPath = Path.Combine(sessionRoot, "client-session.json");
		RejectReparsePoint(sessionMetadataPath);
		await File.WriteAllTextAsync(sessionMetadataPath, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
		string logPath = Path.Combine(sessionRoot, "client-launch.log");
		RejectReparsePoint(logPath);
		ProcessStartInfo processStartInfo = new ProcessStartInfo(java)
		{
			WorkingDirectory = module,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		processStartInfo.ArgumentList.Add("-classpath");
		processStartInfo.ArgumentList.Add(wrapperJar);
		processStartInfo.ArgumentList.Add("org.gradle.wrapper.GradleWrapperMain");
		processStartInfo.ArgumentList.Add("runClient");
		processStartInfo.ArgumentList.Add("--no-daemon");
		processStartInfo.ArgumentList.Add("--offline");
		processStartInfo.ArgumentList.Add("--console=plain");
		processStartInfo.ArgumentList.Add("-PqaClientInstanceId=" + workspace.InstanceId);
		processStartInfo.ArgumentList.Add("-PqaClientRunDirectory=" + sessionRoot);
		processStartInfo.ArgumentList.Add("-PqaClientServerAddress=127.0.0.1:" + (latencyProxy?.ListenPort ?? workspace.ServerPort));
		string fullName = Directory.GetParent(Path.GetDirectoryName(java)).FullName;
		processStartInfo.Environment["JAVA_HOME"] = fullName;
		IDictionary<string, string?> environment = processStartInfo.Environment;
		ReadOnlySpan<char> readOnlySpan = Path.Combine(fullName, "bin");
		char reference = Path.PathSeparator;
		environment["PATH"] = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference), Environment.GetEnvironmentVariable("PATH"));
		Process launcher = new Process
		{
			StartInfo = processStartInfo,
			EnableRaisingEvents = true
		};
		OwnedJavaClientProcess owner = null;
		try
		{
			foreach (string pathToValidate in new[] { instancesRoot, instanceRoot, Path.Combine(instanceRoot, "qa-world"), text, sessionRoot, sessionMetadataPath, logPath, java, module, wrapperJar })
				RejectReparsePoint(pathToValidate);
			await displayLease.ValidateForSideEffectAsync("launch-client", cancellationToken);
			foreach (string pathToValidate in new[] { instancesRoot, instanceRoot, Path.Combine(instanceRoot, "qa-world"), text, sessionRoot, sessionMetadataPath, logPath, java, module, wrapperJar })
				RejectReparsePoint(pathToValidate);
			if (!launcher.Start())
			{
				throw new InvalidOperationException("Gradle wrapper did not start the owned client candidate.");
			}
			owner = new OwnedJavaClientProcess(launcher, launcher, null, workspace, sessionRoot, logPath, displayLease);
			owner.StartLeaseMonitor();
			launcher.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
			{
				owner.AppendLog("OUT", e.Data);
			};
			launcher.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
			{
				owner.AppendLog("ERR", e.Data);
			};
			launcher.BeginOutputReadLine();
			launcher.BeginErrorReadLine();
			string descriptorPath = Path.Combine(sessionRoot, "endermanqa-client-bridge.json");
			DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(90.0);
			while (!File.Exists(descriptorPath) && DateTimeOffset.UtcNow < deadline)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (launcher.HasExited)
				{
					throw new InvalidOperationException("Owned client launcher exited before the bridge descriptor was created; inspect the private launch log.");
				}
				await Task.Delay(250, cancellationToken);
			}
			if (!File.Exists(descriptorPath))
			{
				throw new TimeoutException("Owned Minecraft client bridge did not become ready within 90 seconds.");
			}
			Process child = Process.GetProcessById(checked((int)(await ReadDescriptorPidAsync(descriptorPath, cancellationToken))));
			if (!Path.GetFullPath(child.MainModule?.FileName ?? "").Equals(java, StringComparison.OrdinalIgnoreCase) || !WindowsProcessTree.IsDescendantOf(child.Id, launcher.Id))
			{
				throw new InvalidOperationException("Client descriptor PID is not the expected Java process owned by this Workbench launcher.");
			}
			owner._client = child;
			await WindowsLeasedWindowRouter.RouteAndRevealAsync(child.Id, displayLease, cancellationToken);
			FabricClientBridgeClient bridge = await FabricClientBridgeClient.ConnectOwnedAsync(descriptorPath, sessionRoot, instanceRoot, workspace.InstanceId, child, java, cancellationToken);
			owner._bridge = bridge;
			DateTimeOffset readyDeadline = DateTimeOffset.UtcNow.AddSeconds(60.0);
			while (DateTimeOffset.UtcNow < readyDeadline)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (child.HasExited || launcher.HasExited)
				{
					throw new InvalidOperationException("Owned Minecraft client exited before joining the QA world.");
				}
				FabricClientBridgeStatus fabricClientBridgeStatus = await bridge.GetStatusAsync(cancellationToken);
				if (fabricClientBridgeStatus.ConnectedToServer && fabricClientBridgeStatus.WorldAvailable && fabricClientBridgeStatus.PlayerAvailable)
				{
					return owner;
				}
				await Task.Delay(500, cancellationToken);
			}
			throw new TimeoutException("Owned Minecraft client did not join the selected loopback QA server within 60 seconds.");
		}
		catch
		{
			if (owner != null)
			{
				owner._leaseMonitorCancellation.Cancel();
				if (owner._leaseMonitor != null)
				{
					try
					{
						await owner._leaseMonitor.ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (OperationCanceledException)
					{
					}
				}
				await owner.TerminateOwnedTreeAsync();
				owner._bridge?.Dispose();
				owner._client.Dispose();
				try
				{
					await displayLease.ReleaseAsync(CancellationToken.None);
				}
				catch (Exception)
				{
				}
				owner._leaseMonitorCancellation.Dispose();
			}
			else
			{
				if (!launcher.HasExited)
				{
					launcher.Kill(entireProcessTree: true);
				}
				try
				{
					await displayLease.ReleaseAsync(CancellationToken.None);
				}
				catch (Exception)
				{
				}
			}
			launcher.Dispose();
			throw;
		}
	}

	private static async Task<long> ReadDescriptorPidAsync(string path, CancellationToken cancellationToken)
	{
		RejectStaticReparseAncestors(path);
		long length = new FileInfo(path).Length;
		if ((length <= 0 || length > 16384) ? true : false)
		{
			throw new InvalidDataException("Client descriptor size is invalid.");
		}
		FileStream stream = File.OpenRead(path);
		long result;
		try
		{
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), cancellationToken);
			if (!jsonDocument.RootElement.TryGetProperty("processId", out var value) || !value.TryGetInt64(out var value2) || value2 <= 0)
			{
				throw new InvalidDataException("Client descriptor has no valid process ID.");
			}
			result = value2;
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

	private void AppendLog(string kind, string? line)
	{
		if (line == null)
		{
			return;
		}
		lock (_logGate)
		{
			try
			{
			RejectReparsePoint(_logPath);
				File.AppendAllText(_logPath, $"{DateTimeOffset.UtcNow:O} [{kind}] {line}{Environment.NewLine}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
			{
			}
		}
	}

	private static void RejectStaticReparseAncestors(string path)
	{
		string current = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(current))
		{
			try
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new InvalidDataException($"Client session path contains a reparse point at '{current}'.");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
			current = parent;
		}
	}

	private async Task TerminateOwnedTreeAsync()
	{
		if (_client != null && !_client.HasExited && WindowsProcessTree.IsDescendantOf(_client.Id, _launcher.Id))
		{
			try
			{
				_client.Kill(entireProcessTree: false);
				await _client.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5.0));
			}
			catch (Exception)
			{
			}
		}
		if (!_launcher.HasExited)
		{
			try
			{
				_launcher.Kill(entireProcessTree: true);
				await _launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10.0));
			}
			catch (Exception)
			{
			}
		}
	}

	public async Task StopAsync(TimeSpan timeout)
	{
		if (_stopped)
		{
			return;
		}
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(45.0))
		{
			throw new ArgumentOutOfRangeException("timeout");
		}
		_stopped = true;
		_leaseMonitorCancellation.Cancel();
		if (_leaseMonitor != null)
		{
			try
			{
				await _leaseMonitor.ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
			}
		}
		if (!_client.HasExited)
		{
			try
			{
				FabricClientBridgeClient bridge = _bridge;
				if (bridge != null)
				{
					await bridge.RequestStopAsync().WaitAsync(TimeSpan.FromSeconds(5.0));
				}
			}
			catch (Exception)
			{
			}
		}
		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(timeout);
			await _client.WaitForExitAsync(cts.Token);
			await _launcher.WaitForExitAsync(cts.Token);
		}
		catch (OperationCanceledException)
		{
			await TerminateOwnedTreeAsync();
		}
		finally
		{
			_bridge?.Dispose();
			try
			{
				await _displayLease.ReleaseAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				_leaseMonitorCancellation.Dispose();
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (!_stopped)
		{
			await StopAsync(TimeSpan.FromSeconds(20.0));
		}
		_client.Dispose();
		_launcher.Dispose();
	}
}
