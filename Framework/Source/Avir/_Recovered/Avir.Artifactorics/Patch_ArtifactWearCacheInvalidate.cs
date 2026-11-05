using HarmonyLib;
using RimWorld;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelChanged")]
public static class Patch_ArtifactWearCacheInvalidate
{
	public static void Postfix(Pawn_ApparelTracker __instance)
	{
		ArtifactWearCache.Invalidate(__instance.pawn);
		ArtifactBonusCache.Invalidate(__instance.pawn);
	}
}
