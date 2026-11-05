using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Was a postfix on PawnGenerator.GenerateTraits - moved to PawnGenerator.GeneratePawn for
    /// the same reason as Patch_Cinderfletched_PostProcess: GenerateTraits doesn't guarantee
    /// genes are assigned yet, so pawn.genes.HasActiveGene(...) could read empty regardless of
    /// what the pawn actually ends up with. GeneratePawn is the outer method, so by the time this
    /// postfix runs, generation is fully finished.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    public static class Patch_DazzlingCall_PostProcess
    {
        private static TraitDef dazzlingCallDef;
        private static PawnKindDef avirPredatorKind;
        private static GeneDef fearbornGeneDef;
        private static bool lookedUp;

        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || pawn.story == null || pawn.story.traits == null) return;

                EnsureLookedUp();
                if (dazzlingCallDef == null || avirPredatorKind == null) return;

                bool hasFearborn = fearbornGeneDef != null
                                    && pawn.genes != null
                                    && pawn.genes.HasActiveGene(fearbornGeneDef);

                bool hasTrait = pawn.story.traits.HasTrait(dazzlingCallDef);

                if (hasTrait && !hasFearborn)
                {
                    Trait trait = pawn.story.traits.allTraits.Find(t => t.def == dazzlingCallDef);
                    if (trait != null) pawn.story.traits.RemoveTrait(trait);
                    hasTrait = false;
                }

                if (!hasTrait && hasFearborn && pawn.kindDef == avirPredatorKind)
                {
                    pawn.story.traits.GainTrait(new Trait(dazzlingCallDef, 0, true));
                }

                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Dazzling Call post-process failed: " + e, 0x4A51B7);
            }
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            dazzlingCallDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_DazzlingCall");
            avirPredatorKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Predator");
            fearbornGeneDef = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
        }
    }
}
