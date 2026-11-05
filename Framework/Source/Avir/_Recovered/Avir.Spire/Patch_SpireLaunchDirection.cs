using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Avir.Spire;

[HarmonyPatch(typeof(Gravship), "DetermineLaunchDirection")]
public static class Patch_SpireLaunchDirection
{
	private static readonly AccessTools.FieldRef<Gravship, Building> Console = AccessTools.FieldRefAccess<Gravship, Building>("pilotConsole");

	public static bool Prefix(Gravship __instance)
	{
		if (Console(__instance) != null)
		{
			return true;
		}
		if (__instance.launchDirection == IntVec3.Zero)
		{
			__instance.launchDirection = IntVec3.North;
		}
		return false;
	}
}
