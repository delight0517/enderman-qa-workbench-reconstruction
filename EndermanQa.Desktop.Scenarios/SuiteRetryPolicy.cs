using System;
using System.Linq;

namespace EndermanQa.Desktop.Scenarios;

public static class SuiteRetryPolicy
{
	public static bool IsInvalidSetup(string? limitation)
	{
		string limitation2 = limitation;
		return new string[4] { "precondition failed", "allowlist", "setup-inconclusive", "fixture contamination" }.Any((string marker) => limitation2?.Contains(marker, StringComparison.OrdinalIgnoreCase) ?? false);
	}
}
