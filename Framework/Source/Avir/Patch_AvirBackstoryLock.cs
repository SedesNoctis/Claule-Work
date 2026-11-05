using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// NO PAWN THAT IS NOT AN AVIR EVER CARRIES AN AVIR HISTORY.
    ///
    /// THE LEAK, READ OUT OF THE ASSEMBLY. PawnBioAndNameGenerator.FillBackstorySlotShuffled
    /// contains a Log.Error call sitting between two reads of BackstoryDef.AllDefs - vanilla's
    /// familiar shape for "the filters produced nothing, complain, and take anything at all". Any
    /// pawnkind whose backstory filters come up empty can therefore be handed a backstory from the
    /// WHOLE database, and every Avir backstory is in that database.
    ///
    /// This is the same hole the Avian culture had, in a different wall: the mod's own defs are
    /// correctly restricted by spawnCategories, and vanilla has a documented path that ignores
    /// restrictions entirely.
    ///
    /// It is also, almost certainly, what actually produced "Viktor Einstrom, refugee of A'Shaign"
    /// with an organ-farm childhood - a case I attributed to the faction gate at the time and
    /// fixed there. That fix was correct and this is a second, independent route to the same
    /// result.
    ///
    /// A POSTFIX, NOT A PREFIX, and the reason is the fallback itself: it fires deep inside the
    /// method after the filters have already failed, so there is no argument to correct on the way
    /// in. The only reliable moment is once the slot has been filled and can be inspected.
    ///
    /// Avir are untouched: the test is genes or Avir pawnkind, and Patch_AvirBackstories does the
    /// positive half of this job for them a moment later.
    /// </summary>
    public static class Patch_AvirBackstoryLock
    {
        private static bool loggedFirst;

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(PawnBioAndNameGenerator),
                    "FillBackstorySlotShuffled");

                if (target == null)
                {
                    Log.Warning("[Avir backstorylock] FillBackstorySlotShuffled not found. A "
                        + "pawnkind whose backstory filters come up empty can still be handed an "
                        + "Avir history from vanilla's whole-database fallback.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(Patch_AvirBackstoryLock), nameof(FillSlot_Postfix)));

                Log.Message("[Avir backstorylock] bound to "
                    + "PawnBioAndNameGenerator.FillBackstorySlotShuffled.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir backstorylock] failed to apply: " + e);
            }
        }

        /// <summary>
        /// Parameters named pawn and slot, matching the Param table.
        /// </summary>
        public static void FillSlot_Postfix(Pawn pawn, BackstorySlot slot)
        {
            try
            {
                if (pawn?.story == null) return;

                // One of ours. Its history is its own.
                if (AvirFaction.ShouldUseAvirContentAtGeneration(pawn)) return;

                BackstoryDef current = slot == BackstorySlot.Childhood
                    ? pawn.story.Childhood
                    : pawn.story.Adulthood;

                if (!Patch_AvirBackstories.IsAvirBackstory(current)) return;

                BackstoryDef replacement = PickOrdinary(slot, current);
                if (replacement == null) return;

                if (slot == BackstorySlot.Childhood)
                {
                    Patch_AvirBackstories.SetChildhood(pawn, replacement);
                }
                else
                {
                    Patch_AvirBackstories.SetAdulthood(pawn, replacement);
                }

                if (!loggedFirst)
                {
                    loggedFirst = true;
                    Log.Message("[Avir backstorylock] " + (pawn.kindDef?.defName ?? "a pawn")
                        + " was handed the Avir backstory '" + current.defName
                        + "' by vanilla's empty-filter fallback despite not being an Avir; replaced "
                        + "with '" + replacement.defName + "'.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir backstorylock] postfix failed: " + e, 0x4A5277);
            }
        }

        /// <summary>
        /// Any backstory for this slot that is not ours.
        ///
        /// Reservoir sampled rather than taking the first match, so a colony full of pawns caught
        /// by this does not end up with the same childhood apiece. Deliberately not filtered by
        /// spawn category: the pawn arrived here precisely BECAUSE its category filters produced
        /// nothing, so honouring them would produce nothing again.
        /// </summary>
        private static BackstoryDef PickOrdinary(BackstorySlot slot, BackstoryDef exclude)
        {
            List<BackstoryDef> all = DefDatabase<BackstoryDef>.AllDefsListForReading;
            if (all == null) return null;

            BackstoryDef best = null;
            int seen = 0;

            for (int i = 0; i < all.Count; i++)
            {
                BackstoryDef b = all[i];
                if (b == null || b == exclude) continue;
                if (b.slot != slot) continue;
                if (Patch_AvirBackstories.IsAvirBackstory(b)) continue;

                seen++;
                if (Rand.Range(0, seen) == 0) best = b;
            }

            return best;
        }
    }
}
