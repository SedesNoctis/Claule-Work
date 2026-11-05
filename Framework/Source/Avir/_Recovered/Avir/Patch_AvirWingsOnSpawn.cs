using HarmonyLib;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Pawn), "SpawnSetup")]
public static class Patch_AvirWingsOnSpawn
{
	public static void Postfix(Pawn __instance)
	{
		RaceProperties raceProps = __instance.RaceProps;
		if (raceProps != null && raceProps.Humanlike)
		{
			AvirWingParts.Ensure(__instance);
		}
	}
}
