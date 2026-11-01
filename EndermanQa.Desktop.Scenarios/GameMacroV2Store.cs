using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndermanQa.Desktop.Scenarios;

public sealed class GameMacroV2Store
{
	private sealed record HashPayload(int ContractVersion, string Id, string Name, DateTimeOffset CreatedAt, GameAutomationTarget Target, string AdapterId, int AdapterContractVersion, string CapabilitySha256, IReadOnlyList<GameMacroStep> Steps, IReadOnlyList<GameMacroAssertion> Assertions);

	private const long MaximumMacroFileBytes = 1048576L;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	private static readonly JsonSerializerOptions CanonicalOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
	{
		WriteIndented = false
	};

	

	private string MacroRoot => Path.Combine(_projectDataRoot, "macros-v2");

	private readonly string _projectDataRoot;

	public GameMacroV2Store(string projectDataRoot)
	{
		_projectDataRoot = Path.GetFullPath(projectDataRoot);
	}

	public GameAutomationMacro Save(string name, GameAutomationTarget target, GameToolDescriptor descriptor, IEnumerable<GameMacroStep> steps, IEnumerable<GameMacroAssertion> assertions)
	{
		ArgumentNullException.ThrowIfNull(target, "target");
		ArgumentNullException.ThrowIfNull(descriptor, "descriptor");
		string name2 = (name ?? string.Empty).Trim();
		GameAutomationMacro gameAutomationMacro = new GameAutomationMacro(2, Guid.NewGuid().ToString("N"), name2, DateTimeOffset.UtcNow, target, descriptor.AdapterId, descriptor.ContractVersion, descriptor.CapabilitySha256, steps.ToArray(), assertions.ToArray(), string.Empty);
		IReadOnlyList<string> readOnlyList = GameMacroValidation.Validate(gameAutomationMacro, descriptor);
		if (readOnlyList.Count != 0)
		{
			throw new InvalidDataException(string.Join("; ", readOnlyList));
		}
		gameAutomationMacro = gameAutomationMacro with
		{
			ContentSha256 = ComputeHash(gameAutomationMacro)
		};
		RejectReparseAncestors(_projectDataRoot);
		Directory.CreateDirectory(MacroRoot);
		RejectReparseAncestors(MacroRoot);
		WriteNew(PathFor(gameAutomationMacro.Id), gameAutomationMacro);
		return gameAutomationMacro;
	}

	public GameAutomationMacro Load(string id, GameAutomationTarget expectedTarget, GameToolDescriptor descriptor)
	{
		GameAutomationMacro gameAutomationMacro = ReadAndValidateFile(id, descriptor);
		if (gameAutomationMacro.Target != expectedTarget)
		{
			throw new InvalidDataException("Game macro belongs to another game target or version.");
		}
		return gameAutomationMacro;
	}

	public GameAutomationMacro LoadForAdapter(string id, GameToolDescriptor descriptor)
	{
		return ReadAndValidateFile(id, descriptor);
	}

	private GameAutomationMacro ReadAndValidateFile(string id, GameToolDescriptor descriptor)
	{
		if (!Guid.TryParseExact(id, "N", out var _))
		{
			throw new InvalidDataException("Game macro ID is invalid.");
		}
		RejectReparseAncestors(MacroRoot);
		string text = PathFor(id);
		RejectReparse(text);
		if (new FileInfo(text).Length > 1048576)
		{
			throw new InvalidDataException("Game macro file exceeds the 1 MiB safety limit.");
		}
		GameAutomationMacro gameAutomationMacro = JsonSerializer.Deserialize<GameAutomationMacro>(File.ReadAllText(text), JsonOptions) ?? throw new InvalidDataException("Game macro file is empty or malformed.");
		if (!string.Equals(gameAutomationMacro.ContentSha256, ComputeHash(gameAutomationMacro), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Game macro content hash does not match; refusing to run a modified file.");
		}
		IReadOnlyList<string> readOnlyList = GameMacroValidation.Validate(gameAutomationMacro, descriptor);
		if (readOnlyList.Count != 0)
		{
			throw new InvalidDataException(string.Join("; ", readOnlyList));
		}
		return gameAutomationMacro;
	}

	public IReadOnlyList<string> ListIds()
	{
		if (!Directory.Exists(MacroRoot))
		{
			return Array.Empty<string>();
		}
		RejectReparseAncestors(MacroRoot);
		return Directory.EnumerateFiles(MacroRoot, "*.json", SearchOption.TopDirectoryOnly).Select(delegate(string path)
		{
			RejectReparse(path);
			return Path.GetFileNameWithoutExtension(path);
		}).Order<string>(StringComparer.Ordinal)
			.ToArray();
	}

	private string PathFor(string id)
	{
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(MacroRoot).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string value = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		string fullPath = Path.GetFullPath(Path.Combine(MacroRoot, id + ".json"));
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new UnauthorizedAccessException("Game macro path escapes this project.");
		}
		return fullPath;
	}

	private static string ComputeHash(GameAutomationMacro macro)
	{
		HashPayload value = new HashPayload(macro.ContractVersion, macro.Id, macro.Name, macro.CreatedAt, macro.Target, macro.AdapterId, macro.AdapterContractVersion, macro.CapabilitySha256, macro.Steps, macro.Assertions);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, CanonicalOptions))));
	}

	private static void WriteNew(string path, GameAutomationMacro macro)
	{
		string text = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				JsonSerializer.Serialize(fileStream, macro, JsonOptions);
				fileStream.Flush(flushToDisk: true);
			}
			File.Move(text, path, overwrite: false);
		}
		catch
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
			throw;
		}
	}

	private static void RejectReparse(string path)
	{
		if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new UnauthorizedAccessException("Game macro storage cannot be a symbolic link/reparse point.");
		}
	}

	private static void RejectReparseAncestors(string path)
	{
		string text = Path.GetFullPath(path);
		while (!string.IsNullOrEmpty(text))
		{
			RejectReparse(text);
			string directoryName = Path.GetDirectoryName(text);
			if (!string.IsNullOrEmpty(directoryName) && !string.Equals(directoryName, text, StringComparison.OrdinalIgnoreCase))
			{
				text = directoryName;
				continue;
			}
			break;
		}
	}
}
