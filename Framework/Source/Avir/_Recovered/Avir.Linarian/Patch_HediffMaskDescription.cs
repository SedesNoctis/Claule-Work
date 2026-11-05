using System.Reflection;
using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch]
public static class Patch_HediffMaskDescription
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.PropertyGetter(typeof(Hediff), "Description");
	}

	private static bool Prepare()
	{
		return TargetMethod() != null;
	}

	public static void Postfix(Hediff __instance, ref string __result)
	{
		if (LinarianHediffMask.Masked(__instance, out (string, string) mask))
		{
			__result = mask.Item2;
		}
	}
}
