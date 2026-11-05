using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(FleckMaker), "ThrowMetaIcon")]
public static class Patch_NoSleepZOverHer
{
	public static bool Prefix(IntVec3 cell, Map map, FleckDef fleckDef)
	{
		if (fleckDef != FleckDefOf.SleepZ || map == null || !cell.InBounds(map))
		{
			return true;
		}
		foreach (Thing thing in cell.GetThingList(map))
		{
			if (thing is Pawn pawn && pawn.Faction != Faction.OfPlayer && LinarianPresence.Record(pawn) != null)
			{
				return false;
			}
		}
		return true;
	}
}
