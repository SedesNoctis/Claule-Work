using HarmonyLib;
using UnityEngine;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Thing), "DrawColor", MethodType.Getter)]
public static class Patch_NoStuffTint
{
	public static void Postfix(Thing __instance, ref Color __result)
	{
		if (__instance.Stuff != null && NoStuffTint.On(__instance.def))
		{
			__result = Color.white;
		}
	}
}
