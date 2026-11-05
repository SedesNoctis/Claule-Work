using System.Collections.Generic;
using Verse;

namespace Avir;

[StaticConstructorOnStartup]
public static class AvirCompDedupe
{
	static AvirCompDedupe()
	{
		foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (item.race != null && item.comps != null && item.defName.StartsWith("Avir"))
			{
				HashSet<string> seen = new HashSet<string>();
				item.comps.RemoveAll((CompProperties c) => c?.compClass?.FullName == "CombatExtended.CompPawnGizmo" && !seen.Add(c.compClass.FullName));
			}
		}
	}
}
