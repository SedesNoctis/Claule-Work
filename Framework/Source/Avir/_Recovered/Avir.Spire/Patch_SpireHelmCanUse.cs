using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Spire;

[HarmonyPatch(typeof(CompPilotConsole), "CanUseNow")]
public static class Patch_SpireHelmCanUse
{
	public static bool Prefix(CompPilotConsole __instance, ref AcceptanceReport __result)
	{
		if (!(__instance is CompSpireHelm compSpireHelm))
		{
			return true;
		}
		if (compSpireHelm.engine == null)
		{
			__result = "CannotLaunchNoEngine".Translate().CapitalizeFirst();
			return false;
		}
		if (compSpireHelm.engine.ValidSubstructure.Count == 0 || !compSpireHelm.engine.ValidSubstructureAt(compSpireHelm.parent.InteractionCell))
		{
			__result = "the cradle's front (where the pilot stands) must be in the lift zone";
			return false;
		}
		MapComponent_SpireLift mapComponent_SpireLift = MapComponent_SpireLift.Of(compSpireHelm.parent.Map);
		int num = compSpireHelm.parent.Map.mapPawns.FreeColonistsSpawned.Count + compSpireHelm.parent.Map.mapPawns.SpawnedColonyAnimals.Count;
		int num2 = mapComponent_SpireLift?.StandableCount() ?? 0;
		if (num2 < num)
		{
			__result = "the lift zone has room to stand for " + num2 + ", but " + num + " must board - stretch it";
			return false;
		}
		__result = compSpireHelm.engine.CanLaunch(compSpireHelm);
		return false;
	}
}
