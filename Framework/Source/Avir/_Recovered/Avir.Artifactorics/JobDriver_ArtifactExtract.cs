using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics;

public class JobDriver_ArtifactExtract : JobDriver
{
	private const TargetIndex SpotInd = TargetIndex.C;

	private Thing Garment => job.targetA.Thing;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		if (pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
		{
			return pawn.Reserve(job.targetC, job, 1, -1, null, errorOnFailed);
		}
		return false;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		this.FailOnDestroyedOrNull(TargetIndex.A);
		this.FailOnDespawnedNullOrForbidden(TargetIndex.C);
		yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.A);
		yield return Toils_Haul.StartCarryThing(TargetIndex.A);
		yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.InteractionCell);
		yield return ArtifactJobUtility.WorkToil(this, TargetIndex.C, 3000f, Finish);
	}

	private void Finish()
	{
		Thing garment = Garment;
		Thing thing = job.GetTarget(TargetIndex.C).Thing;
		CompArtifactSocket compArtifactSocket = garment.Socket();
		MapComponent_ArtifactOrders mapComponent_ArtifactOrders = MapComponent_ArtifactOrders.For(pawn.Map);
		ArtifactOrder artifactOrder = mapComponent_ArtifactOrders?.OrderFor(garment);
		if (compArtifactSocket?.inlaid == null)
		{
			pawn.carryTracker.TryDropCarriedThing(thing.Position, ThingPlaceMode.Near, out var _);
			return;
		}
		bool num = ArtifactoricsSettingsDef.Main.CountsAsAvir(pawn);
		ArtifactState inlaid = compArtifactSocket.inlaid;
		compArtifactSocket.inlaid = null;
		Thing thing2 = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_Artifact);
		thing2.TryGetComp<CompArtifact>().state = inlaid;
		bool garmentSurvived = true;
		if (num)
		{
			pawn.carryTracker.TryDropCarriedThing(thing.Position, ThingPlaceMode.Near, out var resultingThing2);
			if (resultingThing2 != null && artifactOrder?.wearer != null)
			{
				resultingThing2.SetForbidden(value: true, warnOnFail: false);
			}
			Messages.Message(pawn.LabelShort + " drew the artifact out of " + garment.LabelNoCount + " without harm.", garment, MessageTypeDefOf.PositiveEvent, historical: false);
		}
		else
		{
			int burntPoints = inlaid.burntPoints;
			inlaid.LoseForeignExtractionCells();
			int num2 = inlaid.burntPoints - burntPoints;
			string labelNoCount = garment.LabelNoCount;
			if (pawn.carryTracker.CarriedThing != null)
			{
				pawn.carryTracker.DestroyCarriedThing();
			}
			else if (!garment.Destroyed)
			{
				garment.Destroy();
			}
			garmentSurvived = false;
			Messages.Message(pawn.LabelShort + " tore the artifact out of " + labelNoCount + ". The " + ArtifactCarrierUtility.Noun(garment) + " is ruined and the artifact lost its outer cells" + ((num2 > 0) ? (" (" + num2 + " point" + ((num2 == 1) ? "" : "s") + " burnt).") : "."), MessageTypeDefOf.NegativeEvent, historical: false);
		}
		GenPlace.TryPlaceThing(thing2, thing.Position, pawn.Map, ThingPlaceMode.Near);
		if (artifactOrder != null)
		{
			mapComponent_ArtifactOrders.WorkFinished(artifactOrder, garmentSurvived);
		}
	}
}
