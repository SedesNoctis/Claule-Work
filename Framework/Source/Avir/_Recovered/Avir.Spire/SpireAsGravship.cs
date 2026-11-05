using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.Spire;

public static class SpireAsGravship
{
	private static ThingDef cradleDef;

	public static Building_SpireCradle On(Map map)
	{
		if (map == null)
		{
			return null;
		}
		if (cradleDef == null)
		{
			cradleDef = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpireCradle");
		}
		if (cradleDef == null)
		{
			return null;
		}
		List<Thing> list = map.listerThings.ThingsOfDef(cradleDef);
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] is Building_SpireCradle building_SpireCradle && building_SpireCradle.Faction == Faction.OfPlayer)
			{
				return building_SpireCradle;
			}
		}
		return null;
	}
}
