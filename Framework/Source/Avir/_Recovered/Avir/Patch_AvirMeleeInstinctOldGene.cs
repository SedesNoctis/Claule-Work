using HarmonyLib;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Pawn), "SpawnSetup")]
public static class Patch_AvirMeleeInstinctOldGene
{
	public static void Postfix(Pawn __instance)
	{
		AvirMeleeInstinct.DropOldAptitude(__instance);
	}
}
