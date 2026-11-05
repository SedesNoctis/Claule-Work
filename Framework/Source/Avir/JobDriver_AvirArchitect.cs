using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Sends the bound Architect to their core when there is quality work in reach.
    ///
    /// ONLY THE BOUND PAWN. Everyone else is refused at ShouldSkip, so a core with no Architect - or
    /// with somebody else's - never appears as work to them at all. That is the whole point of the
    /// binding: the pawn at the core IS the quality roll for everything the core finishes, and it
    /// must never be whoever wandered past.
    /// </summary>
    public class WorkGiver_AvirArchitect : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamedSilentFail("Avir_FeignalliteCore"));

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            // Cheap gate first: a colony has few Fearborn and one core, and this runs for every
            // pawn looking for work.
            Gene_ArcaneHunger gene = Gene_ArcaneHunger.Of(pawn);
            return gene == null || !gene.Active;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompFeignalliteCore core = t?.TryGetComp<CompFeignalliteCore>();
            if (core == null || core.architect != pawn) return false;

            if (t.IsForbidden(pawn) || t.IsBurning()) return false;
            if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;

            CompRefuelable fuel = t.TryGetComp<CompRefuelable>();
            if (fuel != null && !fuel.HasFuel) return false;

            return core.HasQualityWorkNearby();
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Avir_ArchitectWork");
            return def == null ? null : JobMaker.MakeJob(def, t);
        }
    }

    /// <summary>
    /// Stands the Architect at the core and holds them there.
    ///
    /// THE DRIVER DOES NO CONSTRUCTION ITSELF. All it does is tell the comp, every tick, that its
    /// Architect is present; the comp does the building on its own schedule. Splitting it this way
    /// means the core's existing pass - blueprint to frame, work, complete, deconstruct - stays one
    /// piece of code with one set of rules, and the job is only a presence check.
    ///
    /// It ends when the work runs out, the fuel runs out, or the pawn's arcane hunger empties. None
    /// of those is an error; all three are the machine reaching its limit.
    /// </summary>
    public class JobDriver_AvirArchitect : JobDriver
    {
        private Thing Core => job.targetA.Thing;

        private CompFeignalliteCore CoreComp => Core?.TryGetComp<CompFeignalliteCore>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => CoreComp == null || CoreComp.architect != pawn);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            Toil work = ToilMaker.MakeToil("AvirArchitectWork");
            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.handlingFacing = true;

            work.tickAction = () =>
            {
                CompFeignalliteCore core = CoreComp;
                if (core == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // The single line that opens quality mode. Sent every tick because the comp treats
                // it as a short-lived permission rather than a switch - a job that dies without
                // cleaning up therefore closes the mode by itself within half a second.
                core.NotifyArchitectWorking();

                pawn.rotationTracker.FaceTarget(Core);
                // Signature is (pawn, delta, chairsOnly) - the delta is how many ticks of comfort
                // to credit, and it has no default. Passing 1 per tick is what vanilla work toils do.
                pawn.GainComfortFromCellIfPossible(1);

                CompRefuelable fuel = Core.TryGetComp<CompRefuelable>();
                if (fuel != null && !fuel.HasFuel)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                // Re-checked rather than assumed: the Architect spends arcane hunger while working,
                // so the reason to stop usually arrives during the job rather than before it.
                Gene_ArcaneHunger gene = Gene_ArcaneHunger.Of(pawn);
                if (gene == null || !gene.Active || gene.Value <= 0.01f)
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }

                if (!core.HasQualityWorkNearby())
                {
                    EndJobWith(JobCondition.Succeeded);
                }
            };

            // The bar shows arcane hunger spent, not progress toward anything - there is no single
            // job to be a fraction of, and the reserve is the thing that actually runs out.
            work.WithProgressBar(TargetIndex.A, () => 1f - (Gene_ArcaneHunger.Of(pawn)?.Value ?? 0f));

            yield return work;
        }
    }
}
