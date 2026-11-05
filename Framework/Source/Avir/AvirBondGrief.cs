using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// WHAT IS LEFT WHEN THE OTHER ONE IS GONE.
    ///
    /// A HEDIFF, NOT A MEMORY, AND THE CHOICE IS THE WHOLE POINT. Vanilla's PsychicBondTorn is a
    /// Thought_Memory: thirty days, lerpMoodToZero, and then it is as though nothing happened. That
    /// is exactly the shape Maximus rejected. Memories expire, get cleared by mood resets and are
    /// capped by the memory system; a hediff sits on the pawn until something removes it, which is
    /// what "permanent" has to mean mechanically.
    ///
    /// TWO STAGES, THE SECOND WITHOUT AN END. Fifteen days at -30, then -12 for the rest of the
    /// pawn's life. Maximus's figures. The acute stage is the part you plan around; the floor is
    /// the part you live with, and a colony that loses a bonded Avir carries that colonist at a
    /// permanent deficit from then on.
    ///
    /// WHY -12 RATHER THAN THE -30 HELD FOREVER. A permanent -30 is not grief in gameplay terms, it
    /// is a slow execution: the pawn breaks, breaks again, and never climbs out regardless of what
    /// the player builds around them. -12 is heavy enough to be felt in every mood calculation for
    /// the rest of the game and survivable with effort, which is the difference between a tragedy
    /// and a deleted character.
    ///
    /// IT DOES NOT APPLY TO THE OUTSIDER. A human who loses an Avir mate grieves the way humans do
    /// in this game. The gene is what makes the loss permanent, and they do not have it.
    /// </summary>
    public static class AvirBondGrief
    {
        /// <summary>
        /// Called when a bonded partner is lost, whatever took them.
        ///
        /// Idempotent: an Avir who has already lost someone does not stack a second grief on top.
        /// The loss of a second mate is not a case this species can have - it never takes one.
        /// </summary>
        public static void Sever(Pawn mourner, Pawn lost)
        {
            try
            {
                if (mourner == null || mourner.Dead || mourner.health == null) return;
                if (!AvirPairBond.HasBondGene(mourner)) return;
                if (AvirPairBondDefOf.Avir_BondSevered == null) return;

                if (mourner.health.hediffSet.HasHediff(AvirPairBondDefOf.Avir_BondSevered)) return;

                mourner.health.AddHediff(AvirPairBondDefOf.Avir_BondSevered);

                // The bond itself is over even though its consequences are not. Severed is not a
                // stage of being bonded - it is what is left instead of one.
                AvirBondHediff.Remove(mourner);

                Log.Message("[Avir bond] " + mourner.LabelShortCap + " lost "
                    + (lost?.LabelShortCap ?? "their bonded partner")
                    + ". The bond does not heal; it settles.");
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] grief failed: " + e, 0x4A5285);
            }
        }
    }

    /// <summary>
    /// Notices the loss. Death is the obvious case and not the only one - a partner can also be
    /// sold, banished, or lost with a caravan that never comes back, and vanilla only fires
    /// Notify_PawnDied for the first of those.
    ///
    /// Hooked at Pawn.Kill rather than at the relation removal, because the relation is what tells
    /// us WHO to mourn and it is gone by the time the removal completes.
    /// </summary>
    public static class Patch_AvirBondGrief
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)),
                    prefix: new HarmonyMethod(typeof(Patch_AvirBondGrief), nameof(Kill_Prefix)));

                Log.Message("[Avir bond] grief bound to Pawn.Kill.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir bond] could not bind grief: " + e);
            }
        }

        /// <summary>
        /// A PREFIX, and it has to be. Once Kill has run, the love-partner relations have been
        /// rewritten into ex-partner ones and there is no longer anybody to identify as the mate.
        /// The mourners are collected while the relation still exists and grieved immediately.
        /// </summary>
        public static void Kill_Prefix(Pawn __instance)
        {
            try
            {
                Pawn dying = __instance;
                if (dying?.relations == null || dying.Dead) return;

                List<Pawn> mourners = null;

                List<DirectPawnRelation> relations = dying.relations.DirectRelations;
                if (relations == null) return;

                for (int i = 0; i < relations.Count; i++)
                {
                    DirectPawnRelation rel = relations[i];
                    if (rel?.def == null || rel.otherPawn == null) continue;
                    if (!LovePartnerRelationUtility.IsLovePartnerRelation(rel.def)) continue;

                    if (!AvirPairBond.HasBondGene(rel.otherPawn)) continue;

                    if (mourners == null) mourners = new List<Pawn>();
                    mourners.Add(rel.otherPawn);
                }

                if (mourners == null) return;

                for (int i = 0; i < mourners.Count; i++)
                {
                    AvirBondGrief.Sever(mourners[i], dying);
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] kill prefix failed: " + e, 0x4A5286);
            }
        }
    }
}

namespace Avir
{
    /// <summary>
    /// The mood that the grief hediff cannot carry by itself.
    ///
    /// A hediff can move stats and capacities but not mood, so the permanence lives in the hediff -
    /// which nothing expires - and the number the player sees is read back off it here. That split
    /// is what makes this survive everything a Thought_Memory would not: the memory system's cap,
    /// mood resets, and the thirty-day timer that empties vanilla's version.
    /// </summary>
    public class ThoughtWorker_AvirBondSevered : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.health?.hediffSet == null) return ThoughtState.Inactive;
            if (AvirPairBondDefOf.Avir_BondSevered == null) return ThoughtState.Inactive;

            Hediff grief = p.health.hediffSet.GetFirstHediffOfDef(AvirPairBondDefOf.Avir_BondSevered);
            if (grief == null) return ThoughtState.Inactive;

            // Stage 0 is the acute fortnight, stage 1 the floor it never leaves. Read from the
            // hediff rather than counted again here, so the two can never disagree about which
            // phase the pawn is in.
            return ThoughtState.ActiveAtStage(grief.CurStageIndex);
        }
    }
}
