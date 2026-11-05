using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Avir;

public class JobDriver_AvirTakeChick : JobDriver
{
	private Pawn Chick => job.targetA.Pawn;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDespawnedOrNull(TargetIndex.A);
		this.FailOn(() => AvirChicks.CannotCarry(pawn, Chick) != null);
		yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
		yield return Toils_General.Do(delegate
		{
			AvirChicks.Mount(pawn, Chick);
		});
	}
}
