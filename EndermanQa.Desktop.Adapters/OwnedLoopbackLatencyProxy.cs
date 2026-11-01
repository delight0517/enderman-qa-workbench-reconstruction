using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

public sealed class OwnedLoopbackLatencyProxy : IAsyncDisposable
{
	public sealed record SessionReceipt(string Format, string Scope, string InstanceId, int ServerProcessId, int ClientProcessId, int ListenPort, DateTimeOffset StartedAtUtc, DateTimeOffset? StoppedAtUtc, int ClientToServerDelayMilliseconds, int ServerToClientDelayMilliseconds, bool ClientAccepted, bool ConnectedToServer, long ClientToServerBytes, long ServerToClientBytes, string? Failure, string InterpretationLimit);

	private const int BufferSize = 16384;

	private readonly TcpListener _listener;

	private readonly IPAddress _destinationAddress;

	private readonly int _destinationPort;

	private readonly int _listenPort;

	private readonly TimeSpan _clientToServerDelay;

	private readonly TimeSpan _serverToClientDelay;

	private readonly int _serverProcessId;

	private readonly string _instanceId;

	private readonly string _instancesRoot;

	private readonly CancellationTokenSource _stop = new CancellationTokenSource();

	private readonly ConcurrentDictionary<TcpClient, byte> _sockets = new ConcurrentDictionary<TcpClient, byte>();

	private readonly Task _acceptLoop;

	private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;

	private int _accepted;

	private int _connectedToServer;

	private long _clientToServerBytes;

	private long _serverToClientBytes;

	private int _disposed;

	private DateTimeOffset? _stoppedAtUtc;

	public int ListenPort => _listenPort;

	public string Scope => "one-loopback-client-to-owned-loopback-server; bounded-stream-read-delay";

	public string? Failure { get; private set; }

	public bool ClientAccepted => Volatile.Read(ref _accepted) != 0;

	public bool ConnectedToServer => Volatile.Read(ref _connectedToServer) != 0;

	public long ClientToServerBytes => Interlocked.Read(ref _clientToServerBytes);

	public long ServerToClientBytes => Interlocked.Read(ref _serverToClientBytes);

	public int ClientToServerDelayMilliseconds => checked((int)_clientToServerDelay.TotalMilliseconds);

	public int ServerToClientDelayMilliseconds => checked((int)_serverToClientDelay.TotalMilliseconds);

	private OwnedLoopbackLatencyProxy(OwnedJavaServerProcess serverOwner, string instancesRoot, IPAddress destinationAddress, int destinationPort, TimeSpan clientToServerDelay, TimeSpan serverToClientDelay)
	{
		_serverProcessId = serverOwner.ProcessId;
		_instanceId = serverOwner.Workspace.InstanceId;
		_instancesRoot = Path.GetFullPath(instancesRoot);
		_destinationAddress = destinationAddress;
		_destinationPort = destinationPort;
		_clientToServerDelay = clientToServerDelay;
		_serverToClientDelay = serverToClientDelay;
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start(1);
		_listenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_acceptLoop = AcceptOneAsync();
	}

