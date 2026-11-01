using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

public sealed class OwnedJavaServerProcess : IAsyncDisposable
{
	private readonly Process _process;

	private readonly JavaServerWorkspace _workspace;

	private readonly string _javaExecutable;

	public bool HasExited => _process.HasExited;

	public int ProcessId => _process.Id;

	public JavaServerWorkspace Workspace => _workspace;

	public string JavaExecutable => _javaExecutable;

	private OwnedJavaServerProcess(Process process, JavaServerWorkspace workspace, string javaExecutable)
	{
		_process = process;
		_workspace = workspace;
		_javaExecutable = Path.GetFullPath(javaExecutable);
	}

	public static OwnedJavaServerProcess Start(JavaServerWorkspace workspace, string javaExecutable, bool userAcceptedMinecraftEula, string instancesRoot)
	{
		string text = JavaServerWorkspaceManager.BuildAcknowledgedServerArguments(workspace, userAcceptedMinecraftEula, instancesRoot);
		string fullPath = Path.GetFullPath(workspace.RootPath);
		string eulaPath = Path.Combine(fullPath, "eula.txt");
		string propertiesPath = Path.Combine(fullPath, "server.properties");
		string modsPath = Path.Combine(fullPath, "mods");
		string loaderPath = Path.Combine(fullPath, "fabric-server-launch.jar");
		string bridgePath = Path.Combine(modsPath, Path.GetFileName(workspace.ModJarPath));
		string baselinePath = Path.Combine(fullPath, "endermanqa-mob-spawning.baseline");
		string worldPath = Path.Combine(fullPath, "qa-world");
		string log = Path.Combine(fullPath, "workbench-server.log");
		foreach (string path in new[] { instancesRoot, fullPath, eulaPath, propertiesPath, modsPath, loaderPath, bridgePath, baselinePath, worldPath, log, javaExecutable })
			RejectReparseAncestors(path);
		if (!File.Exists(loaderPath))
		{
			throw new FileNotFoundException("Fabric server launcher is not installed in this private instance.", loaderPath);
		}
		ProcessStartInfo startInfo = new ProcessStartInfo(Path.GetFullPath(javaExecutable))
		{
			WorkingDirectory = fullPath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			ArgumentList = 
			{
				"-Dendermanqa.bridge.enabled=true",
				"-Dendermanqa.instanceId=" + workspace.InstanceId,
				"-jar",
				"fabric-server-launch.jar",
				"nogui"
			}
		};
		if (!text.Contains("-Dendermanqa.bridge.enabled=true", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Unexpected server launch arguments.");
		}
		foreach (string path in new[] { instancesRoot, fullPath, eulaPath, propertiesPath, modsPath, loaderPath, bridgePath, baselinePath, worldPath, log, javaExecutable })
			RejectReparseAncestors(path);
		Process? obj = Process.Start(startInfo) ?? throw new InvalidOperationException("Java server process did not start.");
		obj.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				try
				{
					RejectReparseAncestors(log);
					File.AppendAllText(log, e.Data + Environment.NewLine);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
				{
				}
			}
		};
		obj.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				try
				{
					RejectReparseAncestors(log);
					File.AppendAllText(log, e.Data + Environment.NewLine);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
				{
				}
			}
		};
		obj.BeginOutputReadLine();
		obj.BeginErrorReadLine();
		return new OwnedJavaServerProcess(obj, workspace, javaExecutable);
	}

	public async Task<FabricJavaGameAdapter> ConnectGameAdapterAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		string descriptor = Path.Combine(_workspace.RootPath, "endermanqa-bridge.json");
		RejectReparseAncestors(descriptor);
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(60.0);
		while (!File.Exists(descriptor) && !HasExited && DateTimeOffset.UtcNow < deadline)
		{
			RejectReparseAncestors(descriptor);
			await Task.Delay(250, cancellationToken);
		}
		if (HasExited)
		{
			throw new InvalidOperationException("App-owned Java server exited before the Fabric bridge became ready.");
		}
		if (!File.Exists(descriptor))
		{
			throw new TimeoutException("Fabric bridge descriptor did not appear within 60 seconds. See workbench-server.log in the private instance.");
		}
		RejectReparseAncestors(descriptor);
		return await FabricJavaGameAdapter.ConnectOwnedAsync(_workspace, _process, _javaExecutable, cancellationToken);
	}

	public async Task StopAsync(TimeSpan timeout)
	{
		if (_process.HasExited)
		{
			RestorePauseSetting();
			return;
		}
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30.0))
		{
			throw new ArgumentOutOfRangeException("timeout");
		}
		try
		{
			try
			{
				await _process.StandardInput.WriteLineAsync("stop");
			}
			catch
			{
			}
			using CancellationTokenSource cts = new CancellationTokenSource(timeout);
			try
			{
				await _process.WaitForExitAsync(cts.Token);
			}
			catch (OperationCanceledException)
			{
				if (!_process.HasExited)
				{
					_process.Kill(entireProcessTree: true);
				}
				await _process.WaitForExitAsync();
			}
		}
		finally
		{
			RestorePauseSetting();
		}
	}

	private void RestorePauseSetting()
	{
		string previousPauseWhenEmptySeconds = _workspace.PreviousPauseWhenEmptySeconds;
		if (previousPauseWhenEmptySeconds == null)
		{
			return;
		}
		string path = Path.Combine(_workspace.RootPath, "server.properties");
		RejectReparseAncestors(path);
		if (File.Exists(path))
		{
			string[] array = File.ReadAllLines(path);
			int num = Array.FindIndex(array, (string line) => line.StartsWith("pause-when-empty-seconds=", StringComparison.OrdinalIgnoreCase));
			if (num >= 0 && !(array[num] != "pause-when-empty-seconds=0"))
			{
				array[num] = "pause-when-empty-seconds=" + previousPauseWhenEmptySeconds;
				RejectReparseAncestors(path);
				File.WriteAllLines(path, array, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
	}

	private static void RejectReparseAncestors(string path)
	{
		string current = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(current))
		{
			try
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new System.Security.SecurityException($"Owned server path contains a reparse point at '{current}'.");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
			current = parent;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync(TimeSpan.FromSeconds(15.0));
		_process.Dispose();
	}
}
