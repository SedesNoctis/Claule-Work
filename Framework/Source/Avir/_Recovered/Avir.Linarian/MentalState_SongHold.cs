using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class MentalState_SongHold : MentalState_Song
{
	public IntVec3 point = IntVec3.Invalid;

	private IntVec3 post = IntVec3.Invalid;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref point, "point", IntVec3.Invalid);
		Scribe_Values.Look(ref post, "post", IntVec3.Invalid);
	}

	protected override void Drive()
	{
		if (!point.IsValid)
		{
			point = pawn.Position;
		}
		if (!post.IsValid && !CellFinder.TryFindRandomReachableNearbyCell(point, pawn.Map, 3f, TraverseParms.For(pawn), (IntVec3 x) => x.Standable(pawn.Map), null, out post))
		{
			post = point;
		}
		if (pawn.Position != post)
		{
			Job job = JobMaker.MakeJob(JobDefOf.Goto, post);
			job.locomotionUrgency = LocomotionUrgency.Sprint;
			Force(job);
		}
		else
		{
			Force(JobMaker.MakeJob(JobDefOf.Wait_Combat, 600, checkOverrideOnExpiry: true));
		}
	}
}
