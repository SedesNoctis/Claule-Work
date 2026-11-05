using HarmonyLib;
using UnityEngine;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Building), "DrawColor", MethodType.Getter)]
public static class Patch_NoStuffTintBuilding
{
	public static void Postfix(Building __instance, ref Color __result)
	{
		if (NoStuffTint.On(__instance.def))
		{
			__result = Color.white;
		}
	}
}
