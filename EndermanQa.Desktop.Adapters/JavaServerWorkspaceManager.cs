using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndermanQa.Desktop.Adapters;

public sealed class JavaServerWorkspaceManager
{
	private readonly string _instancesRoot;

	private readonly string _modJarPath;

	public JavaServerWorkspaceManager(string dataRoot, string modJarPath)
	{
		_instancesRoot = Path.GetFullPath(Path.Combine(dataRoot, "instances"));
		_modJarPath = Path.GetFullPath(modJarPath);
	}

	public JavaServerWorkspace PrepareFresh(string instanceId)
	{
		if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length != 32 || instanceId.Any((char c) => !Uri.IsHexDigit(c)))
		{
			throw new ArgumentException("Instance ID must be a generated 32-character hexadecimal identifier.", "instanceId");
		}
		RejectReparsePoint(_instancesRoot);
		RejectReparsePoint(_modJarPath);
		if (!File.Exists(_modJarPath))
		{
			throw new FileNotFoundException("The staged Enderman QA Fabric bridge JAR is missing.", _modJarPath);
		}
		Directory.CreateDirectory(_instancesRoot);
		EnsureSafeInstancesRoot();
		string fullPath = Path.GetFullPath(Path.Combine(_instancesRoot, instanceId.ToLowerInvariant()));
		ReadOnlySpan<char> readOnlySpan = _instancesRoot;
		char reference = Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase))
		{
			throw new SecurityException("Resolved instance path escaped the app-owned instances directory.");
		}
		if (Directory.Exists(fullPath) || File.Exists(fullPath))
		{
			throw new IOException("Refusing to reuse an existing server instance directory.");
		}
		RejectReparsePoint(fullPath);
		Directory.CreateDirectory(fullPath);
		RejectReparsePoint(fullPath);
		try
		{
			string modsPath = Path.Combine(fullPath, "mods");
			RejectReparsePoint(modsPath);
			string fullName = Directory.CreateDirectory(modsPath).FullName;
			RejectReparsePoint(fullName);
			string bridgePath = Path.Combine(fullName, Path.GetFileName(_modJarPath));
			RejectReparsePoint(bridgePath);
			File.Copy(_modJarPath, bridgePath, overwrite: false);
			string eulaPath = Path.Combine(fullPath, "eula.txt");
			RejectReparsePoint(eulaPath);
			File.WriteAllText(eulaPath, "eula=false\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			string baselinePath = Path.Combine(fullPath, "endermanqa-mob-spawning.baseline");
			RejectReparsePoint(baselinePath);
			File.WriteAllText(baselinePath, "true\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			int num = ReserveCandidateLoopbackPort();
			string propertiesPath = Path.Combine(fullPath, "server.properties");
			RejectReparsePoint(propertiesPath);
			File.WriteAllText(propertiesPath, $"level-name=qa-world\r\nlevel-seed=0\r\nonline-mode=false\r\nserver-ip=127.0.0.1\r\nserver-port={num}\r\npause-when-empty-seconds=0\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			string worldPath = Path.Combine(fullPath, "qa-world");
			RejectReparsePoint(worldPath);
			return new JavaServerWorkspace(instanceId.ToLowerInvariant(), fullPath, eulaPath, fullName, worldPath, bridgePath, num);
		}
		catch
		{
			try
			{
				RejectReparsePoint(fullPath);
				Directory.Delete(fullPath, recursive: true);
			}
			catch
			{
			}
			throw;
		}
	}

	public JavaServerWorkspace OpenExisting(string instanceId)
	{
		if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length != 32 || instanceId.Any((char c) => !Uri.IsHexDigit(c)))
		{
			throw new ArgumentException("Instance ID must be a 32-character hexadecimal identifier.", "instanceId");
		}
		string text = instanceId.ToLowerInvariant();
		EnsureSafeInstancesRoot();
		string fullPath = Path.GetFullPath(Path.Combine(_instancesRoot, text));
		ReadOnlySpan<char> readOnlySpan = _instancesRoot;
		char reference = Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullPath))
		{
			throw new SecurityException("Existing server is not an app-owned disposable instance.");
		}
		string text2 = Path.Combine(fullPath, "eula.txt");
		string path = Path.Combine(fullPath, "server.properties");
		string text3 = Path.Combine(fullPath, "mods");
		string text4 = Path.Combine(fullPath, "qa-world");
		string path2 = Path.Combine(fullPath, "fabric-server-launch.jar");
		string text5 = Path.Combine(text3, Path.GetFileName(_modJarPath));
		foreach (string pathToValidate in new[] { fullPath, text2, path, text3, text4, path2, text5 })
		{
			RejectReparsePoint(pathToValidate);
		}
		if (!File.Exists(text2) || !File.ReadAllText(text2).Contains("eula=true", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Existing disposable instance has no accepted per-instance EULA record.");
		}
		if (!File.Exists(path) || !File.Exists(path2) || !Directory.Exists(text3) || !Directory.Exists(text4) || !File.Exists(text5))
		{
			throw new InvalidDataException("Existing instance is missing its QA world or Fabric runtime files.");
		}
		Dictionary<string, string> dictionary = (from line in File.ReadAllLines(path)
			where line.Length > 0 && !line.StartsWith('#')
			select line.Split('=', 2) into parts
			where parts.Length == 2
			select parts).ToDictionary<string[], string, string>((string[] parts) => parts[0], (string[] parts) => parts[1], StringComparer.OrdinalIgnoreCase);
		string value;
		string value2;
		string value3;
		string value4;
		int result = default(int);
		bool flag = !dictionary.TryGetValue("level-name", out value) || value != "qa-world" || !dictionary.TryGetValue("online-mode", out value2) || value2 != "false" || !dictionary.TryGetValue("server-ip", out value3) || value3 != "127.0.0.1" || !dictionary.TryGetValue("server-port", out value4) || !int.TryParse(value4, out result);
		if (!flag)
		{
			bool flag2 = ((result < 1024 || result > 65535) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new SecurityException("Existing instance settings are not the expected private loopback QA configuration.");
		}
		return new JavaServerWorkspace(text, fullPath, text2, text3, text4, text5, result);
	}

	public const string PreparedBridgeJarSha256 = "21826BFB04B57D21BFFDC9F154C70D37B5FEA88D64CFAF1B38A3F330B5590CAE";

	public JavaServerWorkspace ResumePrepared(string instanceId)
	{
		return ResumeUnstarted(instanceId, eulaAccepted: false);
	}

	public JavaServerWorkspace ResumeAcceptedUnstarted(string instanceId)
	{
		return ResumeUnstarted(instanceId, eulaAccepted: true);
	}

	private JavaServerWorkspace ResumeUnstarted(string instanceId, bool eulaAccepted)
	{
		string id = NormalizeInstanceId(instanceId);
		string root = ResolveInstanceRoot(id);
		if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The app-owned prepared instance does not exist.");
		string eula = Path.Combine(root, "eula.txt");
		string properties = Path.Combine(root, "server.properties");
		string mods = Path.Combine(root, "mods");
		string world = Path.Combine(root, "qa-world");
		string loader = Path.Combine(root, "fabric-server-launch.jar");
		string api = Path.Combine(mods, "fabric-api-0.165.0+26.3.jar");
		string bridge = Path.Combine(mods, Path.GetFileName(_modJarPath));
		foreach (string pathToValidate in new[] { root, eula, properties, mods, world, loader, api, bridge, Path.Combine(root, "endermanqa-mob-spawning.baseline") })
		{
			RejectReparsePoint(pathToValidate);
		}
		if (!File.Exists(eula) || !ReadEulaState(eula, eulaAccepted)) throw new InvalidOperationException("Prepared instance EULA state does not match the requested resume path.");
		if (Directory.Exists(world) || File.Exists(world)) throw new InvalidOperationException("Prepared instance already has a world; use the existing-world route.");
		if (!File.Exists(properties) || !Directory.Exists(mods) || !File.Exists(loader) || !File.Exists(api) || !File.Exists(bridge) || !File.Exists(Path.Combine(root, "endermanqa-mob-spawning.baseline")))
			throw new InvalidDataException("Prepared instance is missing required pinned server, mod, or baseline files.");
		var config = ReadServerProperties(properties);
		if (!config.TryGetValue("level-name", out string? level) || level != "qa-world"
			|| !config.TryGetValue("level-seed", out string? seed) || seed != "0"
			|| !config.TryGetValue("online-mode", out string? online) || online != "false"
			|| !config.TryGetValue("server-ip", out string? bind) || bind != "127.0.0.1"
			|| !config.TryGetValue("server-port", out string? portText) || !int.TryParse(portText, out int port) || port < 1024 || port > 65535
			|| !config.TryGetValue("pause-when-empty-seconds", out string? pause) || pause != "0")
			throw new SecurityException("Prepared instance does not match the private 26.3 loopback QA configuration.");
		if (!HashMatches(loader, FabricServerInstaller.LoaderSha256)
			|| !HashMatches(api, FabricServerInstaller.ApiSha256)
			|| !HashMatches(bridge, PreparedBridgeJarSha256))
			throw new InvalidDataException("Prepared instance artifact hash validation failed.");
		if (File.Exists(_modJarPath) && !HashMatches(_modJarPath, PreparedBridgeJarSha256))
			throw new InvalidDataException("The staged bridge artifact does not match this prepared instance version.");
		if (!File.ReadAllText(Path.Combine(root, "endermanqa-mob-spawning.baseline")).Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Prepared instance has no verified mob-spawning baseline.");
		EnsureLoopbackPortAvailable(port);
		return new JavaServerWorkspace(id, root, eula, mods, world, bridge, port);
	}

	private static void EnsureLoopbackPortAvailable(int port)
	{
		try
		{
			using TcpListener listener = new TcpListener(IPAddress.Loopback, port);
			listener.Start();
		}
		catch (SocketException ex)
		{
			throw new InvalidOperationException("The prepared target's loopback port is already active; refusing to resume a running server.", ex);
		}
	}

	public JavaServerWorkspace OpenExistingOrResumePrepared(string instanceId, out bool resumedPrepared)
	{
		string id = NormalizeInstanceId(instanceId);
		string root = ResolveInstanceRoot(id);
		string eula = Path.Combine(root, "eula.txt");
		RejectReparsePoint(eula);
		RejectReparsePoint(Path.Combine(root, "qa-world"));
		if (File.Exists(eula) && !Directory.Exists(Path.Combine(root, "qa-world")) && !File.Exists(Path.Combine(root, "qa-world")))
		{
			if (ReadEulaFalse(eula))
			{
				resumedPrepared = true;
				return ResumePrepared(id);
			}
			if (ReadEulaTrue(eula))
			{
				resumedPrepared = true;
				return ResumeAcceptedUnstarted(id);
			}
		}
		resumedPrepared = false;
		return OpenExisting(id);
	}

	public IReadOnlyList<JavaServerWorkspace> FindResumableInstances()
	{
		if (!Directory.Exists(_instancesRoot)) return Array.Empty<JavaServerWorkspace>();
		EnsureSafeInstancesRoot();
		var found = new List<JavaServerWorkspace>();
		foreach (string directory in Directory.EnumerateDirectories(_instancesRoot))
		{
			string id = Path.GetFileName(directory);
			if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c))) continue;
			try
			{
				RejectReparsePoint(directory);
				string eula = Path.Combine(directory, "eula.txt");
				RejectReparsePoint(eula);
				RejectReparsePoint(Path.Combine(directory, "qa-world"));
				if (ReadEulaFalse(eula)) found.Add(ResumePrepared(id));
				else if (ReadEulaTrue(eula) && !Directory.Exists(Path.Combine(directory, "qa-world"))) found.Add(ResumeAcceptedUnstarted(id));
			}
			catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is SecurityException || ex is ArgumentException) { }
		}
		return found.OrderBy(instance => instance.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private string ResolveInstanceRoot(string id)
	{
		EnsureSafeInstancesRoot();
		string root = Path.GetFullPath(Path.Combine(_instancesRoot, id));
		if (!root.StartsWith(_instancesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			throw new SecurityException("Resolved instance path escaped the app-owned instances directory.");
		return root;
	}

	private void EnsureSafeInstancesRoot()
	{
		RejectReparsePoint(_instancesRoot);
	}

	private static string NormalizeInstanceId(string instanceId)
	{
		if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length != 32 || instanceId.Any(c => !Uri.IsHexDigit(c)))
			throw new ArgumentException("Instance ID must be a generated 32-character hexadecimal identifier.", nameof(instanceId));
		return instanceId.ToLowerInvariant();
	}

	private static void RejectReparsePoint(string path)
	{
		string current = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(current))
		{
			try
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new SecurityException($"Prepared instance path contains a reparse point at '{current}'; refusing to follow it.");
			}
			catch (FileNotFoundException)
			{
			}
			catch (DirectoryNotFoundException)
			{
			}
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
			current = parent;
		}
	}

	private static bool ReadEulaFalse(string path)
	{
		return ReadEulaState(path, accepted: false);
	}

	private static bool ReadEulaTrue(string path) => ReadEulaState(path, accepted: true);

	private static bool ReadEulaState(string path, bool accepted)
	{
		string[] entries = File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
		string expected = accepted ? "eula=true" : "eula=false";
		return entries.Length == 1 && entries[0].Equals(expected, StringComparison.OrdinalIgnoreCase);
	}

	private static Dictionary<string, string> ReadServerProperties(string path)
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in File.ReadAllLines(path))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
			int separator = trimmed.IndexOf('=');
			if (separator <= 0 || !values.TryAdd(trimmed[..separator].Trim(), trimmed[(separator + 1)..].Trim()))
				throw new InvalidDataException("Prepared instance has malformed or duplicate server properties.");
		}
		return values;
	}

	private static bool HashMatches(string path, string expected) =>
		Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(expected, StringComparison.OrdinalIgnoreCase);
	public JavaServerWorkspace UpdateBridgeJarInStoppedInstance(JavaServerWorkspace workspace)
	{
		var (path, path2) = ValidateWorkspaceForInstall(workspace, _instancesRoot);
		foreach (string pathToValidate in new[] { path, path2, Path.Combine(path, "server.properties"), Path.Combine(path, "qa-world"), Path.Combine(path, "fabric-server-launch.jar"), Path.Combine(path, "mods"), workspace.ModJarPath, Path.Combine(path, "endermanqa-mob-spawning.baseline") })
		{
			RejectReparsePoint(pathToValidate);
		}
		if (!Path.GetFullPath(workspace.RootPath).Equals(Path.GetFullPath(Path.Combine(_instancesRoot, workspace.InstanceId)), StringComparison.OrdinalIgnoreCase) || !File.ReadAllText(path2).Contains("eula=true", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(workspace.WorldPath))
		{
			throw new SecurityException("Bridge update target is not the selected EULA-accepted disposable world.");
		}
		try
		{
			using TcpListener tcpListener = new TcpListener(IPAddress.Loopback, workspace.ServerPort);
			tcpListener.Start();
		}
		catch (SocketException innerException)
		{
			throw new InvalidOperationException("The selected server port is active; refusing to update its adapter.", innerException);
		}
		string path3 = Path.Combine(path, "endermanqa-bridge.json");
		RejectReparsePoint(path3);
		if (File.Exists(path3))
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllBytes(path3));
			JsonElement value;
			JsonElement value2;
			int value3 = default(int);
			bool flag = !jsonDocument.RootElement.TryGetProperty("host", out value) || value.GetString() != "127.0.0.1" || !jsonDocument.RootElement.TryGetProperty("port", out value2) || !value2.TryGetInt32(out value3);
			if (!flag)
			{
				bool flag2 = ((value3 < 1024 || value3 > 65535) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				throw new InvalidDataException("Existing bridge descriptor is malformed; refusing to replace an unknown adapter session.");
			}
			try
			{
				using TcpListener tcpListener2 = new TcpListener(IPAddress.Loopback, value3);
				tcpListener2.Start();
			}
			catch (SocketException innerException2)
			{
				throw new InvalidOperationException("A bridge descriptor is still listening; refusing to replace the adapter in a possibly active instance.", innerException2);
			}
			RejectReparsePoint(path3);
			File.Delete(path3);
		}
		if (!File.Exists(_modJarPath))
		{
			throw new FileNotFoundException("Staged bridge JAR is missing.", _modJarPath);
		}
		if (!File.Exists(Path.Combine(path, "endermanqa-mob-spawning.baseline")))
		{
			throw new InvalidDataException("This existing world has no verified natural-mob-spawning baseline; refusing to launch until its owner establishes one.");
		}
		string path4 = Path.Combine(path, "server.properties");
		RejectReparsePoint(path4);
		string[] array = File.ReadAllLines(path4);
		int num = Array.FindIndex(array, (string line) => line.StartsWith("pause-when-empty-seconds=", StringComparison.OrdinalIgnoreCase));
		string previousPauseWhenEmptySeconds = ((num >= 0) ? array[num].Split('=', 2)[1] : "60");
		if (num >= 0)
		{
			array[num] = "pause-when-empty-seconds=0";
		}
		else
		{
			string[] array2 = array;
			int num2 = 0;
			string[] array3 = new string[1 + array2.Length];
			ReadOnlySpan<string> readOnlySpan = new ReadOnlySpan<string>(array2);
			readOnlySpan.CopyTo(new Span<string>(array3).Slice(num2, readOnlySpan.Length));
			num2 += readOnlySpan.Length;
			array3[num2] = "pause-when-empty-seconds=0";
			num2++;
			array = array3;
		}
		RejectReparsePoint(path4);
		File.WriteAllLines(path4, array, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		string fullPath = Path.GetFullPath(workspace.ModJarPath);
		RejectReparsePoint(fullPath);
		ReadOnlySpan<char> readOnlySpan2 = Path.Combine(path, "mods");
		char reference = Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(string.Concat(readOnlySpan2, new ReadOnlySpan<char>(ref reference)), StringComparison.OrdinalIgnoreCase))
		{
			throw new SecurityException("Bridge JAR destination escaped the private instance mods directory.");
		}
		string text = fullPath + ".candidate.tmp";
		RejectReparsePoint(text);
		File.Copy(_modJarPath, text, overwrite: true);
		RejectReparsePoint(fullPath);
		File.Move(text, fullPath, overwrite: true);
		return workspace with
		{
			PreviousPauseWhenEmptySeconds = previousPauseWhenEmptySeconds
		};
	}

	private static int ReserveCandidateLoopbackPort()
	{
		using TcpListener tcpListener = new TcpListener(IPAddress.Loopback, 0);
		tcpListener.Start();
		return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
	}

	public static string BuildAcknowledgedServerArguments(JavaServerWorkspace workspace, bool userAcceptedMinecraftEula, string instancesRoot)
	{
		ArgumentNullException.ThrowIfNull(workspace, "workspace");
		if (!userAcceptedMinecraftEula)
		{
			throw new InvalidOperationException("Starting the Minecraft server requires the user to explicitly accept the Minecraft EULA.");
		}
		if (!File.ReadAllText(ValidateWorkspaceForInstall(workspace, instancesRoot).Eula).Contains("eula=true", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Minecraft EULA has not been explicitly accepted for this server instance.");
		}
		return "-Dendermanqa.bridge.enabled=true -jar fabric-server-launch.jar nogui";
	}

	public static void AcceptMinecraftEula(JavaServerWorkspace workspace, bool userExplicitlyAccepted, string instancesRoot)
	{
		if (!userExplicitlyAccepted)
		{
			throw new InvalidOperationException("Minecraft EULA acceptance was not confirmed.");
		}
		string item = ValidateWorkspaceForInstall(workspace, instancesRoot).Eula;
		if (!File.ReadAllText(item).Contains("eula=false", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("EULA gate is not in its expected unaccepted state.");
		}
		RejectWorkspacePathReparse(item);
		File.WriteAllText(item, "eula=true\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	internal static (string Root, string Eula) ValidateWorkspaceForInstall(JavaServerWorkspace workspace, string instancesRoot)
	{
		string fullPath = Path.GetFullPath(workspace.RootPath);
		string fullPath2 = Path.GetFullPath(workspace.EulaPath);
		string expectedRoot = WorkbenchInstancePaths.ResolveInstanceRoot(workspace.InstanceId, fullPath, instancesRoot);
		if (!fullPath.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(fullPath), "N", out var _))
		{
			throw new SecurityException("Server workspace is not a generated app-owned instance directory.");
		}
		if (!fullPath2.Equals(Path.Combine(fullPath, "eula.txt"), StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath2))
		{
			throw new SecurityException("EULA file is outside the selected private server instance.");
		}
		foreach (string pathToValidate in new[] { fullPath, fullPath2, Path.Combine(fullPath, "server.properties"), Path.Combine(fullPath, "mods"), Path.Combine(fullPath, "fabric-server-launch.jar"), Path.Combine(fullPath, "qa-world") })
		{
			RejectWorkspacePathReparse(pathToValidate);
		}
		return (Root: fullPath, Eula: fullPath2);
	}

	private static void RejectWorkspacePathReparse(string path)
	{
		string current = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(current))
		{
			try
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new SecurityException($"Server workspace path contains a reparse point at '{current}'.");
			}
			catch (FileNotFoundException) { }
			catch (DirectoryNotFoundException) { }
			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || parent.Equals(current, StringComparison.OrdinalIgnoreCase)) break;
			current = parent;
		}
	}
}
