using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Spire;

[HarmonyPatch(typeof(RitualBehaviorWorker_GravshipLaunch), "Cleanup")]
public static class Patch_LaunchCleanupDead
{
	public static void Prefix(LordJob_Ritual ritual)
	{
		Building_GravEngine building_GravEngine = ritual?.selectedTarget.Thing?.TryGetComp<CompPilotConsole>()?.engine;
		if (building_GravEngine != null)
		{
			building_GravEngine.pawnsToBoard?.RemoveWhere((Pawn p) => p == null || p.Dead || p.jobs == null);
			building_GravEngine.pawnsToLeave?.RemoveWhere((Pawn p) => p == null || p.Dead || p.jobs == null);
		}
	}
}
