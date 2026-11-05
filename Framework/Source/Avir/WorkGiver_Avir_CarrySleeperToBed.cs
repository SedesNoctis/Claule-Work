using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// "Any pawn can carry them" - open to every colonist with Hauling enabled, no relation
    /// requirement. This is the general-purpose path; JobGiver_Avir_CarryLovedOneToBed is the
    /// separate autonomous path for close relations that fires without Hauling being on at all.
    /// </summary>
    public class WorkGiver_Avir_CarrySleeperToBed : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn.Map == null)
            {
                yield break;
            }

            foreach (Pawn other in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (Avir_CarrySleeperUtil.IsEligibleSleeper(other, pawn))
                {
                    yield return other;
                }
            }
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return pawn.Map == null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn sleeper))
            {
                return null;
            }

            if (!Avir_CarrySleeperUtil.IsEligibleSleeper(sleeper, pawn))
            {
                return null;
            }

            Building_Bed bed = Avir_CarrySleeperUtil.FindBedFor(sleeper);
            if (bed == null)
            {
                return null;
            }

            return JobMaker.MakeJob(AvirDefOf.Avir_CarrySleeperToBed, sleeper, bed);
        }
    }
}
