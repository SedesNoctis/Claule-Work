using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir;

public class WorkGiver_AvirGiveTonic : WorkGiver_Scanner
{
	public override PathEndMode PathEndMode => PathEndMode.Touch;

	public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

	public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
	{
		return pawn.Map.mapPawns.AllPawnsSpawned.Where(AvirTonic.Wants);
	}

	public override bool ShouldSkip(Pawn pawn, bool forced = false)
	{
		if (AvirTonic.Tonic != null && pawn.Map.listerThings.ThingsOfDef(AvirTonic.Tonic).Count != 0)
		{
			return !PotentialWorkThingsGlobal(pawn).Any();
		}
		return true;
	}

	public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
	{
		if (!(t is Pawn pawn2) || !AvirTonic.Wants(pawn2))
		{
			return null;
		}
		if (!pawn.CanReserve(pawn2, 1, -1, null, forced) || pawn2.IsForbidden(pawn))
		{
			return null;
		}
		Thing thing = GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, pawn.Map.listerThings.ThingsOfDef(AvirTonic.Tonic), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, (Thing x) => !x.IsForbidden(pawn) && pawn.CanReserve(x, 10, 1, null, forced));
		if (thing == null)
		{
			return null;
		}
		Job job = JobMaker.MakeJob(AvirTonicDefOf.Avir_GiveTonic, thing, pawn2);
		job.count = 1;
		return job;
	}
}
