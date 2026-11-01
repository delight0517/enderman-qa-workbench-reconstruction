using System;
using System.Collections.Generic;

namespace EndermanQa.Desktop.Adapters;

public static class FabricBridgeContract
{
	private static readonly HashSet<string> SupportedHazards = new HashSet<string>(StringComparer.Ordinal)
	{
		"fall-ready", "rain-ready", "fall", "water", "water-pulse", "water-source-pulse", "water-flowing-pulse", "water-shallow-pulse", "water-deep-pulse", "water-source",
		"water-flowing", "water-shallow", "water-deep", "water-clear", "lava", "lava-escape", "fire", "fire-stationary", "fire-lethal-pulse", "water-lethal-pulse",
		"lava-lethal-pulse", "disable-target-ai", "ice-slope", "fire-lava", "fire-clear", "magma", "cactus", "cactus-pulse", "powder-snow", "campfire",
		"soul-campfire", "rain", "rain-shelter", "rain-clear", "sweet-berry", "health-6", "projectile-wall", "vehicle-boat", "splash-radius-pad", "lingering-water-cloud",
		"lingering-water-cloud-long", "move-target-outside-cloud", "water-blocked-teleport-attempt"
	};

	public static bool SupportsHazard(string? hazard)
	{
		if (hazard != null)
		{
			return SupportedHazards.Contains(hazard);
		}
		return false;
	}
}
