using HarmonyLib;
using RimWorld;

namespace Avir;

[HarmonyPatch(typeof(Precept_Role), "GenerateNameRaw")]
public static class Patch_AvirSistersRoleName
{
	public static void Postfix(Precept_Role __instance, ref string __result)
	{
		if (AvirSisters.IsSisters(__instance.ideo) && __instance.def.defName == "IdeoRole_Moralist")
		{
			__result = "Lord of Deeds";
		}
	}
}
