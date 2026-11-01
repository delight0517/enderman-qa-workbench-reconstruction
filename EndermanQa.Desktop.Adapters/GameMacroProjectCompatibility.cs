using System;
using System.Text;
using EndermanQa.Desktop.Scenarios;

namespace EndermanQa.Desktop.Adapters;

public static class GameMacroProjectCompatibility
{
	public static bool IsCompatible(GameToolDescriptor descriptor, string projectGameTarget, string projectEngineId)
	{
		ArgumentNullException.ThrowIfNull(descriptor, "descriptor");
		if (descriptor.Engine.Equals("fixture-only", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		bool flag = string.IsNullOrWhiteSpace(projectGameTarget);
		if (!flag)
		{
			bool flag2 = ((projectEngineId == "unknown" || projectEngineId == "custom") ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return false;
		}
		if (!NormalizeGameId(projectGameTarget).Equals(descriptor.GameId, StringComparison.Ordinal))
		{
			return false;
		}
		string text = descriptor.Engine.Trim().ToLowerInvariant();
		return projectEngineId switch
		{
			"unity" => text.StartsWith("unity", StringComparison.Ordinal), 
			"unreal" => text.Contains("unreal", StringComparison.Ordinal), 
			"godot" => text.StartsWith("godot", StringComparison.Ordinal), 
			"minecraft-java" => text.Contains("minecraft", StringComparison.Ordinal) || text.Contains("fabric", StringComparison.Ordinal), 
			_ => false, 
		};
	}

	public static string NormalizeGameId(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value, "value");
		if (value.Contains("Minecraft Java", StringComparison.OrdinalIgnoreCase))
		{
			return "minecraft-java";
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		bool flag = false;
		string text = value.Trim().ToLowerInvariant();
		foreach (char c in text)
		{
			if (char.IsLetterOrDigit(c))
			{
				if (flag && stringBuilder.Length > 0)
				{
					stringBuilder.Append('-');
				}
				stringBuilder.Append(c);
				flag = false;
			}
			else
			{
				flag = true;
			}
		}
		return stringBuilder.ToString();
	}
}
