using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class JobDriver_PlayWithGentleBeast : JobDriver
{
	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return true;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDespawnedOrNull(TargetIndex.A);
		yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
		Toil toil = ToilMaker.MakeToil("PlayWithGentleBeast");
		toil.defaultCompleteMode = ToilCompleteMode.Delay;
		toil.defaultDuration = ((job.def.joyDuration > 0) ? job.def.joyDuration : 1500);
		toil.handlingFacing = true;
		toil.tickIntervalAction = delegate(int delta)
		{
			Pawn pawn = job.targetA.Pawn;
			if (pawn != null)
			{
				base.pawn.rotationTracker.FaceTarget(pawn);
				if ((base.pawn.Position - pawn.Position).LengthHorizontal > 2.5f)
				{
					ReadyForNextToil();
				}
				else
				{
					if (base.pawn.IsHashIntervalTick(300))
					{
						FleckMaker.ThrowMetaIcon(base.pawn.Position, base.pawn.Map, FleckDefOf.Heart);
					}
					JoyUtility.JoyTickCheckEnd(base.pawn, delta, JoyTickFullJoyAction.EndJob, 1.6f);
				}
			}
		};
		yield return toil;
	}
}
