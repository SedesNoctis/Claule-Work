using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Precept), "Icon", MethodType.Getter)]
public static class Patch_AvirSistersRoleIcon
{
	private static Texture2D matriarch;

	private static Texture2D deeds;

	public static void Postfix(Precept __instance, ref Texture2D __result)
	{
		if (__instance is Precept_Role && AvirSisters.IsSisters(__instance.ideo))
		{
			if (__instance.def.leaderRole)
			{
				__result = matriarch ?? (matriarch = ContentFinder<Texture2D>.Get("UI/Ideology/Avir_Role_Matriarch", reportFailure: false)) ?? __result;
			}
			else if (__instance.def.defName == "IdeoRole_Moralist")
			{
				__result = deeds ?? (deeds = ContentFinder<Texture2D>.Get("UI/Ideology/Avir_Role_LordOfDeeds", reportFailure: false)) ?? __result;
			}
		}
	}
}
