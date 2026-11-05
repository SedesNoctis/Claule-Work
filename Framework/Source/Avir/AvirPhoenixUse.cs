using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Drinking it. Applied at FULL severity, unlike every other drug in this mod - the peak is
    /// the first thing that happens and everything after it is the decay.
    ///
    /// Works on ANY pawn, Avir or not. That is the whole selling point: a colony with one Avir
    /// artificer can put a limb back on a human.
    /// </summary>
    public class IngestionOutcomeDoer_PhoenixEssence : IngestionOutcomeDoer
    {
        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int numTaken)
        {
            if (pawn == null || pawn.health == null) return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(AvirPhoenixDefOf.Avir_PhoenixEssenceHediff);
            if (existing != null)
            {
                // A second vial simply resets the clock. There is no overdose here - the drug
                // has only one thing it knows how to do.
                existing.Severity = 1f;
                return;
            }

            Hediff h = HediffMaker.MakeHediff(AvirPhoenixDefOf.Avir_PhoenixEssenceHediff, pawn);
            h.Severity = 1f;
            pawn.health.AddHediff(h);
        }
    }

    /// <summary>
    /// Properties for the revive effect. Exists only so the comp can be declared in XML without
    /// borrowing CompProperties_TargetEffectResurrect, which is vanilla's own and carries fields
    /// this does not use. CompTargetable calls every CompTargetEffect on the parent regardless
    /// of which properties class declared it.
    /// </summary>
    public class CompProperties_AvirPhoenixRevive : CompProperties
    {
        public CompProperties_AvirPhoenixRevive()
        {
            compClass = typeof(CompTargetEffect_AvirPhoenixRevive);
        }
    }

    /// <summary>
    /// Pouring it into a corpse. The body must be FRESH - any rot at all and there is nothing
    /// left for the fire to rebuild.
    ///
    /// A pawn revived this way gets ONLY the rebirth hediff. It never passes through the five
    /// stages: the essence is spent entirely on putting the body back together, and there is
    /// none left over to burn through a colonist who is already standing.
    /// </summary>
    public class CompTargetEffect_AvirPhoenixRevive : CompTargetEffect
    {
        public override void DoEffectOn(Pawn user, Thing target)
        {
            Corpse corpse = target as Corpse;
            if (corpse == null) return;

            if (corpse.GetRotStage() != RotStage.Fresh)
            {
                Messages.Message("AvirPhoenixTooRotten".Translate(corpse.LabelShort),
                                 corpse, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Pawn dead = corpse.InnerPawn;
            if (dead == null) return;

            if (dead.health != null && !AvirPhoenixUtil.HasRebirth(dead))
            {
                dead.health.AddHediff(AvirPhoenixDefOf.Avir_PhoenixRebirth);
            }

            GameComponent_AvirPhoenixRebirth comp = GameComponent_AvirPhoenixRebirth.Instance;
            if (comp != null) comp.Register(dead, corpse.Map, corpse.Position);

            ConsumeOneVial(user);
        }

        /// <summary>
        /// Spends the vial by hand.
        ///
        /// THE VIAL WAS NOT BEING CONSUMED. Vanilla's own use-item job destroys the thing after
        /// the effect runs, but the comp stack copied off the resurrector serum is evidently not
        /// reaching that path here - the essence stayed on the ground and could be reused
        /// forever. Rather than keep guessing at why, the effect now spends its own vial.
        ///
        /// Checks the parent stack first, then the user's inventory, and takes exactly one.
        /// Guarded against double-spending: if something else already destroyed the parent, the
        /// Destroyed check short-circuits and the inventory is left alone.
        /// </summary>
        private void ConsumeOneVial(Pawn user)
        {
            // CARRIED CASE FIRST. With JobDriver_AvirPourEssence the pawn is holding the vial
            // when this runs, and a carried thing has to leave the carry tracker before it is
            // destroyed or the pawn is left holding a destroyed reference.
            if (user != null && user.carryTracker != null)
            {
                Thing carried = user.carryTracker.CarriedThing;
                if (carried != null && carried.def == AvirPhoenixDefOf.Avir_PhoenixEssence)
                {
                    if (carried.stackCount > 1) carried.SplitOff(1).Destroy(DestroyMode.Vanish);
                    else user.carryTracker.DestroyCarriedThing();
                    return;
                }
            }

            if (parent != null && !parent.Destroyed)
            {
                if (parent.stackCount > 1) parent.SplitOff(1).Destroy(DestroyMode.Vanish);
                else parent.Destroy(DestroyMode.Vanish);
                return;
            }

            if (user == null || user.inventory == null) return;

            ThingOwner<Thing> inv = user.inventory.innerContainer;
            for (int i = inv.Count - 1; i >= 0; i--)
            {
                if (inv[i] == null || inv[i].def != AvirPhoenixDefOf.Avir_PhoenixEssence) continue;

                if (inv[i].stackCount > 1) inv[i].SplitOff(1).Destroy(DestroyMode.Vanish);
                else { Thing t = inv[i]; inv.RemoveAt(i); t.Destroy(DestroyMode.Vanish); }
                return;
            }
        }
    }

    /// <summary>
    /// Situational mood for the three healing stages. Split from the agony worker rather than
    /// done as one worker with two stages, because the two need different thought defs anyway -
    /// one is a comfort and the other is a -56 catastrophe, and they read better as separate
    /// entries in the mood tab.
    /// </summary>
    public class ThoughtWorker_AvirPhoenixGrace : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Hediff_PhoenixEssence h = Find(p);
            return h != null && h.Healing ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }

        internal static Hediff_PhoenixEssence Find(Pawn p)
        {
            if (p == null || p.health == null || p.health.hediffSet == null) return null;
            return p.health.hediffSet.GetFirstHediffOfDef(AvirPhoenixDefOf.Avir_PhoenixEssenceHediff)
                   as Hediff_PhoenixEssence;
        }
    }

    /// <summary>
    /// The Judgement and the Immolation. Two stages so the last twelve hours read worse than the
    /// twelve before them.
    /// </summary>
    public class ThoughtWorker_AvirPhoenixAgony : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Hediff_PhoenixEssence h = ThoughtWorker_AvirPhoenixGrace.Find(p);
            if (h == null) return ThoughtState.Inactive;
            if (h.Immolating) return ThoughtState.ActiveAtStage(1);
            if (h.Judging) return ThoughtState.ActiveAtStage(0);
            return ThoughtState.Inactive;
        }
    }
}
