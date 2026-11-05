using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Carries a sleeping (not downed - Rescue already handles downed pawns) colonist who's
    /// resting somewhere that isn't a proper bed, and moves them into one, without waking
    /// them.
    ///
    /// HISTORY OF GETTING HERE (kept for future reference/debugging): the first attempt used
    /// pawn.carryTracker.TryStartCarry(sleeper) directly, which failed every time - decompiled
    /// source confirms Pawn.Downed is computed purely from healthState == PawnHealthState.Down,
    /// not from a live check of forceDowned, so briefly forcing forceDowned=true didn't
    /// actually make Downed true, and TryStartCarry's internal gating kept rejecting a
    /// non-downed pawn. Diagnostic logging also caught the sleeper ending up with a non-null
    /// holdingOwner despite TryStartCarry reporting failure - a partial/corrupted add.
    ///
    /// THE FIX: don't go through TryStartCarry's gating at all. Handle the container transfer
    /// directly - DeSpawn the sleeper, then ThingOwner.TryAddOrTransfer them into the
    /// carrier's carryTracker.innerContainer. TryAddOrTransfer is specifically built to
    /// cleanly move a Thing that already reports an owner (which is literally what the
    /// engine's own error message suggested back when this first started throwing "already in
    /// another container" warnings), so this sidesteps both problems - no Downed requirement,
    /// no ownership conflict.
    ///
    /// While actually carried, a pawn doesn't run its own AI/ticks normally, so there's no
    /// window for them to "wake up" mid-carry - that's what makes "without waking them up"
    /// hold.
    ///
    /// NEXT ISSUE FOUND (also fixed): once carried, manually dropping the sleeper and forcing
    /// a fresh LayDown job on them directly left them standing next to the bed instead of in
    /// it - the debug log showed their reservation attempt on the bed failing outright,
    /// because the carrier's OWN reservation on that same bed (from TryMakePreToilReservations)
    /// was still active at that exact moment. Fixed by using Toils_Bed.TuckIntoBed instead of
    /// doing the drop/reassign manually - it's the real vanilla helper Rescue itself uses for
    /// this exact hand-off, and it manages the reservation transition correctly.
    /// </summary>
    public class JobDriver_Avir_CarrySleeperToBed : JobDriver
    {
        private Pawn Sleeper => (Pawn)job.GetTarget(TargetIndex.A).Thing;
        private Building_Bed Bed => (Building_Bed)job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Deliberately NOT reserving the bed here. TuckIntoBed reserves the bed on the
            // SLEEPER's behalf when it hands off their resting job - if the carrier ALSO holds
            // a full-slot reservation on that same bed the whole time (as the earlier version
            // of this method did), there's no room left for the sleeper's own reservation to
            // succeed once TuckIntoBed tries to make it, and the hand-off fails outright. Only
            // the sleeper themselves needs reserving here, so nobody else tries to carry the
            // same person mid-job.
            return pawn.Reserve(Sleeper, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // The bed itself should never despawn mid-job - safe to check for the whole job.
            this.FailOnDespawnedOrNull(TargetIndex.B);

            Toil gotoSleeper = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoSleeper.FailOnDespawnedOrNull(TargetIndex.A);
            gotoSleeper.FailOn(() =>
            {
                Pawn sleeper = Sleeper;
                if (sleeper.Downed)
                {
                    Log.Warning("[Avir CarrySleeper] Failing: sleeper.Downed was already true before pickup.");
                    return true;
                }
                if (sleeper.CurrentBed() != null)
                {
                    Log.Warning("[Avir CarrySleeper] Failing: sleeper.CurrentBed() was " + sleeper.CurrentBed() + " (already counts as a proper bed).");
                    return true;
                }
                if (sleeper.Awake())
                {
                    Log.Warning("[Avir CarrySleeper] Failing: sleeper.Awake() was true (woke up before carrier arrived).");
                    return true;
                }
                return false;
            });
            yield return gotoSleeper;

            Toil startCarry = new Toil();
            startCarry.initAction = delegate
            {
                Pawn sleeper = Sleeper;

                if (sleeper.Spawned)
                {
                    sleeper.DeSpawn(DestroyMode.Vanish);
                }

                if (!pawn.carryTracker.innerContainer.TryAddOrTransfer(sleeper))
                {
                    Log.Warning("[Avir CarrySleeper] TryAddOrTransfer failed. sleeper.Spawned=" + sleeper.Spawned
                        + " sleeper.holdingOwner=" + (sleeper.holdingOwner == null ? "null" : sleeper.holdingOwner.ToString()));
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            startCarry.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return startCarry;

            // Once carried, the only thing worth checking is that we're still actually
            // holding them - not whether they're "spawned" (they won't be, on purpose).
            Toil gotoBed = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
            gotoBed.FailOn(() => pawn.carryTracker.CarriedThing != Sleeper);
            yield return gotoBed;

            // Toils_Bed.TuckIntoBed is vanilla's own purpose-built helper for exactly this -
            // it's what Rescue itself uses to place a carried pawn into a bed. Doing this
            // manually (drop + directly call StartJob on the sleeper) was the cause of the
            // "just standing there" bug: the carrier's own reservation on the bed was still
            // active at that point, so the sleeper's own LayDown job couldn't reserve the
            // same one-slot bed and silently failed to start. TuckIntoBed handles the
            // reservation handoff correctly, the way vanilla actually does it.
            yield return Toils_Bed.TuckIntoBed(TargetIndex.B, TargetIndex.A);
        }
    }
}
