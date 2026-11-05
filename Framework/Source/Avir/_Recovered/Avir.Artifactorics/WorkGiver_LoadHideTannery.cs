using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics;

public class WorkGiver_LoadHideTannery : WorkGiver_Scanner
{
	public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

	public override PathEndMode PathEndMode => PathEndMode.Touch;

	public override bool ShouldSkip(Pawn pawn, bool forced = false)
	{
		return !pawn.Map.listerBuildings.allBuildingsColonist.Any((Building b) => b.TryGetComp<CompHideTannery>() != null);
	}

	private static Thing FindHide(Pawn pawn, CompHideTannery t)
	{
		return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableEver), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, (Thing x) => t.IsHide(x) && !x.IsForbidden(pawn) && pawn.CanReserve(x));
	}

	public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
	{
		CompHideTannery compHideTannery = thing.TryGetComp<CompHideTannery>();
		if (compHideTannery == null || thing.Faction != pawn.Faction || compHideTannery.Space <= 0 || thing.IsForbidden(pawn) || !pawn.CanReserve(thing, 1, -1, null, forced))
		{
			return false;
		}
		return FindHide(pawn, compHideTannery) != null;
	}

	public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
	{
		CompHideTannery compHideTannery = thing.TryGetComp<CompHideTannery>();
		Thing thing2 = ((compHideTannery == null) ? null : FindHide(pawn, compHideTannery));
		if (thing2 == null)
		{
			return null;
		}
		Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LoadHideTannery"), thing2, thing);
		job.count = Mathf.Min(compHideTannery.Space, thing2.stackCount);
		return job;
	}
}
