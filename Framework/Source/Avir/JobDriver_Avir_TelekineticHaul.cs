using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Single-item-at-a-time telekinetic hauling. The pawn does not move; the JOB doesn't even
    /// have a destination cell of its own - "origin" is just wherever the pawn happened to be
    /// standing when the job started. Each cycle:
    ///   1. Search: pick the next thing to move. Bill-ingredient delivery is checked first (so a
    ///      crafter working a bench with this amulet keeps getting fed), then general "tidy up
    ///      loose haulables" second.
    ///   2. Windup: a short telekinetic "grab" delay with a spark effect at the source.
    ///   3. Transit: the thing is despawned and treated as in-flight for a short duration scaled
    ///      to distance, then placed at its destination with a spark effect there too.
    /// If nothing is found for a few cycles in a row, the job ends cleanly and the pawn falls
    /// back to normal work assignment.
    /// </summary>
    public class JobDriver_Avir_TelekineticHaul : JobDriver
    {
        private const int WindupTicks = 20;
        private const int QueueRefreshIntervalTicks = 40;
        private const int MaxIdleCyclesBeforeDone = 3;

        private IntVec3 origin = IntVec3.Invalid;
        private float radius;
        private int ownerKey;

        private Avir_TeleTransfer activeTransfer;
        private Thing carriedThing;
        private bool winding;
        private bool inTransit;
        private int phaseTicksRemaining;
        private int idleCycles;
        private int nextRefreshTick;

        private readonly HashSet<Thing> excludedThingsScratch = new HashSet<Thing>();
        private readonly HashSet<IntVec3> excludedDestinationsScratch = new HashSet<IntVec3>();

        public override string GetReport()
        {
            return "Avir_TelekinesisReport".Translate();
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // No standing reservation is needed: individual items are claimed via
            // AvirTelekinesisClaims and pawn.Reserve at the moment each one is actually lifted,
            // not up front, since the exact set of things moved is decided cycle-by-cycle.
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !AvirTelekinesisUtility.TryGetRadius(pawn, out _));

            Toil channel = ToilMaker.MakeToil("Avir_TelekineticHaul");

            channel.initAction = delegate
            {
                origin = pawn.Position;
                ownerKey = pawn.thingIDNumber;
                AvirTelekinesisUtility.TryGetRadius(pawn, out radius);
                AvirTelekinesisClaims.ReleaseAllForOwner(ownerKey);
                activeTransfer = null;
                carriedThing = null;
                winding = false;
                inTransit = false;
                idleCycles = 0;
                nextRefreshTick = 0;
            };

            channel.tickAction = delegate { TickChannel(); };
            channel.defaultCompleteMode = ToilCompleteMode.Never;
            channel.AddFinishAction(delegate { CleanupOnFinish(); });

            yield return channel;
        }

        private void TickChannel()
        {
            if (!AvirTelekinesisUtility.TryGetRadius(pawn, out radius))
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (inTransit)
            {
                AdvanceTransit();
                return;
            }

            if (winding)
            {
                AdvanceWindup();
                return;
            }

            if (Find.TickManager.TicksGame < nextRefreshTick)
            {
                return;
            }

            nextRefreshTick = Find.TickManager.TicksGame + QueueRefreshIntervalTicks;
            TryStartNextTransfer();
        }

        private void TryStartNextTransfer()
        {
            excludedThingsScratch.Clear();
            excludedDestinationsScratch.Clear();

            if (AvirTelekinesisUtility.TryFindBillIngredientTransfer(pawn, origin, radius, excludedThingsScratch, ownerKey, out Avir_TeleTransfer billTransfer)
                && AvirTelekinesisClaims.TryClaim(billTransfer.thing, ownerKey)
                && pawn.CanReserve(billTransfer.thing, 1, billTransfer.thing.stackCount))
            {
                BeginTransfer(billTransfer);
                return;
            }

            if (AvirTelekinesisUtility.TryFindHaulTransfer(pawn, origin, radius, excludedThingsScratch, excludedDestinationsScratch, ownerKey, out Avir_TeleTransfer haulTransfer)
                && AvirTelekinesisClaims.TryClaim(haulTransfer.thing, ownerKey)
                && pawn.CanReserve(haulTransfer.thing, 1, haulTransfer.thing.stackCount))
            {
                BeginTransfer(haulTransfer);
                return;
            }

            idleCycles++;
            if (idleCycles >= MaxIdleCyclesBeforeDone)
            {
                ReadyForNextToil();
            }
        }

        private void BeginTransfer(Avir_TeleTransfer transfer)
        {
            activeTransfer = transfer;
            winding = true;
            phaseTicksRemaining = WindupTicks;
            idleCycles = 0;
            pawn.rotationTracker.FaceCell(transfer.sourceCell);
        }

        private void AdvanceWindup()
        {
            phaseTicksRemaining--;
            if (phaseTicksRemaining > 0)
            {
                return;
            }

            winding = false;

            if (activeTransfer?.thing == null || activeTransfer.thing.Destroyed || !activeTransfer.thing.Spawned)
            {
                AvirTelekinesisClaims.Release(activeTransfer?.thing, ownerKey);
                activeTransfer = null;
                return;
            }

            if (!pawn.Reserve(activeTransfer.thing, job, 1, activeTransfer.thing.stackCount, null, false))
            {
                AvirTelekinesisClaims.Release(activeTransfer.thing, ownerKey);
                activeTransfer = null;
                return;
            }

            carriedThing = LiftThing(activeTransfer);
            if (carriedThing == null)
            {
                AvirTelekinesisClaims.Release(activeTransfer.thing, ownerKey);
                activeTransfer = null;
                return;
            }

            FleckMaker.ThrowMicroSparks(AvirTelekinesisUtility.WorldPosForCell(activeTransfer.sourceCell), pawn.Map);

            Vector3 start = AvirTelekinesisUtility.WorldPosForCell(activeTransfer.sourceCell);
            Vector3 end = AvirTelekinesisUtility.WorldPosForCell(activeTransfer.destination);
            phaseTicksRemaining = AvirTelekinesisUtility.CalculateTravelTicks(start, end);
            inTransit = true;
        }

        private void AdvanceTransit()
        {
            phaseTicksRemaining--;
            if (phaseTicksRemaining > 0)
            {
                return;
            }

            inTransit = false;
            FinishActiveTransfer();
        }

        private Thing LiftThing(Avir_TeleTransfer transfer)
        {
            Thing thing = transfer.thing;
            if (thing == null || thing.Destroyed || !thing.Spawned)
            {
                return null;
            }

            int desiredCount = transfer.count > 0 ? Mathf.Min(transfer.count, thing.stackCount) : thing.stackCount;
            Thing lifted = desiredCount < thing.stackCount ? thing.SplitOff(desiredCount) : thing;
            if (lifted.Spawned)
            {
                lifted.DeSpawn();
            }

            return lifted;
        }

        private void FinishActiveTransfer()
        {
            if (carriedThing == null)
            {
                AvirTelekinesisClaims.Release(activeTransfer?.thing, ownerKey);
                activeTransfer = null;
                return;
            }

            IntVec3 destination = activeTransfer.destination;
            Thing resultingThing = null;
            bool placed = destination.IsValid
                && (GenPlace.TryPlaceThing(carriedThing, destination, pawn.Map, ThingPlaceMode.Direct, out resultingThing)
                    || GenPlace.TryPlaceThing(carriedThing, destination, pawn.Map, ThingPlaceMode.Near, out resultingThing));

            if (!placed)
            {
                // Nowhere valid to put it (someone built over the stockpile mid-flight, etc.) -
                // drop it back where it came from rather than losing it.
                GenPlace.TryPlaceThing(carriedThing, activeTransfer.sourceCell, pawn.Map, ThingPlaceMode.Near, out resultingThing);
            }
            else
            {
                FleckMaker.ThrowMicroSparks(AvirTelekinesisUtility.WorldPosForCell(destination), pawn.Map);
            }

            AvirTelekinesisUtility.NoteMoved(resultingThing ?? carriedThing);
            AvirTelekinesisUtility.NoteMoved(activeTransfer.thing);
            AvirTelekinesisClaims.Release(activeTransfer.thing, ownerKey);
            carriedThing = null;
            activeTransfer = null;
        }

        private void CleanupOnFinish()
        {
            if (carriedThing != null)
            {
                GenPlace.TryPlaceThing(carriedThing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                carriedThing = null;
            }

            AvirTelekinesisClaims.ReleaseAllForOwner(ownerKey);
            activeTransfer = null;
            winding = false;
            inTransit = false;
        }
    }
}
