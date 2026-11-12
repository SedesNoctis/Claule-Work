using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace FlightGene;

public class HediffComp_FlightMonitor : HediffComp
{
	private bool shouldRemove;

	public bool autoTriggered;

	private int clearChecks;

	private const int LookAheadNodes = 8;

	public HediffCompProperties_FlightMonitor Props => (HediffCompProperties_FlightMonitor)props;

	public override bool CompShouldRemove
	{
		get
		{
			if (shouldRemove)
			{
				return !StrandedIfLanded;
			}
			return false;
		}
	}

	private bool StrandedIfLanded
	{
		get
		{
			Pawn pawn = base.Pawn;
			if (pawn == null || !pawn.Spawned || pawn.Map == null)
			{
				return false;
			}
			if (pawn.Dead)
			{
				return false;
			}
			if (AvirFlightRules.CannotStandAt(pawn.Map, pawn.Position))
			{
				return true;
			}
			return AvirFlightTerrain.IsWaterAt(pawn.Map, pawn.Position);
		}
	}

	public override void CompPostPostAdd(DamageInfo? dinfo)
	{
		base.CompPostPostAdd(dinfo);
		AvirFlightRegistry.Register(base.Pawn);
	}

	// Fix254: a flyer killed in the air stays in the registry no longer. A dead pawn's hediffs do not tick and are not
	// removed, so nothing ever took it out - and with one stale name in it the registry's whole short cut ("nobody is
	// flying": one integer read on every step of every pawn) was off for the rest of the session.
	public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
	{
		base.Notify_PawnDied(dinfo, culprit);
		AvirFlightRegistry.Unregister(base.Pawn);
	}

	public override void CompPostPostRemoved()
	{
		base.CompPostPostRemoved();
		AvirFlightRegistry.Unregister(base.Pawn);
	}

	public override void CompPostTick(ref float severityAdjustment)
	{
		base.CompPostTick(ref severityAdjustment);
		Pawn pawn = base.Pawn;
		if (pawn == null)
		{
			return;
		}
		AvirFlightRegistry.Register(pawn);
		if (!pawn.IsHashIntervalTick(60))
		{
			return;
		}
		if (pawn.Dead || pawn.Downed)
		{
			shouldRemove = true;
			return;
		}
		if (AvirFlightUtility.GroundedByForm(pawn))
		{
			shouldRemove = true;
			return;
		}
		Need_Rest need_Rest = pawn.needs?.rest;
		if (need_Rest != null && need_Rest.CurLevel <= Props.restThreshold)
		{
			if (!shouldRemove && PawnUtility.ShouldSendNotificationAbout(pawn))
			{
				Messages.Message("Flight_EndedExhausted".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, historical: false);
			}
			shouldRemove = true;
		}
		AutoLandIfClear(pawn);
		AvirFlightRescue.NoteHookAlive("HediffComp_FlightMonitor.CompPostTick");
		AvirFlightRescue.CheckAndRescue(pawn);
	}

	private void AutoLandIfClear(Pawn pawn)
	{
		if (!autoTriggered || shouldRemove || !pawn.Spawned || pawn.Map == null)
		{
			return;
		}
		if (AvirFlightRules.CannotStandAt(pawn.Map, pawn.Position))
		{
			clearChecks = 0;
			return;
		}
		if (RouteAheadNeedsFlight(pawn))
		{
			clearChecks = 0;
			return;
		}
		clearChecks++;
		if (clearChecks >= 2)
		{
			shouldRemove = true;
		}
	}

	private static bool RouteAheadNeedsFlight(Pawn pawn)
	{
		Pawn_PathFollower pather = pawn.pather;
		if (pather == null || !pather.Moving)
		{
			return false;
		}
		PawnPath curPath = pather.curPath;
		if (curPath == null || !curPath.Found)
		{
			return false;
		}
		int num = Math.Min(8, curPath.NodesLeftCount);
		for (int i = 0; i < num; i++)
		{
			if (AvirFlightRules.CannotStandAt(pawn.Map, curPath.Peek(i)))
			{
				return true;
			}
		}
		return false;
	}

	public override void CompExposeData()
	{
		base.CompExposeData();
		Scribe_Values.Look(ref shouldRemove, "shouldRemove", defaultValue: false);
		Scribe_Values.Look(ref autoTriggered, "autoTriggered", defaultValue: false);
		Scribe_Values.Look(ref clearChecks, "clearChecks", 0);
	}
}
