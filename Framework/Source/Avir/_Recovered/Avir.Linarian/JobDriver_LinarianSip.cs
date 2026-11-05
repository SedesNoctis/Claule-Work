using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class JobDriver_LinarianSip : JobDriver
{
	private Pawn Target => job.targetA.Pawn;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return true;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDespawnedOrNull(TargetIndex.A);
		yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
		Toil toil = ToilMaker.MakeToil("LinarianSip");
		toil.initAction = delegate
		{
			LinarianActs.SipNow(pawn, Target);
		};
		toil.defaultCompleteMode = ToilCompleteMode.Instant;
		yield return toil;
	}
}
