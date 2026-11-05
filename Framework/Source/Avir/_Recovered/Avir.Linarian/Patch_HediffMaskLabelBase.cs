using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Hediff), "LabelBase", MethodType.Getter)]
public static class Patch_HediffMaskLabelBase
{
	public static void Postfix(Hediff __instance, ref string __result)
	{
		if (LinarianHediffMask.Masked(__instance, out (string, string) mask))
		{
			(__result, _) = mask;
		}
	}
}
