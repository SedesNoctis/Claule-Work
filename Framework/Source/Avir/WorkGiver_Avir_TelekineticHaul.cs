using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Only proves that SOME telekinetic haul is possible near this pawn right now - it does not
    /// try to fully pre-plan the job. Once JobDriver_Avir_TelekineticHaul actually starts, it
    /// independently rescans (bill ingredients first, then general storage) every refresh, so the
    /// pawn may end up moving something other than the exact "seed" thing that triggered this.
    /// </summary>
    public class WorkGiver_Avir_TelekineticHaul : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.None;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.HaulableAlways);

        public override bool Prioritized => true;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return pawn?.Map == null || !AvirTelekinesisUtility.TryGetRadius(pawn, out _);
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn.Map == null || !AvirTelekinesisUtility.TryGetRadius(pawn, out float radius))
            {
                yield break;
            }

            float radiusSq = radius * radius;
            foreach (Thing t in pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling())
            {
                if ((t.Position - pawn.Position).LengthHorizontalSquared <= radiusSq)
                {
                    yield return t;
                }
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!AvirTelekinesisUtility.TryGetRadius(pawn, out float radius))
            {
                return false;
            }

            if (!AvirTelekinesisUtility.CanHaul(pawn, t, pawn.thingIDNumber))
            {
                return false;
            }

            if (AvirTelekinesisUtility.TryFindStorageCellInRadius(pawn, t, pawn.Position, radius, null, out _))
            {
                return true;
            }

            // Fallback: t might have nowhere in a storage group to go yet, but still be exactly
            // what a nearby bench's active bill is short on (e.g. a freshly mined raw resource
            // sitting on the ground with no stockpile assigned). The JobDriver rescans properly
            // once it starts either way, so this is just enough to justify offering the job.
            HashSet<Thing> scratch = new HashSet<Thing>();
            return AvirTelekinesisUtility.TryFindBillIngredientTransfer(pawn, pawn.Position, radius, scratch, pawn.thingIDNumber, out Avir_TeleTransfer transfer)
                && transfer.thing == t;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HasJobOnThing(pawn, t, forced))
            {
                return null;
            }

            return JobMaker.MakeJob(AvirDefOf.Avir_TelekineticHaul, t);
        }
    }
}
