using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Avir.Ritualistics;

public class JobDriver_PerformRitualAuto : JobDriver
{
	private CompRitualCircle Circle => job.targetA.Thing?.TryGetComp<CompRitualCircle>();

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDestroyedOrNull(TargetIndex.A);
		this.FailOnForbidden(TargetIndex.A);
		yield return Toils_Goto.GotoCell(job.targetA.Thing.Position, PathEndMode.OnCell);
		Toil toil = ToilMaker.MakeToil("BeginRitualAuto");
		toil.initAction = delegate
		{
			CompRitualCircle circle = Circle;
			if (circle == null || !circle.WantsAutoNow(pawn))
			{
				EndJobWith(JobCondition.Incompletable);
			}
			else
			{
				circle.Begin(pawn, IntVec3.Invalid);
			}
		};
		toil.defaultCompleteMode = ToilCompleteMode.Instant;
		yield return toil;
		Toil toil2 = ToilMaker.MakeToil("PerformRitualAuto");
		toil2.defaultCompleteMode = ToilCompleteMode.Never;
		toil2.FailOn(() => Circle == null || Circle.performer != pawn);
		toil2.tickIntervalAction = delegate(int delta)
		{
			if (!Circle.RitualTick(pawn, delta))
			{
				ReadyForNextToil();
			}
		};
		toil2.AddFinishAction(delegate
		{
			if (Circle?.performer == pawn)
			{
				Circle.End(null);
			}
		});
		yield return toil2;
	}
}
