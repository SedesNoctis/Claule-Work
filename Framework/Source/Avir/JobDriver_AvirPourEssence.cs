using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// CARRY THE VIAL TO THE BODY, THEN POUR IT.
    ///
    /// WHY THIS EXISTS. Vanilla's JobDriver_UseItem walks the pawn to the ITEM and uses it where
    /// it stands - the target chosen in the targeter is never approached. That is why the essence
    /// was resurrecting corpses from across the map: it was not a bug in our comps, it is simply
    /// what UseItem does, and MechSerumResurrector behaves the same way.
    ///
    /// This replaces that job for the essence alone. Four steps, in the order a person would do
    /// them: go to the vial, pick it up, carry it to the body, pour.
    ///
    /// HOW THE CORPSE GETS HERE. CompUsable builds the job from CompProperties_Usable.useJob and
    /// puts the target the player picked into targetB. That is the same slot vanilla uses, so
    /// pointing useJob at this driver is the only wiring needed. If targetB ever arrives empty
    /// the job fails cleanly rather than pouring into nothing, and says so once in the log.
    /// </summary>
    public class JobDriver_AvirPourEssence : JobDriver
    {
        private const TargetIndex VialInd = TargetIndex.A;
        private const TargetIndex CorpseInd = TargetIndex.B;

        /// <summary>Long enough to read as a deliberate act rather than a click.</summary>
        private const int PourTicks = 300;

        private Corpse TargetCorpse
        {
            get { return job.GetTarget(CorpseInd).Thing as Corpse; }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // CompUsable normally sets this, but a count of zero makes StartCarryThing pick up
            // nothing at all, so it is forced here rather than trusted.
            if (job.count <= 0) job.count = 1;

            if (!pawn.Reserve(job.GetTarget(VialInd), job, 1, 1, null, errorOnFailed)) return false;

            if (job.GetTarget(CorpseInd).IsValid
                && !pawn.Reserve(job.GetTarget(CorpseInd), job, 1, -1, null, errorOnFailed)) return false;

            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(VialInd);
            this.FailOnDestroyedOrNull(CorpseInd);

            // 1. To the vial.
            yield return Toils_Goto.GotoThing(VialInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(VialInd);

            // 2. Pick it up. One, not the stack.
            yield return Toils_Haul.StartCarryThing(VialInd);

            // 3. THE STEP VANILLA'S USEITEM DOES NOT HAVE - carry it to the body.
            yield return Toils_Goto.GotoThing(CorpseInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(CorpseInd);

            // 4. Pour, with a progress bar over the corpse.
            Toil pour = Toils_General.Wait(PourTicks, CorpseInd);
            pour.WithProgressBarToilDelay(CorpseInd);
            pour.FailOnDespawnedNullOrForbidden(CorpseInd);
            yield return pour;

            yield return new Toil
            {
                initAction = delegate { Apply(); },
                defaultCompleteMode = ToilCompleteMode.Instant,
            };
        }

        private void Apply()
        {
            Corpse corpse = TargetCorpse;
            if (corpse == null)
            {
                Log.WarningOnce("[Avir] Phoenix pour job had no corpse in targetB - CompUsable may "
                    + "not be filling that slot on this version.", 0x4A5214);
                return;
            }

            Thing vial = pawn.carryTracker != null ? pawn.carryTracker.CarriedThing : null;
            if (vial == null) vial = job.GetTarget(VialInd).Thing;
            if (vial == null) return;

            ThingWithComps twc = vial as ThingWithComps;
            if (twc == null) return;

            CompTargetEffect_AvirPhoenixRevive comp = twc.GetComp<CompTargetEffect_AvirPhoenixRevive>();
            if (comp == null) return;

            comp.DoEffectOn(pawn, corpse);
        }
    }
}
