using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class MentalState_SongConfused : MentalState_Song
{
	public ThingWithComps dropped;

	private IntVec3 origin = IntVec3.Invalid;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref dropped, "dropped");
		Scribe_Values.Look(ref origin, "origin", IntVec3.Invalid);
	}

	public override void PostStart(string reason)
	{
		base.PostStart(reason);
		origin = pawn.Position;
		LinarianFeast.DropWeapon(pawn, ref dropped);
	}

	protected override void Drive()
	{
		if (!origin.IsValid)
		{
			origin = pawn.Position;
		}
		if (Rand.Chance(0.15f) && pawn.Spawned)
		{
			MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, Rand.Element("AAAH!", "No, no, no-", "Make them stop!", "The voices!"), 2.5f);
		}
		Pawn_PathFollower pather = pawn.pather;
		if ((pather == null || !pather.Moving) && CellFinder.TryFindRandomReachableNearbyCell(origin, pawn.Map, 6f, TraverseParms.For(pawn), (IntVec3 c) => c.Standable(pawn.Map), null, out var result))
		{
			Job job = JobMaker.MakeJob(JobDefOf.Goto, result);
			job.locomotionUrgency = LocomotionUrgency.Sprint;
			Force(job);
		}
	}

	public override void PostEnd()
	{
		base.PostEnd();
		if (pawn.Spawned && dropped != null && dropped.Spawned && !dropped.Destroyed && pawn.CanReserveAndReach(dropped, PathEndMode.ClosestTouch, Danger.Deadly))
		{
			pawn.jobs?.jobQueue?.EnqueueFirst(JobMaker.MakeJob(JobDefOf.Equip, dropped));
		}
	}
}
