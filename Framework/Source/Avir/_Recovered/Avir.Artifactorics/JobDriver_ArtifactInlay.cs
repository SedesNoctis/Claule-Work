using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics;

public class JobDriver_ArtifactInlay : JobDriver
{
	private const TargetIndex SpotInd = TargetIndex.C;

	private Thing Artifact => job.targetA.Thing;

	private Thing Garment => job.targetB.Thing;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		if (pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed) && pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed))
		{
			return pawn.Reserve(job.targetC, job, 1, -1, null, errorOnFailed);
		}
		return false;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDestroyedOrNull(TargetIndex.B);
		this.FailOnDespawnedNullOrForbidden(TargetIndex.C);
		AddFinishAction(delegate
		{
			if (Artifact != null && !Artifact.Destroyed && pawn.inventory.innerContainer.Contains(Artifact))
			{
				pawn.inventory.innerContainer.TryDrop(Artifact, pawn.Position, pawn.Map, ThingPlaceMode.Near, out var _);
			}
		});
		yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TargetIndex.A);
		yield return Toils_Haul.StartCarryThing(TargetIndex.A);
		yield return new Toil
		{
			initAction = delegate
			{
				Thing carriedThing = pawn.carryTracker.CarriedThing;
				if (carriedThing != null)
				{
					pawn.carryTracker.innerContainer.TryTransferToContainer(carriedThing, pawn.inventory.innerContainer);
				}
			},
			defaultCompleteMode = ToilCompleteMode.Instant
		};
		yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.B);
		yield return Toils_Haul.StartCarryThing(TargetIndex.B);
		yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.InteractionCell);
		yield return ArtifactJobUtility.WorkToil(this, TargetIndex.C, 3000f, Finish);
	}

	private void Finish()
	{
		Thing garment = Garment;
		CompArtifact compArtifact = Artifact?.TryGetComp<CompArtifact>();
		CompArtifactSocket compArtifactSocket = garment.Socket();
		MapComponent_ArtifactOrders mapComponent_ArtifactOrders = MapComponent_ArtifactOrders.For(pawn.Map);
		ArtifactOrder artifactOrder = mapComponent_ArtifactOrders?.OrderFor(garment);
		if (compArtifact != null && compArtifactSocket != null && !compArtifactSocket.HasArtifact)
		{
			compArtifactSocket.inlaid = compArtifact.state.Clone();
			pawn.inventory.innerContainer.Remove(Artifact);
			Artifact.Destroy();
			Messages.Message(pawn.LabelShort + " inlaid the artifact into " + garment.LabelNoCount + ".", garment, MessageTypeDefOf.PositiveEvent, historical: false);
		}
		pawn.carryTracker.TryDropCarriedThing(job.GetTarget(TargetIndex.C).Thing.Position, ThingPlaceMode.Near, out var resultingThing);
		if (resultingThing != null && artifactOrder?.wearer != null)
		{
			resultingThing.SetForbidden(value: true, warnOnFail: false);
		}
		if (artifactOrder != null)
		{
			mapComponent_ArtifactOrders.WorkFinished(artifactOrder, garmentSurvived: true);
		}
	}
}
