using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Avir;

public class JobDriver_AvirGiveTonic : JobDriver
{
	private Thing Tonic => job.targetA.Thing;

	private Pawn Patient => job.targetB.Pawn;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		if (pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed))
		{
			return pawn.Reserve(job.targetA, job, 10, 1, null, errorOnFailed);
		}
		return false;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
		this.FailOn(() => !AvirTonic.Wants(Patient));
		yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(TargetIndex.A);
		yield return Toils_Haul.StartCarryThing(TargetIndex.A);
		yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
		Toil toil = Toils_General.Wait(120).WithProgressBarToilDelay(TargetIndex.B);
		toil.FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);
		yield return toil;
		Toil toil2 = ToilMaker.MakeToil("GiveTonic");
		toil2.initAction = delegate
		{
			Thing carriedThing = pawn.carryTracker.CarriedThing;
			if (carriedThing != null && carriedThing.def == AvirTonic.Tonic)
			{
				Thing thing = ((carriedThing.stackCount > 1) ? carriedThing.SplitOff(1) : carriedThing);
				if (thing == carriedThing)
				{
					pawn.carryTracker.innerContainer.Remove(carriedThing);
				}
				thing.Ingested(Patient, 0f);
				if (!thing.Destroyed)
				{
					thing.Destroy();
				}
			}
		};
		toil2.defaultCompleteMode = ToilCompleteMode.Instant;
		yield return toil2;
	}
}
