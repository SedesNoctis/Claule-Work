using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Sends Fearborn to feed a reservoir that has asked for it.
    ///
    /// SCANS RESERVOIRS, NOT PAWNS. A ThingRequest for one def is a lister lookup; the alternative
    /// - every Fearborn checking every tick whether anything nearby is thirsty - is the same answer
    /// arrived at expensively.
    ///
    /// CAPTIVES ARE NOT HANDLED HERE. A prisoner will not walk to a machine and open a vein because
    /// a work giver asked politely; draining them is done by a warden bringing them, or by the
    /// player ordering it. This giver is for colonists who are willing.
    /// </summary>
    public class WorkGiver_AvirDonateEssence : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamedSilentFail("Avir_EssenceReservoir"));

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            // The gene test is the Fearborn gate - Arcane Hunger is only ever granted to that
            // lineage - and it costs one cached lookup, so it goes first.
            Gene_ArcaneHunger gene = Gene_ArcaneHunger.Of(pawn);
            if (gene == null || !gene.Active) return true;
            return gene.Value <= Building_AvirEssenceReservoir.VolunteerFloor;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_AvirEssenceReservoir reservoir)) return false;
            if (!forced && !reservoir.WantsDonation) return false;
            if (reservoir.IsForbidden(pawn) || reservoir.IsBurning()) return false;
            if (!pawn.CanReserve(reservoir, 1, -1, null, forced)) return false;

            // A prisoner or slave is never a volunteer, even if they are somehow running work.
            if (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony) return false;

            return reservoir.DonatableFrom(pawn) > 0.01f;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Avir_DonateEssence");
            return def == null ? null : JobMaker.MakeJob(def, t);
        }
    }

    /// <summary>
    /// Walk to the reservoir, stand there for a while, hand over part of yourself.
    ///
    /// The delay is not flavour. Donation is a chunk of a pawn's reserve in one action, and an
    /// instantaneous transfer would let the player tap a dozen Avir in a second with no chance to
    /// change their mind or to notice the cost.
    /// </summary>
    public class JobDriver_AvirDonateEssence : JobDriver
    {
        private const int DonateTicks = 480;

        private Building_AvirEssenceReservoir Reservoir => (Building_AvirEssenceReservoir)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil give = Toils_General.Wait(DonateTicks, TargetIndex.A);
            give.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            give.WithProgressBarToilDelay(TargetIndex.A);
            give.PlaySustainerOrSound(SoundDefOf.Interact_Tend);
            yield return give;

            yield return Toils_General.Do(() =>
            {
                Building_AvirEssenceReservoir reservoir = Reservoir;
                if (reservoir == null) return;
                reservoir.AcceptDonation(pawn);
            });
        }
    }
}
