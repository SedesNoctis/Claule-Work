using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Pawn), "DescriptionFlavor", MethodType.Getter)]
public static class Patch_StandInDescription
{
	public static void Postfix(Pawn __instance, ref string __result)
	{
		Pawn pawn = LinarianStandIn.OwnerOf(__instance);
		if (pawn != null)
		{
			__result = LinarianDisguise.Description(pawn);
		}
	}
}
