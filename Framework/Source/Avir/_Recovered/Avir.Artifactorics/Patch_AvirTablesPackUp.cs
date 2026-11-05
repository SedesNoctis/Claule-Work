using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Thing), "Destroy")]
public static class Patch_AvirTablesPackUp
{
	public static bool Bypass;

	public static bool Prefix(Thing __instance, DestroyMode mode)
	{
		if (Bypass || mode != 0 || !(__instance is Building { Spawned: not false } building))
		{
			return true;
		}
		if (building.Faction != Faction.OfPlayer || !building.def.Minifiable || !building.def.defName.StartsWith("Avir_"))
		{
			return true;
		}
		Map map = building.Map;
		IntVec3 position = building.Position;
		MinifiedThing minifiedThing = building.MakeMinified();
		if (minifiedThing == null)
		{
			return true;
		}
		if (!GenPlace.TryPlaceThing(minifiedThing, position, map, ThingPlaceMode.Near))
		{
			GenPlace.TryPlaceThing(minifiedThing, CellFinder.StandableCellNear(position, map, 10f), map, ThingPlaceMode.Near);
		}
		Messages.Message(building.LabelCap + " was pushed out of its place and has been packed up beside it.", minifiedThing, MessageTypeDefOf.NeutralEvent, historical: false);
		return false;
	}
}
