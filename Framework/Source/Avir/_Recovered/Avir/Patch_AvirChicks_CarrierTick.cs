using HarmonyLib;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Pawn), "TickInterval")]
public static class Patch_AvirChicks_CarrierTick
{
	public static void Postfix(Pawn __instance, int delta)
	{
		if (AvirChicks.ChicksOf.Count != 0 && AvirChicks.ChicksOf.ContainsKey(__instance))
		{
			AvirChicks.CarrierTick(__instance, delta);
		}
	}
}
