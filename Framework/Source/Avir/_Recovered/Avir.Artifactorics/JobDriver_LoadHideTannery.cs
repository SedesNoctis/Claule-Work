using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics;

public class JobDriver_LoadHideTannery : JobDriver
{
	private Thing Hide => job.GetTarget(TargetIndex.A).Thing;

	private Thing Tannery => job.GetTarget(TargetIndex.B).Thing;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		if (pawn.Reserve(Hide, job, 1, -1, null, errorOnFailed))
		{
			return pawn.Reserve(Tannery, job, 1, -1, null, errorOnFailed);
		}
		return false;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
		this.FailOn(delegate
		{
			CompHideTannery compHideTannery2 = Tannery.TryGetComp<CompHideTannery>();
			return compHideTannery2 != null && compHideTannery2.Space <= 0;
		});
		yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(TargetIndex.A);
		yield return Toils_Haul.StartCarryThing(TargetIndex.A, putRemainderInQueue: false, subtractNumTakenFromJobCount: true);
		yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
		Toil toil = ToilMaker.MakeToil("LoadHides");
		toil.initAction = delegate
		{
			CompHideTannery compHideTannery = Tannery.TryGetComp<CompHideTannery>();
			Thing carriedThing = pawn.carryTracker.CarriedThing;
			if (compHideTannery != null && carriedThing != null)
			{
				int num = Mathf.Min(compHideTannery.Space, carriedThing.stackCount);
				if (num > 0)
				{
					pawn.carryTracker.innerContainer.TryTransferToContainer(carriedThing, compHideTannery.hides, num);
				}
			}
		};
		toil.defaultCompleteMode = ToilCompleteMode.Instant;
		yield return toil;
	}
}
