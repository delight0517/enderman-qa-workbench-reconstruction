using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EndermanQa.Desktop.Adapters;

namespace EndermanQa.Desktop.Persistence;

public sealed class ProjectClientCaptureStore
{
	private readonly string _projectRoot;

	private readonly string _instancesRoot;

	public ProjectClientCaptureStore(string projectRoot, string instancesRoot)
	{
		_projectRoot = Path.GetFullPath(projectRoot);
		_instancesRoot = Path.GetFullPath(instancesRoot);
		RejectDirectoryReparsePoint(_projectRoot);
		RejectDirectoryReparsePoint(_instancesRoot);
		RejectDirectoryReparsePoint(Path.Combine(_projectRoot, "project.json"));
		if (!File.Exists(Path.Combine(_projectRoot, "project.json")))
		{
			throw new InvalidDataException("Capture destination is not a registered Workbench project root.");
		}
	}

	public async Task<SavedClientCapture> SaveAsync(string instanceRoot, string sessionRoot, OwnedJavaClientProcess.CaptureEvidence capture, string runId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(capture, "capture");
		RejectDirectoryReparsePoint(_projectRoot);
		RejectDirectoryReparsePoint(_instancesRoot);
		Guid result;
		bool flag = !Guid.TryParseExact(runId, "N", out result) || !Guid.TryParse(capture.RequestId, out result) || !Guid.TryParse(capture.InstanceId, out result) || capture.ProcessId <= 0 || capture.GameVersion != "26.3" || capture.CaptureSource != "minecraft-client-main-framebuffer";
		if (!flag)
		{
			int width = capture.Width;
			bool flag2 = ((width < 1 || width > 8192) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			int width = capture.Height;
			bool flag2 = ((width < 1 || width > 8192) ? true : false);
			flag3 = flag2;
		}
		bool flag4 = flag3;
		if (!flag4)
		{
			long bytes = capture.Bytes;
			bool flag2 = ((bytes < 1 || bytes > 52428800) ? true : false);
			flag4 = flag2;
		}
		if (flag4 || capture.Sha256.Length != 64 || !capture.Sha256.All(Uri.IsHexDigit))
		{
			throw new InvalidDataException("Capture receipt does not meet the project storage contract.");
		}
		string fullPath = WorkbenchInstancePaths.ResolveInstanceRoot(capture.InstanceId, instanceRoot, _instancesRoot);
		RejectDirectoryReparsePoint(fullPath);
		if (!Path.GetFullPath(instanceRoot).Equals(fullPath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Capture source instance is not a Workbench-owned Minecraft instance.");
		}
		string fullPath2 = Path.GetFullPath(Path.Combine(fullPath, "client-runs", Path.GetFileName(Path.GetFullPath(sessionRoot))));
		RejectDirectoryReparsePoint(Path.Combine(fullPath, "client-runs"));
		RejectDirectoryReparsePoint(fullPath2);
		if (!Path.GetFullPath(sessionRoot).Equals(fullPath2, StringComparison.OrdinalIgnoreCase) || capture.SessionId != Path.GetFileName(fullPath2))
		{
			throw new InvalidOperationException("Capture source session is outside the owned client-runs directory.");
		}
		string text = "capture-" + capture.RequestId + ".png";
		string path = text + ".json";
		string sourcePng = Path.Combine(fullPath2, text);
		string sourceSidecar = Path.Combine(fullPath2, path);
		RejectReparsePoint(sourcePng);
		RejectReparsePoint(sourceSidecar);
		if (!Path.GetFullPath(capture.PngPath).Equals(sourcePng, StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(capture.SidecarPath).Equals(sourceSidecar, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Capture evidence paths do not match the request-scoped private session files.");
		}
		RejectReparsePoint(sourcePng);
		RejectReparsePoint(sourceSidecar);
		FileInfo fileInfo = new FileInfo(sourcePng);
		if (fileInfo.Length != capture.Bytes || fileInfo.Length > 52428800)
		{
			throw new InvalidDataException("Capture file size changed after process/session verification.");
		}
		if (!(await HashFileAsync(sourcePng, cancellationToken)).Equals(capture.Sha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Capture source hash changed before project persistence.");
		}
		string fullPath3 = Path.GetFullPath(Path.Combine(_projectRoot, "evidence", "minecraft-client"));
		RejectDirectoryReparsePoint(fullPath3);
		string directoryName = Path.GetDirectoryName(fullPath3);
		if (Directory.Exists(directoryName))
		{
			RejectDirectoryReparsePoint(directoryName);
		}
		Directory.CreateDirectory(directoryName);
		RejectDirectoryReparsePoint(directoryName);
		if (Directory.Exists(fullPath3))
		{
			RejectDirectoryReparsePoint(fullPath3);
		}
		Directory.CreateDirectory(fullPath3);
		RejectDirectoryReparsePoint(fullPath3);
		string relativePath = Path.GetRelativePath(_projectRoot, fullPath3);
		if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Capture evidence directory escaped the project root.");
		}
		string path2 = capture.CapturedAt.UtcDateTime.ToString("yyyy-MM-dd");
		string relativeRun = Path.Combine("evidence", "minecraft-client", path2, runId);
		string destination = Path.GetFullPath(Path.Combine(_projectRoot, relativeRun));
		string directoryName2 = Path.GetDirectoryName(destination);
		RejectDirectoryReparsePoint(destination);
		RejectDirectoryReparsePoint(directoryName2);
		ReadOnlySpan<char> readOnlySpan = fullPath3;
		char reference = Path.DirectorySeparatorChar;
		if (!destination.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Capture run directory escaped the project evidence root.");
		}
		if (Directory.Exists(destination) || File.Exists(destination))
		{
			throw new IOException("Capture run folder already exists; existing evidence will not be overwritten.");
		}
		Directory.CreateDirectory(directoryName2);
		RejectDirectoryReparsePoint(directoryName2);
		string staging = Path.Combine(directoryName2, ".capture-stage-" + Guid.NewGuid().ToString("N"));
		RejectDirectoryReparsePoint(staging);
		Directory.CreateDirectory(staging);
		RejectDirectoryReparsePoint(staging);
		try
		{
			string stagedPng = Path.Combine(staging, "frame.png");
			string stagedSidecar = Path.Combine(staging, "frame.json");
			RejectReparsePoint(stagedPng);
			RejectReparsePoint(stagedSidecar);
			await CopyNewAsync(sourcePng, stagedPng, cancellationToken);
			await CopyNewAsync(sourceSidecar, stagedSidecar, cancellationToken);
			if (!(await HashFileAsync(stagedPng, cancellationToken)).Equals(capture.Sha256, StringComparison.OrdinalIgnoreCase) || new FileInfo(stagedPng).Length != capture.Bytes)
			{
				throw new InvalidDataException("Staged screenshot differs from its verified client-framebuffer source.");
			}
			RejectDirectoryReparsePoint(staging);
			RejectDirectoryReparsePoint(destination);
			Directory.Move(staging, destination);
			RejectDirectoryReparsePoint(destination);
		}
		catch
		{
			if (Directory.Exists(staging))
			{
				RejectDirectoryReparsePoint(staging);
				Directory.Delete(staging, recursive: true);
			}
			throw;
		}
		return new SavedClientCapture(runId, Path.Combine(relativeRun, "frame.png"), Path.Combine(relativeRun, "frame.json"), capture.Bytes, capture.Sha256.ToLowerInvariant(), capture.Width, capture.Height, capture.GameVersion, capture.Dimension, capture.GameTick, capture.ProcessId, capture.InstanceId, capture.TargetId, capture.CapturedAt);
	}

	private static async Task CopyNewAsync(string source, string destination, CancellationToken cancellationToken)
	{
		RejectReparsePoint(source);
		RejectReparsePoint(destination);
		FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		try
		{
			FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous);
			try
			{
				await input.CopyToAsync(output, 65536, cancellationToken);
				await output.FlushAsync(cancellationToken);
			}
			finally
			{
				if (output != null)
				{
					await output.DisposeAsync();
				}
			}
		}
		finally
		{
			if (input != null)
			{
				await input.DisposeAsync();
			}
		}
	}

	private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
	{
		FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		string result;
		try
		{
			result = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
		}
		finally
		{
			if (input != null)
			{
				await input.DisposeAsync();
			}
		}
		return result;
	}

	private static void RejectReparsePoint(string path)
	{
		RejectDirectoryReparsePoint(path);
	}

	private static void RejectDirectoryReparsePoint(string path)
	{
		string current = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(current))
		{
			try
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new InvalidDataException($"Capture project/session path contains a reparse point at '{current}'.");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
			current = parent;
		}
	}
}
