using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    // WHY THIS EXISTS: our own Avir_TelekineticHaul job only fires when OUR WorkGiverDef gets
    // picked for a pawn's next job. It never sees:
    //   - Right-click "Haul [thing] to storage" - that float menu option builds its job through
    //     HaulAIUtility.HaulToStorageJob directly, bypassing every WorkGiverDef entirely.
    //   - Vanilla's own WorkGiver_HaulGeneral, and any OTHER mod's hauling WorkGiver.
    // All of those converge on one place in the end: a JobDriver_HaulToCell actually running.
    // That is where we intercept.
    //
    // FIX 2026-09-13 - WHY THIS MOVED OFF HaulAIUtility.HaulToStorageJob.
    //
    // It used to be a postfix on HaulToStorageJob, which BUILDS a job. The engine calls that
    // method speculatively, most notably while assembling the right-click float menu, purely to
    // find out whether "Haul to storage" should be offered. Nothing is assigned to anyone.
    //
    // The postfix answered that question by performing the work: it queued the teleport and
    // nulled the job. So merely pointing the cursor at an item moved it - and since the pawn's
    // state was never consulted, it moved just as happily while the pawn was asleep. Maximus
    // called it funny but inconvenient; it is also where the "promised but not moved" edge cases
    // that pendingThings exists to paper over were coming from, since a query could promise a
    // teleport that no job was ever going to account for.
    //
    // Patching the DRIVER instead means the teleport happens only when a pawn has genuinely taken
    // the job and started executing it. Asking whether a job is possible is now free of side
    // effects. A sleeping pawn cannot start a job, so the sleep case disappears on its own rather
    // than needing its own special-case check.
    //
    // Only the plain "haul to a cell" case is handled, exactly as before. Container-destination
    // hauls (HaulToContainer) still walk normally; Avir_TelekineticHaul's own container-aware
    // paths cover those separately.
    [HarmonyPatch(typeof(JobDriver_HaulToCell), "MakeNewToils")]
    public static class Patch_AvirHaulToStorageTeleport
    {
        [HarmonyPrefix]
        public static bool Prefix(JobDriver_HaulToCell __instance, ref IEnumerable<Toil> __result)
        {
            Pawn pawn = __instance?.pawn;
            Job job = __instance?.job;
            if (pawn?.Map == null || job == null) return true;

            Thing thing = job.targetA.Thing;
            IntVec3 destination = job.targetB.Cell;
            if (thing == null || !thing.Spawned || !destination.IsValid) return true;

            // ALREADY PROMISED A TELEPORT AND NOT MOVED YET. Acting again would queue a second
            // move for the same stack from a position it no longer really has - see
            // MapComponent_AvirTelekinesisCache.IsAwaitingTeleport for why that one tick matters.
            if (MapComponent_AvirTelekinesisCache.IsAwaitingTeleport(thing)) return true;

            if (!AvirTelekinesisUtility.TryGetRadius(pawn, out float radius)) return true;

            // BOTH ENDS INSIDE THE BUBBLE - the same rule every other telekinesis intercept uses.
            IntVec3 origin = pawn.Position;
            float radiusSq = radius * radius;
            if ((thing.Position - origin).LengthHorizontalSquared > radiusSq
                || (destination - origin).LengthHorizontalSquared > radiusSq)
            {
                return true; // out of reach - let the pawn walk it normally
            }

            MapComponent_AvirTelekinesisCache cache = pawn.Map.GetComponent<MapComponent_AvirTelekinesisCache>();
            if (cache == null) return true;

            __result = TelekineticToils(__instance, thing, destination);
            return false;
        }

        private static IEnumerable<Toil> TelekineticToils(JobDriver_HaulToCell driver, Thing thing, IntVec3 destination)
        {
            Toil toil = ToilMaker.MakeToil("AvirTelekineticHaulToCell");
            toil.defaultCompleteMode = ToilCompleteMode.Instant;
            toil.initAction = delegate
            {
                Pawn pawn = driver.pawn;

                // Job.count is what vanilla already decided to move (e.g. capped to what fits at
                // the destination) - 0/negative conventionally means "the whole stack".
                int count = driver.job.count > 0 ? driver.job.count : -1;

                // Never move the world synchronously from here either. This runs inside the
                // pawn's own job tick rather than inside someone else's enumeration, which is
                // safer than the old call site was, but the deferred queue is still the only
                // path that is guaranteed not to mutate listerHaulables underneath a live
                // iterator somewhere else in the frame.
                pawn.Map.GetComponent<MapComponent_AvirTelekinesisCache>()
                    ?.EnqueueTeleportToCell(thing, destination, count);

                driver.EndJobWith(JobCondition.Succeeded);
            };

            yield return toil;
        }
    }
}
