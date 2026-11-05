using Avir;
using RimWorld;
using UnityEngine;
using Verse;

namespace FlightGene;

public class CompAbilityEffect_ToggleFlight : CompAbilityEffect
{
	private new CompProperties_AbilityToggleFlight Props => (CompProperties_AbilityToggleFlight)props;

	private Pawn Pawn => parent.pawn;

	public override bool GizmoDisabled(out string reason)
	{
		Pawn pawn = Pawn;
		if (pawn != null && AvirFlightUtility.GroundedByForm(pawn) && !AvirFlightUtility.IsFlying(pawn))
		{
			reason = "In a beast's form only the crow flies.";
			return true;
		}
		return base.GizmoDisabled(out reason);
	}

	public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
	{
		base.Apply(target, dest);
		Pawn pawn = Pawn;
		if (pawn == null || pawn.health == null)
		{
			return;
		}
		HediffDef def = Props.hediffDef ?? FlightDefOf.Flight_Hediff;
		Hediff firstHediffOfDef = pawn.health.hediffSet.GetFirstHediffOfDef(def);
		if (firstHediffOfDef == null && AvirFlightUtility.GroundedByForm(pawn))
		{
			return;
		}
		if (firstHediffOfDef != null)
		{
			if (pawn.Spawned && pawn.Map != null && AvirFlightRules.CannotStandAt(pawn.Map, pawn.Position))
			{
				if (PawnUtility.ShouldSendNotificationAbout(pawn))
				{
					Messages.Message("Avir_FlightCannotLandHere".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.RejectInput, historical: false);
				}
			}
			else
			{
				pawn.health.RemoveHediff(firstHediffOfDef);
			}
			return;
		}
		if (AvirWingParts.Clipped(pawn))
		{
			if (PawnUtility.ShouldSendNotificationAbout(pawn))
			{
				Messages.Message(pawn.LabelShort + " cannot fly: a wing is broken.", pawn, MessageTypeDefOf.RejectInput, historical: false);
			}
			return;
		}
		Need_Rest need_Rest = pawn.needs?.rest;
		if (need_Rest != null && need_Rest.CurLevel <= Props.restThreshold)
		{
			if (PawnUtility.ShouldSendNotificationAbout(pawn))
			{
				Messages.Message("Flight_TooTired".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.RejectInput, historical: false);
			}
			return;
		}
		pawn.health.AddHediff(def);
		if (pawn.Spawned)
		{
			FleckMaker.ThrowDustPuffThick(pawn.DrawPos, pawn.Map, 1.5f, new Color(1f, 1f, 1f, 0.6f));
		}
	}
}
