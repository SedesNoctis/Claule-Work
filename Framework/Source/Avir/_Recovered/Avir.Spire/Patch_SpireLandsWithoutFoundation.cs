using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir.Spire;

[HarmonyPatch(typeof(GravshipPlacementUtility), "SpawnFoundations")]
public static class Patch_SpireLandsWithoutFoundation
{
	private static readonly AccessTools.FieldRef<Gravship, Dictionary<IntVec3, TerrainDef>> Foundations = AccessTools.FieldRefAccess<Gravship, Dictionary<IntVec3, TerrainDef>>("foundations");

	private static readonly AccessTools.FieldRef<Gravship, Dictionary<IntVec3, TerrainDef>> Terrains = AccessTools.FieldRefAccess<Gravship, Dictionary<IntVec3, TerrainDef>>("terrains");

	public static void Prefix(Gravship gravship)
	{
		if (gravship != null)
		{
			Clean(Foundations(gravship));
			Clean(Terrains(gravship));
			Clean(Tmp("tmpFoundations")(gravship));
			Clean(Tmp("tmpTerrains")(gravship));
		}
	}

	private static AccessTools.FieldRef<Gravship, Dictionary<IntVec3, TerrainDef>> Tmp(string name)
	{
		return AccessTools.FieldRefAccess<Gravship, Dictionary<IntVec3, TerrainDef>>(name);
	}

	private static void Clean(Dictionary<IntVec3, TerrainDef> d)
	{
		if (d == null)
		{
			return;
		}
		foreach (IntVec3 item in (from kv in d
			where kv.Value == null
			select kv.Key).ToList())
		{
			d.Remove(item);
		}
	}
}
