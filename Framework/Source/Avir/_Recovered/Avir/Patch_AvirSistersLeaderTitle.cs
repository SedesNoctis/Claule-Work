using HarmonyLib;
using RimWorld;

namespace Avir;

[HarmonyPatch(typeof(IdeoFoundation), "GenerateLeaderTitle")]
public static class Patch_AvirSistersLeaderTitle
{
	public static void Postfix(IdeoFoundation __instance)
	{
		if (AvirSisters.IsSisters(__instance.ideo))
		{
			__instance.ideo.leaderTitleMale = "Matriarch";
			__instance.ideo.leaderTitleFemale = "Matriarch";
		}
	}
}
