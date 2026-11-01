using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Adapters;

internal static class ManagedFixtureCleanup
{
	internal static async Task ReleaseAllAsync(IEnumerable<string> entityUuids, Func<string, Task<(string Action, string EntityUuid, bool? FixtureReleased)>> release)
	{
		List<string> failures = new List<string>();
		foreach (string uuid in entityUuids.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				(string, string, bool?) tuple = await release(uuid).ConfigureAwait(continueOnCapturedContext: false);
				if (tuple.Item1 != "release_test_fixture" || !string.Equals(tuple.Item2, uuid, StringComparison.OrdinalIgnoreCase) || !tuple.Item3.GetValueOrDefault())
				{
					failures.Add(uuid + ": release receipt identity/state was not confirmed");
				}
			}
			catch (Exception ex)
			{
				failures.Add($"{uuid}: {ex.GetType().Name}: {ex.Message}");
			}
		}
		if (failures.Count != 0)
		{
			throw new InvalidOperationException("One or more Minecraft macro fixtures could not be confirmed released; do not repeat this macro until the world is reconciled. " + string.Join(" | ", failures));
		}
	}
}
