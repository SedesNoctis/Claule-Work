using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Spire;

[HarmonyPatch(typeof(GravshipUtility), "GetConnectedSubstructure")]
public static class Patch_SpireDomainIsSubstructure
{
	public static bool Prefix(Building_GravEngine engine, HashSet<IntVec3> cells, int maxCells)
	{
		if (!(engine is Building_SpireCradle building_SpireCradle))
		{
			return true;
		}
		building_SpireCradle.Domain(cells, maxCells);
		return false;
	}
}
