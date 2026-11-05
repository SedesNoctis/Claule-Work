using HarmonyLib;
using RimWorld;

namespace Avir.Spire;

[HarmonyPatch(typeof(Building_GravEngine), "MaxFuel", MethodType.Getter)]
public static class Patch_SpireMaxFuel
{
	public static void Postfix(Building_GravEngine __instance, ref float __result)
	{
		if (__instance is Building_SpireCradle { Spire: not null } building_SpireCradle)
		{
			__result += building_SpireCradle.Spire.Capacity / 5f;
		}
	}
}
