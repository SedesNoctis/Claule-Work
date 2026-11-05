using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir;

public class JobDriver_AvirPutChickToBed : JobDriver
{
	private Pawn Chick => job.targetA.Pawn;

	private Building_Bed Bed => job.targetB.Thing as Building_Bed;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return true;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDespawnedOrNull(TargetIndex.B);
		this.FailOn(() => Chick == null || !AvirChicks.CarrierOf.TryGetValue(Chick, out var value) || value != pawn);
		yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
		yield return Toils_General.Do(delegate
		{
			Pawn chick = Chick;
			Building_Bed bed = Bed;
			AvirChicks.Dismount(chick, RestUtility.GetBedSleepingSlotPosFor(chick, bed));
			if (chick.Spawned && chick.jobs != null)
			{
				chick.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, bed), JobCondition.InterruptForced);
			}
		});
	}

	public override string GetReport()
	{
		return "putting " + (Chick?.LabelShort ?? "a chick") + " to bed.";
	}
}
