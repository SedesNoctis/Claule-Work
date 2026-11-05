using HarmonyLib;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Pawn_EquipmentTracker), "Notify_EquipmentAdded")]
public static class Patch_ArtifactEquipAdded
{
	public static void Postfix(Pawn_EquipmentTracker __instance)
	{
		ArtifactWearCache.Invalidate(__instance.pawn);
		ArtifactBonusCache.Invalidate(__instance.pawn);
	}
}
