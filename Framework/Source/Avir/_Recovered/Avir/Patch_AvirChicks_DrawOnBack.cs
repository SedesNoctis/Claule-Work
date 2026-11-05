using HarmonyLib;
using UnityEngine;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Pawn), "DrawPos", MethodType.Getter)]
public static class Patch_AvirChicks_DrawOnBack
{
	public static void Postfix(Pawn __instance, ref Vector3 __result)
	{
		if (AvirChicks.CarrierOf.Count != 0 && AvirChicks.CarrierOf.TryGetValue(__instance, out var value) && value.Spawned)
		{
			__result = AvirChicks.SeatPos(value, __instance);
		}
	}
}
