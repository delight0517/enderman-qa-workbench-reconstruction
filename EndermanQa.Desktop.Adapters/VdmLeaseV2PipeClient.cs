using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace EndermanQa.Desktop.Adapters;

internal sealed class VdmLeaseV2PipeClient
{
	internal sealed record ProviderReadiness(string State, string Detail);

	private sealed record VerifiedPeer(string ExecutablePath, string Sha256);

	private const int MaximumRegistryBytes = 1048576;

	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4.0);

	private readonly string _registryPath;

	internal VdmLeaseV2PipeClient(string? registryPath = null)
	{
		_registryPath = registryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "gdrive", "My Drive", "Codex App Registry", "APP_PATH_REGISTRY.json");
	}

	internal ProviderReadiness ProbeReadiness()
	{
		if (!OperatingSystem.IsWindows())
		{
			return new ProviderReadiness("unsupported", "VDM task leases require Windows.");
		}
		try
		{
			ResolveVerifiedInstalledPeer(_registryPath);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is JsonException || ex is ArgumentException || ex is CryptographicException) ? 1 : 0) != 0)
		{
			return new ProviderReadiness("install-unverified", "No verified installed VDM executable/hash; candidate builds are not accepted.");
		}
		if (WaitNamedPipe("\\\\.\\pipe\\OnlyChatAiTaskDisplayLease.v2", 0u))
		{
			return new ProviderReadiness("endpoint-present", "Installed VDM verified and v2 lease pipe is present; readiness only, no lease was acquired.");
		}
		if (Marshal.GetLastWin32Error() != 121)
		{
			return new ProviderReadiness("endpoint-absent", "Installed VDM is verified, but the v2 task-lease pipe is not available.");
		}
		return new ProviderReadiness("endpoint-busy", "Installed VDM is verified; lease pipe is busy. No connection or lease was attempted.");
	}

	internal async Task<byte[]> SendOnceAsync(VdmLeaseV2Protocol.Request request, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(request, "request");
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("VDM lease IPC is Windows-only.");
		}
		int num = request.Utf8.Length;
		bool flag = ((num <= 1 || num > 8192) ? true : false);
		if (flag || request.Utf8[^1] != 10)
		{
			throw new InvalidDataException("VDM request is not one bounded newline-delimited frame.");
		}
		VerifiedPeer peer = ResolveVerifiedInstalledPeer(_registryPath);
		using NamedPipeClientStream pipe = new NamedPipeClientStream(".", "OnlyChatAiTaskDisplayLease.v2", PipeDirection.InOut, PipeOptions.WriteThrough | PipeOptions.Asynchronous);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(DefaultTimeout);
		bool mayHaveReachedProvider = false;
		try
		{
			await pipe.ConnectAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			VerifyConnectedServer(pipe.SafePipeHandle, peer);
			mayHaveReachedProvider = true;
			await pipe.WriteAsync(request.Utf8.AsMemory(), timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			await pipe.FlushAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			return await ReadOneFrameAsync(pipe, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex) when (((ex is IOException || ex is TimeoutException || ex is OperationCanceledException || ex is InvalidDataException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			if (cancellationToken.IsCancellationRequested && ex is OperationCanceledException)
			{
				throw;
			}
			throw new VdmLeaseTransportException(request.Action, mayHaveReachedProvider, ex);
		}
	}

	private static async Task<byte[]> ReadOneFrameAsync(NamedPipeClientStream pipe, CancellationToken cancellationToken)
	{
		byte[] frame = new byte[8192];
		int count = 0;
		byte[] oneByte = new byte[1];
		while (count < frame.Length)
		{
			if (await pipe.ReadAsync(oneByte.AsMemory(), cancellationToken).ConfigureAwait(continueOnCapturedContext: false) == 0)
			{
				throw new EndOfStreamException("VDM closed the pipe before its reply frame completed.");
			}
			if (oneByte[0] == 10)
			{
				if (count == 0)
				{
					throw new InvalidDataException("VDM returned an empty reply frame.");
				}
				byte[] array = new byte[count];
				Buffer.BlockCopy(frame, 0, array, 0, count);
				return array;
			}
			if (oneByte[0] == 13 || oneByte[0] == 0)
			{
				throw new InvalidDataException("VDM reply frame contains an invalid line or NUL delimiter.");
			}
			frame[count++] = oneByte[0];
		}
		throw new InvalidDataException("VDM reply exceeded the 8 KiB frame limit or omitted its newline.");
	}

	private static VerifiedPeer ResolveVerifiedInstalledPeer(string registryPath)
	{
		if (!File.Exists(registryPath))
		{
			throw new InvalidOperationException("The local application registry is unavailable; VDM lease IPC is disabled.");
		}
		long length = new FileInfo(registryPath).Length;
		if ((length <= 0 || length > 1048576) ? true : false)
		{
			throw new InvalidDataException("The local application registry size is invalid.");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllBytes(registryPath), new JsonDocumentOptions
		{
			MaxDepth = 16
		});
		JsonElement rootElement = jsonDocument.RootElement;
		if (rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("apps", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("The local application registry schema is invalid.");
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var value2) && value2.ValueKind == JsonValueKind.String && !(value2.GetString() != "ai-virtual-display-manager"))
			{
				if (!item.TryGetProperty("verified", out var value3) || value3.ValueKind != JsonValueKind.True || !item.TryGetProperty("windows", out var value4) || value4.ValueKind != JsonValueKind.Object || !value4.TryGetProperty("installedVerified", out var value5) || value5.ValueKind != JsonValueKind.True)
				{
					throw new InvalidOperationException("VDM has no verified installed identity; candidate processes are not trusted.");
				}
				string path = RequiredString(value4, "installed");
				string text = RequiredString(value4, "installedExeSha256");
				if (text.Length != 64 || !text.Equals(text.ToUpperInvariant(), StringComparison.Ordinal) || !IsHex(text))
				{
					throw new InvalidDataException("VDM installed executable hash is missing or malformed in the registry.");
				}
				string fullPath = Path.GetFullPath(Path.Combine(path, "AI Virtual Display Manager.exe"));
				if (!File.Exists(fullPath))
				{
					throw new FileNotFoundException("The registered VDM executable is missing.", fullPath);
				}
				if (!HashFile(fullPath).Equals(text, StringComparison.Ordinal))
				{
					throw new InvalidDataException("VDM installed executable hash does not match the registry.");
				}
				return new VerifiedPeer(fullPath, text);
			}
		}
		throw new InvalidDataException("The VDM app record is missing from the local registry.");
	}

	private static void VerifyConnectedServer(SafePipeHandle pipe, VerifiedPeer expected)
	{
		if (!GetNamedPipeServerProcessId(pipe, out var serverProcessId) || serverProcessId == 0)
		{
			throw new IOException("Could not identify the local VDM pipe server process.");
		}
		using Process process = Process.GetProcessById(checked((int)serverProcessId));
		if (process.HasExited)
		{
			throw new IOException("VDM pipe server exited during identity verification.");
		}
		string fullPath = Path.GetFullPath(process.MainModule?.FileName ?? string.Empty);
		if (!fullPath.Equals(expected.ExecutablePath, StringComparison.OrdinalIgnoreCase))
		{
			throw new UnauthorizedAccessException("VDM pipe server image is not the verified installed executable.");
		}
		if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(HashFile(fullPath)), Convert.FromHexString(expected.Sha256)))
		{
			throw new UnauthorizedAccessException("VDM pipe server image digest differs from the registered installation.");
		}
		if (process.HasExited)
		{
			throw new IOException("VDM pipe server exited during identity verification.");
		}
	}

	private static string RequiredString(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			throw new InvalidDataException("The local app registry is missing '" + name + "'.");
		}
		return value.GetString();
	}

	private static bool IsHex(string value)
	{
		for (int i = 0; i < value.Length; i++)
		{
			if (!Uri.IsHexDigit(value[i]))
			{
				return false;
			}
		}
		return true;
	}

	private static string HashFile(string path)
	{
		using FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "WaitNamedPipeW", SetLastError = true)]
	private static extern bool WaitNamedPipe(string name, uint timeoutMilliseconds);
}
