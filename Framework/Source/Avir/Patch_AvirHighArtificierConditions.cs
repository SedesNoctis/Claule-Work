using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Three of the High Artificer traits (Avir_HA_SleeplessGenius, Avir_HA_FragileMuse,
    /// Avir_HA_CompulsiveMaker) carry a genuinely permanent condition on top of their stat/mood
    /// effects - see Hediffs_Avir_HighArtificier.xml and
    /// Hediffs_Avir_HighArtificier_Restlessness.xml. This grants the matching hediff the moment
    /// the trait is actually gained.
    ///
    /// Runs as a plain postfix on TraitSet.GainTrait, alongside (but independent of)
    /// Patch_Avir_TraitCategoryRestriction's prefix on the same method. Harmony postfixes run
    /// regardless of whether a prefix vetoed the original call, so this checks HasTrait after
    /// the fact rather than trusting that GainTrait having been CALLED means it succeeded - a
    /// blocked roll (wrong pawnkind, category mismatch) never actually adds the trait, so
    /// HasTrait comes back false and this postfix is a no-op for it. Safe to fire twice; it
    /// won't duplicate the hediff.
    /// </summary>
    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.GainTrait))]
    public static class Patch_AvirHighArtificierConditions
    {
        public static void Postfix(TraitSet __instance, Trait trait, Pawn ___pawn)
        {
            try
            {
                if (trait?.def == null || ___pawn == null) return;

                string defName = trait.def.defName;
                string hediffDefName;

                if (defName == "Avir_HA_SleeplessGenius") hediffDefName = "Avir_ChronicOverexertion";
                else if (defName == "Avir_HA_FragileMuse") hediffDefName = "Avir_ChronicMelancholy";
                else if (defName == "Avir_HA_CompulsiveMaker") hediffDefName = "Avir_Restlessness";
                else return;

                // The trait roll may have been vetoed by the category restriction (wrong
                // pawnkind) - if so, it was never actually added, and there is nothing to grant.
                if (!___pawn.story.traits.HasTrait(trait.def)) return;

                HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
                if (hediffDef == null) return;

                Pawn pawn = ___pawn;
                if (pawn.health?.hediffSet == null) return;
                if (pawn.health.hediffSet.HasHediff(hediffDef)) return; // already has it

                Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn);
                pawn.health.AddHediff(hediff);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] High Artificer condition grant failed: " + e, 0x4A51D2);
            }
        }
    }
}
