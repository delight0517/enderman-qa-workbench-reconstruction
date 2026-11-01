using System;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

public sealed class FabricServerInstaller
{
	[CompilerGenerated]
	private HttpClient _003Chttp_003EP;

	private readonly string _instancesRoot;

	private const string LoaderUrl = "https://meta.fabricmc.net/v2/versions/loader/26.3/0.20.1/1.1.2/server/jar";

	private const string ApiUrl = "https://maven.fabricmc.net/net/fabricmc/fabric-api/fabric-api/0.165.0+26.3/fabric-api-0.165.0+26.3.jar";

	internal const string LoaderSha256 = "70446947655B4F8F286ABA896AA4C19A255E8CA8A730636EF2A6017AE4415D95";

	internal const string ApiSha256 = "18442E2AD466D6EF41C37DBB4CB68495E6CA6658B1F1EE9BE76143D155EF2C88";

	public FabricServerInstaller(HttpClient http, string instancesRoot)
	{
		_003Chttp_003EP = http;
		_instancesRoot = Path.GetFullPath(instancesRoot);
	}

	public async Task InstallAsync(JavaServerWorkspace workspace, CancellationToken cancellationToken = default(CancellationToken))
	{
		string item = JavaServerWorkspaceManager.ValidateWorkspaceForInstall(workspace, _instancesRoot).Root;
		await DownloadVerifiedAsync("https://meta.fabricmc.net/v2/versions/loader/26.3/0.20.1/1.1.2/server/jar", Path.Combine(item, "fabric-server-launch.jar"), "70446947655B4F8F286ABA896AA4C19A255E8CA8A730636EF2A6017AE4415D95", 4194304, cancellationToken);
		await DownloadVerifiedAsync("https://maven.fabricmc.net/net/fabricmc/fabric-api/fabric-api/0.165.0+26.3/fabric-api-0.165.0+26.3.jar", Path.Combine(workspace.ModsPath, "fabric-api-0.165.0+26.3.jar"), "18442E2AD466D6EF41C37DBB4CB68495E6CA6658B1F1EE9BE76143D155EF2C88", 12582912, cancellationToken);
	}

	private async Task DownloadVerifiedAsync(string url, string destination, string expectedHash, int maximumBytes, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await _003Chttp_003EP.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		long? contentLength = response.Content.Headers.ContentLength;
		if (contentLength.HasValue)
		{
			long valueOrDefault = contentLength.GetValueOrDefault();
			if (valueOrDefault < 1 || valueOrDefault > maximumBytes)
			{
				throw new InvalidDataException("Fabric download size is outside its pinned safety limit.");
			}
		}
		Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
		try
		{
			MemoryStream output = new MemoryStream();
			try
			{
				byte[] buffer = new byte[8192];
				while (true)
				{
					int num = await input.ReadAsync(buffer, cancellationToken);
					if (num == 0)
					{
						break;
					}
					if (output.Length + num > maximumBytes)
					{
						throw new InvalidDataException("Fabric download exceeded its pinned safety limit.");
					}
					await output.WriteAsync(buffer.AsMemory(0, num), cancellationToken);
				}
				byte[] array = output.ToArray();
				if (!Convert.ToHexString(SHA256.HashData(array)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Downloaded Fabric artifact SHA-256 did not match the pinned hash.");
				}
				string staging = destination + ".part";
				await File.WriteAllBytesAsync(staging, array, cancellationToken);
				File.Move(staging, destination, overwrite: false);
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
}
