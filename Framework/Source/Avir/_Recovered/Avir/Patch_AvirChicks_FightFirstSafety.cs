using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir;

[HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
public static class Patch_AvirChicks_FightFirstSafety
{
	public static void Prefix(Pawn ___pawn, ref Job newJob)
	{
		if (AvirChicks.ChicksOf.Count == 0 || newJob?.def == null)
		{
			return;
		}
		if (___pawn != null && newJob.def != AvirChicks.RideDef && newJob.def != AvirChicks.TakeDef && newJob.def != AvirChicks.PutToBedDef)
		{
			SetDownIfCarried(___pawn, newJob.targetA);
			SetDownIfCarried(___pawn, newJob.targetB);
			SetDownIfCarried(___pawn, newJob.targetC);
		}
		if (___pawn == null || !AvirChicks.ChicksOf.ContainsKey(___pawn) || (newJob.def != JobDefOf.AttackMelee && newJob.def != JobDefOf.AttackStatic))
		{
			return;
		}
		if (___pawn.Drafted)
		{
			AvirChicks.DismountAll(___pawn);
			return;
		}
		JobDef namedSilentFail = DefDatabase<JobDef>.GetNamedSilentFail("Avir_ChicksToSafety");
		IntVec3 intVec = AvirChicks.SafeCell(___pawn);
		if (namedSilentFail == null || !intVec.IsValid)
		{
			AvirChicks.DismountAll(___pawn);
		}
		else
		{
			newJob = JobMaker.MakeJob(namedSilentFail, intVec);
		}
	}

	private static void SetDownIfCarried(Pawn actor, LocalTargetInfo t)
	{
		if (t.Thing is Pawn pawn && pawn != actor && AvirChicks.CarrierOf.ContainsKey(pawn))
		{
			AvirChicks.LeaveUntil[pawn] = Find.TickManager.TicksGame + 2500;
			AvirChicks.Dismount(pawn);
		}
	}
}
