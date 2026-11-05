using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class MentalState_SongCircle : MentalState_Song
{
	public IntVec3 centre = IntVec3.Invalid;

	public float angle;

	protected override void Drive()
	{
		if (pawn.IsHashIntervalTick(600) && herald != null)
		{
			WhirlWitnesses.Look(pawn, herald);
		}
		Need_Rest need_Rest = pawn.needs?.rest;
		if (need_Rest != null)
		{
			need_Rest.CurLevel = Mathf.Max(0f, need_Rest.CurLevel - 0.004f);
			if (need_Rest.CurLevel <= 0.02f)
			{
				RecoverFromState();
				LinarianHediffs.Sleep(pawn, 10000);
				return;
			}
		}
		if (!centre.IsValid)
		{
			centre = pawn.Position;
		}
		if (!pawn.pather.Moving)
		{
			angle += 0.9f;
			IntVec3 intVec = centre + new IntVec3(Mathf.RoundToInt(Mathf.Cos(angle) * 3.5f), 0, Mathf.RoundToInt(Mathf.Sin(angle) * 3.5f));
			if (!intVec.InBounds(pawn.Map) || !intVec.Standable(pawn.Map))
			{
				intVec = centre;
			}
			Job job = JobMaker.MakeJob(JobDefOf.Goto, intVec);
			job.locomotionUrgency = LocomotionUrgency.Walk;
			job.expiryInterval = 600;
			Force(job);
		}
	}

	public override void MentalStateTick(int delta)
	{
		if (Find.TickManager.TicksGame > until)
		{
			RecoverFromState();
		}
		else if (pawn.IsHashIntervalTick(30, delta))
		{
			Drive();
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref centre, "centre", IntVec3.Invalid);
		Scribe_Values.Look(ref angle, "angle", 0f);
	}
}
