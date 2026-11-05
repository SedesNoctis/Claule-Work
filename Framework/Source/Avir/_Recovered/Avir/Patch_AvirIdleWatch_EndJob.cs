using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir;

[HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
public static class Patch_AvirIdleWatch_EndJob
{
	private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> pawnRef = AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

	public static void Prefix(Pawn_JobTracker __instance, JobCondition condition)
	{
		if (condition != JobCondition.Errored && condition != JobCondition.ErroredPather)
		{
			return;
		}
		try
		{
			Pawn p = pawnRef(__instance);
			if (AvirIdleWatch.Watched(p) && __instance.curJob != null && __instance.curJob.def != JobDefOf.Wait)
			{
				AvirIdleWatch.Note(p, condition.ToString() + ": " + AvirIdleWatch.Describe(p, __instance.curJob) + "; " + AvirIdleWatch.FlightState(p) + AvirIdleWatch.PatherNow(p), log: true);
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir idle] watch failed: " + ex.Message, 4870913);
		}
	}
}
