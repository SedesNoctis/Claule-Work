using HarmonyLib;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Hediff), "LabelColor", MethodType.Getter)]
public static class Patch_HediffMaskColor
{
	public static void Postfix(Hediff __instance, ref Color __result)
	{
		if (LinarianHediffMask.Masked(__instance, out (string, string) _))
		{
			__result = Color.white;
		}
	}
}
