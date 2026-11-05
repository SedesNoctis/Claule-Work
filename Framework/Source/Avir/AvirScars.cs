using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE AVIR DO NOT SCAR.
    ///
    /// A body that is psychic energy wearing flesh as a borrowed coat does not keep a record of
    /// every time the coat was torn. It re-forms. What is left behind on a human - the itchy bite
    /// scar, the shattered rib that aches in the cold, the permanently ruined shoulder - simply
    /// does not persist on something that is only holding its shape by intention.
    ///
    /// TWO HALVES, AND THE FIRST ONE DOES ALMOST ALL THE WORK.
    ///
    ///   1. PREVENTION, in XML. Avir_PsychicNature carries <preventPermanentWounds>true</> - a
    ///      vanilla GeneDef field, consulted in HediffComp_GetsPermanent.PreFinalizeInjury. An
    ///      injury on a carrier never becomes permanent in the first place, so from the moment
    ///      the gene is present no new scar can form at all. This is the real fix, it costs
    ///      nothing at runtime, and it needs no code.
    ///
    ///   2. CLEARING, here. Prevention cannot help with scars that already exist: ones rolled on
    ///      by PawnGenerator before any of this ran, ones already sitting on pawns in a live
    ///      save, and ones from a mod that writes a permanent injury directly rather than letting
    ///      one mature. Those are cleared on generation, on spawn, and once an hour thereafter.
    ///
    /// WHAT IS NOT TOUCHED. Missing body parts. A severed hand is not a scar - it is an absence,
    /// it is usually the direct result of something the player watched happen, and regrowing it
    /// silently would make combat consequence-free. If the Avir are ever meant to regrow limbs
    /// that is a deliberate feature with its own cost, not a side effect of tidying up scars.
    /// </summary>
    public static class AvirScars
    {
        /// <summary>
        /// Removes every permanent injury on the pawn. Returns how many went.
        ///
        /// Cheap in the common case: the loop below only allocates when there is actually
        /// something to remove, and on a healthy Avir it is one pass over the hediff list with
        /// no work done.
        /// </summary>
        public static int Clear(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return 0;

            List<Hediff> doomed = null;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                if (h == null) continue;

                // IsPermanent() is the vanilla test (HediffUtility) and reads the
                // HediffComp_GetsPermanent flag - it is true only for an injury that has
                // actually matured into a scar, not for a fresh wound still healing.
                if (!(h is Hediff_Injury)) continue;
                if (!h.IsPermanent()) continue;

                if (doomed == null) doomed = new List<Hediff>();
                doomed.Add(h);
            }

            if (doomed == null) return 0;

            for (int i = 0; i < doomed.Count; i++)
            {
                pawn.health.RemoveHediff(doomed[i]);
            }

            return doomed.Count;
        }

        /// <summary>Clear only if this pawn is actually an Avir.</summary>
        public static int ClearIfAvir(Pawn pawn)
        {
            if (!AvirRhythmUtility.IsAvir(pawn)) return 0;
            return Clear(pawn);
        }
    }

    /// <summary>
    /// Clears scars the moment a pawn spawns, whoever owns them.
    ///
    /// Unlike the warp blade corrector next door, this is NOT restricted to non-player pawns.
    /// Removing a scar takes nothing away from the player - it is a pure improvement to their
    /// colonist, it is what the gene is supposed to do anyway, and there is no version of "I
    /// wanted my Avir to keep that scar" worth writing a settings toggle for.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_AvirScarsOnSpawn
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null || __instance.Dead) return;
                AvirScars.ClearIfAvir(__instance);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Scar clearing on spawn failed: " + e, 0x5B12AC);
            }
        }
    }

    /// <summary>
    /// Clears scars at the moment of generation, before the pawn is ever seen.
    ///
    /// PawnGenerator rolls old-age injuries and combat history onto a fresh pawn, and those are
    /// written as already-permanent - the prevention flag never gets a chance to refuse them
    /// because nothing ever matured. This catches them at the source, so an Avir hunter is never
    /// briefly generated with a scar that a later sweep then has to notice and undo.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirScarsOnGenerate
    {
        public static void Postfix(Pawn __result)
        {
            try
            {
                if (__result == null || __result.health == null) return;
                AvirScars.ClearIfAvir(__result);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Scar clearing on generation failed: " + e, 0x5B12AD);
            }
        }
    }
}
