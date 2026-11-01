using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

public sealed class AiVirtualDisplayManagerClient
{
	public const string PipeName = "OnlyChatAiDebugDisplay.v1";

	private const int MaxReplyBytes = 4096;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true
	};

	public async Task<VirtualDisplayManagerStatus> GetStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using NamedPipeClientStream pipe = new NamedPipeClientStream(".", "OnlyChatAiDebugDisplay.v1", PipeDirection.InOut, PipeOptions.Asynchronous);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(2.0));
		await pipe.ConnectAsync(timeout.Token);
		byte[] array = JsonSerializer.SerializeToUtf8Bytes(new
		{
			version = 1,
			request_id = Guid.NewGuid().ToString(),
			action = "status"
		}, JsonOptions);
		await pipe.WriteAsync(array, timeout.Token);
		await pipe.WriteAsync(new byte[1] { 10 }, timeout.Token);
		await pipe.FlushAsync(timeout.Token);
		using MemoryStream reply = new MemoryStream();
		byte[] buffer = new byte[512];
		while (reply.Length <= 4096)
		{
			int num = await pipe.ReadAsync(buffer, timeout.Token);
			if (num == 0)
			{
				throw new EndOfStreamException("VDM closed the status connection without a reply.");
			}
			int num2 = Array.IndexOf(buffer, (byte)10, 0, num);
			int count = ((num2 >= 0) ? num2 : num);
			reply.Write(buffer, 0, count);
			if (num2 >= 0)
			{
				break;
			}
		}
		long length = reply.Length;
		if ((length <= 0 || length > 4096) ? true : false)
		{
			throw new InvalidDataException("VDM status reply exceeds protocol limit.");
		}
		VirtualDisplayManagerStatus virtualDisplayManagerStatus = JsonSerializer.Deserialize<VirtualDisplayManagerStatus>(reply.ToArray(), JsonOptions) ?? throw new InvalidDataException("VDM status reply is malformed.");
		bool flag = virtualDisplayManagerStatus.Version != 1 || string.IsNullOrWhiteSpace(virtualDisplayManagerStatus.RequestId);
		if (!flag)
		{
			bool flag2;
			switch (virtualDisplayManagerStatus.State)
			{
			case "active":
			case "idle":
			case "failed":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = !flag2;
		}
		bool flag3 = flag || virtualDisplayManagerStatus.IdleSeconds < 0;
		if (!flag3)
		{
			int idleTimeoutSeconds = virtualDisplayManagerStatus.IdleTimeoutSeconds;
			bool flag2 = ((idleTimeoutSeconds < 1 || idleTimeoutSeconds > 3600) ? true : false);
			flag3 = flag2;
		}
		if (flag3 || (virtualDisplayManagerStatus.DisplayEnabled && virtualDisplayManagerStatus.State != "active"))
		{
			throw new InvalidDataException("VDM status reply failed contract validation.");
		}
		return virtualDisplayManagerStatus;
	}
}
