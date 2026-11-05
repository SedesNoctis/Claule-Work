using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public class MentalState_SongBearer : MentalState_Song
{
	public Thing item;

	public IntVec3 lair = IntVec3.Invalid;

	private bool carrying;

	protected override void Drive()
	{
		if (pawn.RaceProps.Humanlike && pawn.needs?.rest != null)
		{
			pawn.needs.rest.CurLevel = Mathf.Min(1f, pawn.needs.rest.CurLevel + 0.002f);
		}
		if (pawn.IsHashIntervalTick(250))
		{
			foreach (Pawn item in pawn.Map.mapPawns.AllPawnsSpawned.Where((Pawn a) => a.RaceProps.predator && !a.Dead && !a.Downed && (a.CurJob?.AnyTargetIs(pawn) ?? false)).ToList())
			{
				IntVec3 fleeDest = CellFinderLoose.GetFleeDest(item, new List<Thing> { pawn }, 24f);
				if (fleeDest.IsValid)
				{
					item.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Flee, fleeDest, pawn), JobCondition.InterruptForced);
				}
			}
		}
		if (carrying)
		{
			if (!lair.IsValid || (pawn.Position - lair).LengthHorizontal <= 2f)
			{
				Need_Food need_Food = herald?.needs?.food;
				if (need_Food != null && need_Food.CurLevelPercentage < 0.6f && pawn.inventory.innerContainer.Any((Thing t) => t.def.IsNutritionGivingIngestible))
				{
					foreach (Thing item2 in pawn.inventory.innerContainer.Where((Thing t) => t.def.IsNutritionGivingIngestible).ToList())
					{
						item2.Destroy();
					}
					need_Food.CurLevelPercentage = 1f;
				}
				pawn.inventory?.innerContainer.TryDropAll(pawn.Position, pawn.Map, ThingPlaceMode.Near);
				RecoverFromState();
			}
			else if (pawn.CurJobDef != JobDefOf.Goto)
			{
				Job job = JobMaker.MakeJob(JobDefOf.Goto, lair);
				job.expiryInterval = 2500;
				Force(job);
			}
		}
		else if (this.item == null || this.item.Destroyed || !this.item.Spawned)
		{
			RecoverFromState();
		}
		else if ((this.item.Position - pawn.Position).LengthHorizontal <= 1.5f)
		{
			Thing thing = this.item.SplitOff(Mathf.Min(this.item.stackCount, Rand.RangeInclusive(10, 40)));
			if (pawn.inventory != null && pawn.inventory.innerContainer.TryAdd(thing))
			{
				carrying = true;
			}
			else if (!thing.Spawned)
			{
				GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
			}
		}
		else if (pawn.CurJobDef != JobDefOf.Goto || pawn.CurJob.targetA.Thing != this.item)
		{
			Job job2 = JobMaker.MakeJob(JobDefOf.Goto, this.item);
			job2.expiryInterval = 2500;
			Force(job2);
		}
	}

	public override void MentalStateTick(int delta)
	{
		if (Find.TickManager.TicksGame > until)
		{
			pawn.inventory?.innerContainer.TryDropAll(pawn.Position, pawn.Map, ThingPlaceMode.Near);
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
		Scribe_References.Look(ref item, "item");
		Scribe_Values.Look(ref lair, "lair", IntVec3.Invalid);
		Scribe_Values.Look(ref carrying, "carrying", defaultValue: false);
	}
}
