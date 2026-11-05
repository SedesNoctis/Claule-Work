using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Ritualistics;

public class WorkGiver_AutoRitual : WorkGiver_Scanner
{
	public override PathEndMode PathEndMode => PathEndMode.OnCell;

	public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
	{
		return pawn.Map.listerBuildings.allBuildingsColonist.Where((Building b) => b.TryGetComp<CompRitualCircle>()?.autoPerform ?? false);
	}

	public override bool ShouldSkip(Pawn pawn, bool forced = false)
	{
		return !PotentialWorkThingsGlobal(pawn).Any();
	}

	public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
	{
		CompRitualCircle compRitualCircle = t.TryGetComp<CompRitualCircle>();
		if (compRitualCircle == null || !compRitualCircle.WantsAutoNow(pawn))
		{
			return null;
		}
		if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced))
		{
			return null;
		}
		if (!pawn.CanReach(t.Position, PathEndMode.OnCell, Danger.Some))
		{
			return null;
		}
		return JobMaker.MakeJob(RitualDefOf.Avir_PerformRitualAuto, t);
	}
}
