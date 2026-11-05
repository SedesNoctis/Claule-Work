using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Avir.Spire;

[HarmonyPatch(typeof(JobGiver_BoardOrLeaveGravship), "TryGiveJob")]
public static class Patch_SpireBoarding
{
	public static bool Prefix(Pawn pawn, ref Job __result)
	{
		if (!ModsConfig.OdysseyActive || pawn?.Map == null || pawn.Downed)
		{
			return true;
		}
		if (pawn.Map.listerThings.ThingsOfDef(ThingDefOf.GravEngine).Any())
		{
			return true;
		}
		Building_SpireCradle building_SpireCradle = SpireAsGravship.On(pawn.Map);
		if (building_SpireCradle == null)
		{
			return true;
		}
		__result = null;
		if (building_SpireCradle.pawnsToBoard != null && building_SpireCradle.pawnsToBoard.Contains(pawn))
		{
			IntVec3 spot;
			if (GravshipUtility.IsOnboardGravship_NewTemp(pawn.Position, building_SpireCradle, pawn, desperate: false, respectAllowedAreas: false))
			{
				spot = pawn.Position;
			}
			else if (!GravshipUtility.TryFindSpotOnGravship(pawn, building_SpireCradle, out spot))
			{
				Messages.Message("FailedToBoardGravship".Translate(pawn.Named("PAWN"), building_SpireCradle.RenamableLabel), pawn, MessageTypeDefOf.NegativeEvent, historical: false);
				building_SpireCradle.pawnsToBoard.Remove(pawn);
				return false;
			}
			if (pawn.lord?.LordJob is LordJob_Ritual lordJob_Ritual)
			{
				lordJob_Ritual.Cancel();
			}
			Job job = JobMaker.MakeJob(JobDefOf.GotoShip, spot);
			job.locomotionUrgency = LocomotionUrgency.Jog;
			__result = job;
			return false;
		}
		if (building_SpireCradle.pawnsToLeave != null && building_SpireCradle.pawnsToLeave.Contains(pawn))
		{
			IntVec3 spot2;
			if (!GravshipUtility.IsOnboardGravship_NewTemp(pawn.Position, building_SpireCradle, pawn, desperate: true, respectAllowedAreas: false))
			{
				spot2 = pawn.Position;
			}
			else if (!GravshipUtility.TryFindSpotOffGravship(pawn, building_SpireCradle, out spot2))
			{
				Messages.Message("FailedToLeaveGravship".Translate(pawn.Named("PAWN"), building_SpireCradle.RenamableLabel), pawn, MessageTypeDefOf.NegativeEvent, historical: false);
				building_SpireCradle.pawnsToLeave.Remove(pawn);
				return false;
			}
			pawn.lord?.LordJob?.Notify_PawnLost(pawn, PawnLostCondition.ForcedByPlayerAction);
			Job job2 = JobMaker.MakeJob(JobDefOf.LeaveShip, spot2);
			job2.locomotionUrgency = LocomotionUrgency.Jog;
			__result = job2;
		}
		return false;
	}
}
