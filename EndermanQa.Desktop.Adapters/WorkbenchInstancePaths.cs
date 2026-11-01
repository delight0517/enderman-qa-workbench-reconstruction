using System;
using System.IO;
using System.Linq;

namespace EndermanQa.Desktop.Adapters;

internal static class WorkbenchInstancePaths
{
	public static string ValidateInstanceId(string instanceId)
	{
		if (string.IsNullOrWhiteSpace(instanceId) || instanceId.Length != 32 || instanceId.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidOperationException("Workbench instance ID must be a 32-character hexadecimal value.");
		return instanceId.ToLowerInvariant();
	}

	public static string ResolveInstanceRoot(string instanceId, string workspaceRoot, string instancesRoot)
	{
		string normalizedId = ValidateInstanceId(instanceId);
		string normalizedInstancesRoot = Path.GetFullPath(instancesRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (normalizedInstancesRoot.Length == 0)
			throw new InvalidOperationException("Workbench instances root is invalid.");
		if (Directory.Exists(normalizedInstancesRoot) && (File.GetAttributes(normalizedInstancesRoot) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidOperationException("Workbench instances root is a reparse point.");

		string expectedRoot = Path.GetFullPath(Path.Combine(normalizedInstancesRoot, normalizedId));
		string actualRoot = Path.GetFullPath(workspaceRoot);
		string rootPrefix = normalizedInstancesRoot + Path.DirectorySeparatorChar;
		if (!expectedRoot.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Resolved instance path escaped the app-owned instances root.");
		if (!actualRoot.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Workspace root does not match the exact app-owned instance ID.");
		if (Directory.Exists(actualRoot) && (File.GetAttributes(actualRoot) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidOperationException("Workbench instance root is a reparse point.");
		return expectedRoot;
	}

	public static bool IsOwnedClientSession(string instanceRoot, string sessionRoot)
	{
		string expectedParent = Path.GetFullPath(Path.Combine(instanceRoot, "client-runs"));
		string actualSession = Path.GetFullPath(sessionRoot);
		return string.Equals(Path.GetDirectoryName(actualSession), expectedParent, StringComparison.OrdinalIgnoreCase)
			&& Guid.TryParse(Path.GetFileName(actualSession), out _);
	}
}