	internal bool Matches(OwnedJavaServerProcess owner)
	{
		if (owner.ProcessId == _serverProcessId)
		{
			return owner.Workspace.InstanceId.Equals(_instanceId, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static OwnedLoopbackLatencyProxy Start(OwnedJavaServerProcess serverOwner, string instancesRoot, TimeSpan clientToServerDelay, TimeSpan serverToClientDelay)
	{
		ArgumentNullException.ThrowIfNull(serverOwner, "serverOwner");
		if (serverOwner.HasExited || serverOwner.ProcessId <= 0)
		{
			throw new InvalidOperationException("Latency proxy requires the exact live Workbench-owned server process.");
		}
		JavaServerWorkspace workspace = serverOwner.Workspace;
		WorkbenchInstancePaths.ResolveInstanceRoot(workspace.InstanceId, workspace.RootPath, instancesRoot);
		IPAddress address;
		bool flag = !IPAddress.TryParse("127.0.0.1", out address);
		if (!flag)
		{
			int serverPort = workspace.ServerPort;
			bool flag2 = ((serverPort < 1 || serverPort > 65535) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("Latency proxy target must be the selected server's IPv4 loopback endpoint.");
		}
		ValidateDelay(clientToServerDelay, "clientToServerDelay");
		ValidateDelay(serverToClientDelay, "serverToClientDelay");
		return new OwnedLoopbackLatencyProxy(serverOwner, instancesRoot, address, workspace.ServerPort, clientToServerDelay, serverToClientDelay);
	}

	public async Task<string> WriteSessionReceiptAsync(string clientSessionRoot, int clientProcessId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (clientProcessId <= 0 || Interlocked.CompareExchange(ref _disposed, 0, 0) == 0)
		{
			throw new InvalidOperationException("Latency proxy receipt is written after this session proxy stops and with an exact client PID.");
		}
		string fullPath = WorkbenchInstancePaths.ResolveInstanceRoot(_instanceId, Path.Combine(_instancesRoot, _instanceId), _instancesRoot);
		string fullSessionRoot = Path.GetFullPath(clientSessionRoot);
		string relativePath = Path.GetRelativePath(fullPath, fullSessionRoot);
		string[] segments = relativePath.Split(new char[2]
		{
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar
		}, StringSplitOptions.RemoveEmptyEntries);
		string text = Path.Combine(fullSessionRoot, "client-session.json");
		if (Path.IsPathRooted(relativePath) || segments.Length != 2 || segments[0] != "client-runs" || !Guid.TryParse(segments[1], out var _) || !File.Exists(text) || new FileInfo(text).Length > 16384)
		{
			throw new InvalidDataException("Latency receipt target is not the exact owned client's session directory.");
		}
		using (JsonDocument jsonDocument = JsonDocument.Parse(await File.ReadAllTextAsync(text, cancellationToken)))
		{
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("instanceId", out var value) || value.GetString() != _instanceId || !rootElement.TryGetProperty("sessionId", out var value2) || value2.GetString() != segments[1])
			{
				throw new InvalidDataException("Latency receipt session marker does not match its owned instance/session.");
			}
		}
		string path = Path.Combine(fullSessionRoot, "latency-proxy-session.json");
		SessionReceipt value3 = new SessionReceipt("endermanqa-loopback-stream-delay-v1", Scope, _instanceId, _serverProcessId, clientProcessId, ListenPort, _startedAtUtc, _stoppedAtUtc, ClientToServerDelayMilliseconds, ServerToClientDelayMilliseconds, ClientAccepted, ConnectedToServer, ClientToServerBytes, ServerToClientBytes, Failure, "Delay is applied once per bounded NetworkStream read; byte totals and pipe state do not equal RTT, per-packet latency, or proof of game actions.");
		FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.WriteThrough | FileOptions.Asynchronous);
		string result2;
		try
		{
			await JsonSerializer.SerializeAsync(stream, value3, new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true
			}, cancellationToken);
			await stream.FlushAsync(cancellationToken);
			stream.Flush(flushToDisk: true);
			using FileStream readback = File.OpenRead(path);
			string text2 = Convert.ToHexString(await SHA256.HashDataAsync(readback, cancellationToken));
			result2 = path + " sha256=" + text2;
		}
		finally
		{
			if (stream != null)
			{
				await stream.DisposeAsync();
			}
		}
		return result2;
	}

	private static void ValidateDelay(TimeSpan delay, string name)
	{
		if (delay < TimeSpan.Zero || delay > TimeSpan.FromMilliseconds(500.0))
		{
			throw new ArgumentOutOfRangeException(name, "Each direction must be between 0 and 500 milliseconds.");
		}
	}

	private async Task AcceptOneAsync()
	{
		TcpClient client = null;
		TcpClient server = null;
		try
		{
			client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (Interlocked.Exchange(ref _accepted, 1) == 0)
			{
				client.NoDelay = true;
				server = new TcpClient(AddressFamily.InterNetwork)
				{
					NoDelay = true
				};
				await server.ConnectAsync(_destinationAddress, _destinationPort, _stop.Token).ConfigureAwait(continueOnCapturedContext: false);
				Volatile.Write(ref _connectedToServer, 1);
				_sockets.TryAdd(client, 0);
				_sockets.TryAdd(server, 0);
				using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
				Task up = PumpAsync(client.GetStream(), server.GetStream(), _clientToServerDelay, clientToServer: true, linked.Token);
				Task down = PumpAsync(server.GetStream(), client.GetStream(), _serverToClientDelay, clientToServer: false, linked.Token);
				Task completed = await Task.WhenAny(up, down).ConfigureAwait(continueOnCapturedContext: false);
				linked.Cancel();
				await ObservePumpAsync(completed).ConfigureAwait(continueOnCapturedContext: false);
				Close(client);
				Close(server);
				await ObservePumpAsync((completed == up) ? down : up).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException) when (_stop.IsCancellationRequested)
		{
		}
		catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
		{
		}
		catch (SocketException) when (_stop.IsCancellationRequested)
		{
		}
		catch (IOException) when (_stop.IsCancellationRequested)
		{
		}
		catch (SocketException ex5)
		{
			Failure = $"destination_connect_or_socket_error:{ex5.SocketErrorCode}";
		}
		catch (IOException)
		{
			Failure = "stream_io_error";
		}
		finally
		{
			byte value;
			if (client != null)
			{
				_sockets.TryRemove(client, out value);
				Close(client);
			}
			if (server != null)
			{
				_sockets.TryRemove(server, out value);
				Close(server);
			}
		}
	}

	private async Task PumpAsync(NetworkStream source, NetworkStream destination, TimeSpan delay, bool clientToServer, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[16384];
		while (true)
		{
			int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (count == 0)
			{
				break;
			}
			if (delay > TimeSpan.Zero)
			{
				await Task.Delay(delay, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			await destination.FlushAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (clientToServer)
			{
				Interlocked.Add(ref _clientToServerBytes, count);
			}
			else
			{
				Interlocked.Add(ref _serverToClientBytes, count);
			}
		}
	}

	private static async Task ObservePumpAsync(Task task)
	{
		try
		{
			await task.ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
		}
		catch (IOException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private static void Close(TcpClient client)
	{
		try
		{
			client.Dispose();
		}
		catch (SocketException)
		{
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}
		_stoppedAtUtc = DateTimeOffset.UtcNow;
		_stop.Cancel();
		try
		{
			_listener.Stop();
		}
		catch (SocketException)
		{
		}
		foreach (TcpClient key in _sockets.Keys)
		{
			Close(key);
		}
		try
		{
			await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5.0)).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (TimeoutException)
		{
		}
		catch (OperationCanceledException)
		{
		}
		_stop.Dispose();
	}
}
