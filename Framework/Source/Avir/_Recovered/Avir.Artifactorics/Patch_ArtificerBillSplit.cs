using HarmonyLib;
using RimWorld;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Bill_Production), "ShouldDoNow")]
public static class Patch_ArtificerBillSplit
{
	public static void Postfix(Bill_Production __instance, ref bool __result)
	{
		if (__result)
		{
			WorkGiverDef current = WorkGiver_ArtificerBill.current;
			if (current != null && !WorkGiver_ArtificerBill.Takes(current, __instance))
			{
				__result = false;
			}
		}
	}
}
