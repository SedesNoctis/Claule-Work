using System.Linq;
using Avir.Artifactorics;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(CompTotemScare), "CompTickRare")]
public static class Patch_ScareTotemSinger
{
	public static void Postfix(CompTotemScare __instance)
	{
		Map map = __instance.parent.Map;
		CompArtifactHost compArtifactHost = __instance.parent.TryGetComp<CompArtifactHost>();
		if (map == null || compArtifactHost == null || compArtifactHost.Working == 0)
		{
			return;
		}
		float range = __instance.Range;
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned.Where((Pawn p) => p.Faction != Faction.OfPlayer && LinarianPresence.Record(p) != null && LinarianVeil.InForm(p) != null && (p.Position - __instance.parent.Position).LengthHorizontal <= range).ToList())
		{
			if (!LinarianGods.ScaredOff(item) && Rand.Chance(0.3f) && compArtifactHost.Spend(1f))
			{
				LinarianGods.Scare(item, __instance.parent, range);
			}
		}
	}
}
