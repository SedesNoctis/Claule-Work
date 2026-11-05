using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Forces Avir_DazzlingCall onto every pawn generated as the Avir_Predator PawnKindDef,
    /// unconditionally - on top of whatever Patch_AvirTraits (core Avir assembly) already rolled
    /// for them. Everyone else still only gets it through that normal 1-2 trait roll, since
    /// Avir_DazzlingCall is a plain commonality-0 Avir_* trait and Patch_AvirTraits sweeps up
    /// every trait matching that pattern automatically.
    ///
    /// Deliberately a second, independent Harmony patch on the same method rather than an edit
    /// to Patch_AvirTraits.cs in the core assembly - this whole file only exists when Hauts'
    /// Framework is active (see LoadFolders.xml), so it can't live in an assembly that's always
    /// loaded.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GenerateTraits")]
    public static class Patch_DazzlingCall_ForcePredator
    {
        private static TraitDef dazzlingCallDef;
        private static PawnKindDef avirPredatorKind;
        private static bool lookedUp;

        public static void Postfix(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.story == null || pawn.story.traits == null) return;

                EnsureLookedUp();
                if (dazzlingCallDef == null || avirPredatorKind == null) return;
                if (pawn.kindDef != avirPredatorKind) return;
                if (pawn.story.traits.HasTrait(dazzlingCallDef)) return;

                pawn.story.traits.GainTrait(new Trait(dazzlingCallDef, 0, true));
                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Dazzling Call forced-grant failed: " + e, 0x4A51B5);
            }
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            dazzlingCallDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_DazzlingCall");
            avirPredatorKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Predator");
        }
    }
}
