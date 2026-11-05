using System.Linq;
using Avir.Artifactorics;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(CompObserver), "CompTickRare")]
public static class Patch_ObserverSeesSinger
{
	public static void Postfix(CompObserver __instance)
	{
		Map map = __instance.parent.Map;
		if (map == null || !__instance.BothEyes)
		{
			return;
		}
		float range = __instance.Range;
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned.Where((Pawn p) => p.Faction != Faction.OfPlayer && LinarianPresence.Record(p) != null && (LinarianVeil.Veiled(p) || LinarianVeil.InForm(p) != null) && (p.Position - __instance.parent.Position).LengthHorizontal <= range).ToList())
		{
			LinarianGods.SeenQuietly(item);
		}
	}
}
