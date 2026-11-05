using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;

namespace Avir.Spire;

[HarmonyPatch(typeof(Building_GravEngine), "MissingComponents", MethodType.Getter)]
public static class Patch_SpireNeedsOnlyHelm
{
	public static void Postfix(Building_GravEngine __instance, ref List<GravshipComponentTypeDef> __result)
	{
		if (__instance is Building_SpireCradle && __result != null && __result.Count != 0)
		{
			__result = __result.Where((GravshipComponentTypeDef d) => d.defName != "Thruster" && d.defName != "FuelStorage" && d.defName != "Controls").ToList();
		}
	}
}
