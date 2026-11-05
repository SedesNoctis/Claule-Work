using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class MentalState_SongRetreat : MentalState
{
	public Pawn herald;

	public int until;

	public ThingWithComps dropped;

	private IntVec3 edge = IntVec3.Invalid;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref herald, "singer");
		Scribe_Values.Look(ref until, "until", 0);
		Scribe_References.Look(ref dropped, "dropped");
		Scribe_Values.Look(ref edge, "edge", IntVec3.Invalid);
	}

	public override void PostStart(string reason)
	{
		base.PostStart(reason);
		LinarianFeast.DropWeapon(pawn, ref dropped);
	}

	public override void MentalStateTick(int delta)
	{
		base.MentalStateTick(delta);
		if (Find.TickManager.TicksGame > until && until > 0)
		{
			RecoverFromState();
		}
		else if (pawn.IsHashIntervalTick(30, delta) && pawn.Spawned)
		{
			if (!edge.IsValid && !RCellFinder.TryFindBestExitSpot(pawn, out edge))
			{
				RecoverFromState();
			}
			else if ((pawn.Position - edge).LengthHorizontal <= 3f)
			{
				RecoverFromState();
			}
			else if (pawn.CurJobDef != JobDefOf.Goto || !(pawn.CurJob.targetA.Cell == edge))
			{
				Job job = JobMaker.MakeJob(JobDefOf.Goto, edge);
				job.locomotionUrgency = LocomotionUrgency.Sprint;
				job.exitMapOnArrival = false;
				pawn.jobs.StartJob(job, JobCondition.InterruptForced);
			}
		}
	}

	public override void PostEnd()
	{
		base.PostEnd();
		HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_RetreatWeary");
		if (namedSilentFail != null && !pawn.Dead)
		{
			LinarianHediffs.Add(pawn, namedSilentFail, 15000);
		}
		if (pawn.Spawned && dropped != null && dropped.Spawned && !dropped.Destroyed && pawn.CanReserveAndReach(dropped, PathEndMode.ClosestTouch, Danger.Deadly))
		{
			pawn.jobs?.jobQueue?.EnqueueFirst(JobMaker.MakeJob(JobDefOf.Equip, dropped));
		}
		if (pawn.Spawned)
		{
			MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "There was no order...", 4f);
		}
	}
}
