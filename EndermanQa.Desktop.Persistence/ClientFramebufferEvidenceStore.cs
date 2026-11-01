using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EndermanQa.Desktop.Adapters;

namespace EndermanQa.Desktop.Persistence;

internal sealed class ClientFramebufferEvidenceStore
{
	internal sealed record StoredEvidence(string ProjectRelativeFolder, string PngPath, string SidecarPath, string IndexPath, string Sha256, long Bytes, string? CaseReference);

	internal async Task<StoredEvidence> StoreAsync(string instancesRoot, string projectDataRoot, string ownedSessionRoot, OwnedJavaClientProcess.CaptureEvidence evidence, string? caseReference)
	{
		ArgumentNullException.ThrowIfNull(evidence, "evidence");
		RejectReparsePoint(instancesRoot);
		RejectReparsePoint(projectDataRoot);
		RejectReparsePoint(ownedSessionRoot);
		RejectReparsePoint(evidence.PngPath);
		RejectReparsePoint(evidence.SidecarPath);
		if (!Guid.TryParseExact(evidence.SessionId, "D", out var sessionId) || !Guid.TryParseExact(evidence.RequestId, "D", out var requestId))
		{
			throw new InvalidDataException("Client capture IDs are not canonical GUIDs.");
		}
		string expectedName = $"capture-{requestId:D}.png";
		string fullPath = Path.GetFullPath(ownedSessionRoot);
		string fullPath2 = Path.GetFullPath(evidence.PngPath);
		string sourceSidecar = Path.GetFullPath(evidence.SidecarPath);
		DirectoryInfo parent = Directory.GetParent(fullPath);
		DirectoryInfo directoryInfo = parent?.Parent;
		if (!Path.GetFileName(fullPath).Equals(sessionId.ToString("D"), StringComparison.OrdinalIgnoreCase) || parent == null || !parent.Name.Equals("client-runs", StringComparison.OrdinalIgnoreCase) || directoryInfo == null || !Path.GetFullPath(directoryInfo.FullName).Equals(WorkbenchInstancePaths.ResolveInstanceRoot(evidence.InstanceId, directoryInfo.FullName, instancesRoot), StringComparison.OrdinalIgnoreCase) || !Path.GetDirectoryName(fullPath2).Equals(fullPath, StringComparison.OrdinalIgnoreCase) || !sourceSidecar.Equals(fullPath2 + ".json", StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(fullPath2).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Capture artifacts do not belong to the exact owned client session.");
		}
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(projectDataRoot).TrimEnd(Path.DirectorySeparatorChar);
		char reference2 = Path.DirectorySeparatorChar;
		string text = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference2));
		string fullPath3 = Path.GetFullPath(Path.Combine(text, "evidence", "client-captures"));
		if (!fullPath3.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Capture evidence path escaped the active project data root.");
		}
		RejectReparsePoint(fullPath3);
		Directory.CreateDirectory(fullPath3);
		RejectReparsePoint(fullPath3);
		string text2 = Path.Combine(fullPath3, sessionId.ToString("D"));
		RejectReparsePoint(text2);
		Directory.CreateDirectory(text2);
		RejectReparsePoint(text2);
		string finalDirectory = Path.Combine(text2, requestId.ToString("D"));
		string stagingDirectory = Path.Combine(text2, ".pending-" + Guid.NewGuid().ToString("N"));
		RejectReparsePoint(finalDirectory);
		RejectReparsePoint(stagingDirectory);
		Directory.CreateDirectory(stagingDirectory);
		RejectReparsePoint(stagingDirectory);
		try
		{
			string pngPath = Path.Combine(stagingDirectory, expectedName);
			string sidecarPath = pngPath + ".json";
			RejectReparsePoint(pngPath);
			RejectReparsePoint(sidecarPath);
			await CopyNewAsync(fullPath2, pngPath).ConfigureAwait(continueOnCapturedContext: false);
			await CopyNewAsync(sourceSidecar, sidecarPath).ConfigureAwait(continueOnCapturedContext: false);
			FileStream stream = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
			string storedHash;
			try
			{
				storedHash = Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(continueOnCapturedContext: false)).ToLowerInvariant();
			}
			finally
			{
				if (stream != null)
				{
					await stream.DisposeAsync();
				}
			}
			FileInfo info = new FileInfo(pngPath);
			if (!storedHash.Equals(evidence.Sha256, StringComparison.OrdinalIgnoreCase) || info.Length != evidence.Bytes)
			{
				throw new InvalidDataException("Project copy does not match the verified framebuffer receipt.");
			}
			string reference = (string.IsNullOrWhiteSpace(caseReference) ? null : caseReference.Trim());
			if (reference != null && reference.Length > 64)
			{
				throw new ArgumentException("Case reference is too long.", "caseReference");
			}
			string path = Path.Combine(stagingDirectory, "capture-index.json");
			RejectReparsePoint(path);
			string contents = JsonSerializer.Serialize(new
			{
				format = "endermanqa-project-client-capture-v1",
				recordedAtUtc = DateTimeOffset.UtcNow,
				caseReference = reference,
				instanceId = evidence.InstanceId,
				sessionId = evidence.SessionId,
				targetId = evidence.TargetId,
				gameVersion = evidence.GameVersion,
				dimension = evidence.Dimension,
				gameTick = evidence.GameTick,
				processId = evidence.ProcessId,
				captureRequestId = evidence.RequestId,
				captureSource = evidence.CaptureSource,
				width = evidence.Width,
				height = evidence.Height,
				bytes = info.Length,
				sha256 = storedHash,
				image = expectedName,
				sidecar = Path.GetFileName(sidecarPath)
			}, new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true
			});
			await File.WriteAllTextAsync(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(continueOnCapturedContext: false);
			RejectReparsePoint(finalDirectory);
			Directory.Move(stagingDirectory, finalDirectory);
			RejectReparsePoint(finalDirectory);
			return new StoredEvidence(Path.Combine("evidence", "client-captures", sessionId.ToString("D"), requestId.ToString("D")), Path.Combine(finalDirectory, expectedName), Path.Combine(finalDirectory, expectedName + ".json"), Path.Combine(finalDirectory, "capture-index.json"), storedHash, info.Length, reference);
		}
		catch
		{
			try
			{
				if (Directory.Exists(stagingDirectory))
				{
					RejectReparsePoint(stagingDirectory);
					Directory.Delete(stagingDirectory, recursive: true);
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
			throw;
		}
	}

	private static async Task CopyNewAsync(string source, string destination)
	{
		RejectReparsePoint(source);
		RejectReparsePoint(destination);
		FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
		try
		{
			FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous);
			try
			{
				await input.CopyToAsync(output).ConfigureAwait(continueOnCapturedContext: false);
				await output.FlushAsync().ConfigureAwait(continueOnCapturedContext: false);
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

	private static void RejectReparsePoint(string path)
	{
		string current = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(current))
		{
			try
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new InvalidDataException($"Client framebuffer evidence path contains a reparse point at '{current}'.");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
			current = parent;
		}
	}
}
