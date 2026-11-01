using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EndermanQa.Desktop.Scenarios;

public static class GameMacroValidation
{
	public const int ContractVersion = 2;

	public static readonly IReadOnlySet<string> SupportedOperators = new HashSet<string>(StringComparer.Ordinal) { "equals", "not-equals", "greater-than", "less-than" };

	public static IReadOnlyList<string> Validate(GameAutomationMacro macro, GameToolDescriptor descriptor)
	{
		List<string> list = new List<string>();
		if ((object)macro == null)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<string>("Game macro is missing.");
		}
		if ((object)descriptor == null)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<string>("Game adapter descriptor is missing.");
		}
		if ((object)macro.Target == null)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<string>("Game target is missing.");
		}
		if (macro.Steps == null || macro.Assertions == null)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<string>("Macro steps or assertions are missing.");
		}
		if (descriptor.Actions == null || descriptor.ObservationKeys == null)
		{
			return new _003C_003Ez__ReadOnlySingleElementList<string>("Adapter capabilities are missing.");
		}
		if (macro.ContractVersion != 2)
		{
			list.Add("Unsupported game macro contract version.");
		}
		if (!Guid.TryParseExact(macro.Id, "N", out var _))
		{
			list.Add("Macro ID is invalid.");
		}
		if (string.IsNullOrWhiteSpace(macro.Name) || macro.Name.Length > 80 || macro.Name.Any(char.IsControl))
		{
			list.Add("Macro name is invalid.");
		}
		string ownership = macro.Target.Ownership;
		if ((!(ownership == "owned") && !(ownership == "explicit-disposable")) || 1 == 0)
		{
			list.Add("Target must be verified as owned or disposable.");
		}
		if (string.IsNullOrWhiteSpace(macro.Target.GameId) || string.IsNullOrWhiteSpace(macro.Target.TargetId))
		{
			list.Add("Target identity is incomplete.");
		}
		if (!string.Equals(macro.Target.GameId, descriptor.GameId, StringComparison.Ordinal) || !string.Equals(macro.AdapterId, descriptor.AdapterId, StringComparison.Ordinal))
		{
			list.Add("Macro game/adapter identity does not match the selected tool.");
		}
		if (macro.AdapterContractVersion != descriptor.ContractVersion || !string.Equals(macro.CapabilitySha256, descriptor.CapabilitySha256, StringComparison.OrdinalIgnoreCase))
		{
			list.Add("Adapter contract or capability set changed after the macro was authored.");
		}
		if (descriptor.Availability != "ready")
		{
			list.Add("Selected game adapter is not ready: " + descriptor.Availability);
		}
		int count = macro.Steps.Count;
		if ((count < 1 || count > 100) ? true : false)
		{
			list.Add("A game macro must contain 1–100 steps.");
		}
		count = macro.Assertions.Count;
		if ((count < 1 || count > 100) ? true : false)
		{
			list.Add("A game macro must contain 1–100 assertions.");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.Ordinal);
		foreach (GameMacroStep step in macro.Steps)
		{
			if ((object)step == null)
			{
				list.Add("Macro contains a missing step.");
				continue;
			}
			if (string.IsNullOrWhiteSpace(step.Id) || step.Id.Length > 80 || step.Id.Any(char.IsControl))
			{
				list.Add("Step ID is invalid.");
			}
			else if (!hashSet.Add(step.Id))
			{
				list.Add("Step IDs must be unique.");
			}
			if (!descriptor.Actions.Contains(step.Action))
			{
				list.Add("Adapter does not allow action '" + step.Action + "'.");
			}
			if (step.Arguments == null || step.Arguments.Count > 32 || step.Arguments.Any<KeyValuePair<string, string>>((KeyValuePair<string, string> pair) => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 80 || pair.Value == null || pair.Value.Length > 512))
			{
				list.Add("Step '" + step.Id + "' has invalid arguments.");
			}
		}
		foreach (GameMacroAssertion assertion in macro.Assertions)
		{
			if ((object)assertion == null)
			{
				list.Add("Macro contains a missing assertion.");
				continue;
			}
			if (string.IsNullOrWhiteSpace(assertion.Id) || assertion.Id.Length > 80 || assertion.Id.Any(char.IsControl))
			{
				list.Add("Assertion ID is invalid.");
			}
			else if (!hashSet2.Add(assertion.Id))
			{
				list.Add("Assertion IDs must be unique.");
			}
			if (!hashSet.Contains(assertion.AfterStepId))
			{
				list.Add("Assertion '" + assertion.Id + "' refers to a missing step.");
			}
			if (!descriptor.ObservationKeys.Contains(assertion.ObservationKey))
			{
				list.Add("Adapter does not provide observation '" + assertion.ObservationKey + "'.");
			}
			if (!SupportedOperators.Contains(assertion.Operator))
			{
				list.Add("Assertion '" + assertion.Id + "' uses an unsupported operator.");
			}
			if (assertion.ExpectedValue == null || assertion.ExpectedValue.Length > 256)
			{
				list.Add("Assertion '" + assertion.Id + "' expected value is invalid.");
			}
		}
		return list.Distinct<string>(StringComparer.Ordinal).ToArray();
	}

	internal static string ComputeCapabilityHash(GameToolDescriptor descriptor)
	{
		string s = JsonSerializer.Serialize(new
		{
			AdapterId = descriptor.AdapterId,
			ContractVersion = descriptor.ContractVersion,
			GameId = descriptor.GameId,
			Engine = descriptor.Engine,
			RequiresOwnedProcess = descriptor.RequiresOwnedProcess,
			RequiresUnmirroredDisplay = descriptor.RequiresUnmirroredDisplay,
			Actions = descriptor.Actions.Order<string>(StringComparer.Ordinal).ToArray(),
			ObservationKeys = descriptor.ObservationKeys.Order<string>(StringComparer.Ordinal).ToArray()
		});
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
	}
}
