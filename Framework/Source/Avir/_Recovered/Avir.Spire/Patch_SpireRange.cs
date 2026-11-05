using HarmonyLib;
using RimWorld;

namespace Avir.Spire;

[HarmonyPatch(typeof(Building_GravEngine), "MaxLaunchDistance", MethodType.Getter)]
public static class Patch_SpireRange
{
	public static void Postfix(Building_GravEngine __instance, ref int __result)
	{
		if (__instance is Building_SpireCradle { Spire: not null } building_SpireCradle)
		{
			__result += building_SpireCradle.Spire.Range;
		}
	}
}
