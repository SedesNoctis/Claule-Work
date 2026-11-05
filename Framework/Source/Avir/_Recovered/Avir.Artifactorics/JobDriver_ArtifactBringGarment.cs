using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics;

public class JobDriver_ArtifactBringGarment : JobDriver
{
	private Thing Garment => job.targetA.Thing;

	private Building_ArtificerSpot Spot => (Building_ArtificerSpot)job.targetB.Thing;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return true;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOn(() => Garment == null || ArtifactCarrierUtility.HolderOf(Garment) != pawn);
		this.FailOnDespawnedOrNull(TargetIndex.B);
		yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
		Toil toil = Toils_General.Wait(60);
		toil.WithProgressBarToilDelay(TargetIndex.B);
		yield return toil;
		yield return new Toil
		{
			initAction = delegate
			{
				if (ArtifactCarrierUtility.DropFromHolder(pawn, Garment, Spot.Position, out var dropped))
				{
					dropped.SetForbidden(value: true, warnOnFail: false);
					ArtifactOrder artifactOrder = MapComponent_ArtifactOrders.For(pawn.Map)?.OrderFor(dropped);
					if (artifactOrder != null && artifactOrder.stage == ArtifactOrderStage.WaitingForWearer)
					{
						artifactOrder.stage = ArtifactOrderStage.Ready;
					}
				}
			},
			defaultCompleteMode = ToilCompleteMode.Instant
		};
	}
}
