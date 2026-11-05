using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Unlike Predator/Strider, neither end of this is guaranteed - both are independent rolls,
    /// PriestessChance just being a lot higher than RareChance. Same GeneratePawn timing as the
    /// other post-process patches (genes have to actually be assigned before we can check
    /// Fearborn).
    ///
    /// Mutually exclusive with Avir_ShroudPriestess - checked explicitly here (not just via
    /// conflictingTraits, which vanilla only enforces during its own trait roll, not for a direct
    /// GainTrait() call like this). Both this patch and Patch_ShroudPriestess_PostProcess check
    /// for each other, since Harmony doesn't guarantee which of two postfixes on the same method
    /// runs first.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    public static class Patch_BloodMage_PostProcess
    {
        // Tune freely.
        private const float PriestessChance = 0.15f;
        private const float RareChance = 0.01f;

        private static TraitDef bloodMageDef;
        private static TraitDef shroudPriestessDef;
        private static PawnKindDef avirPriestessKind;
        private static GeneDef fearbornGeneDef;
        private static bool lookedUp;

        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || pawn.story == null || pawn.story.traits == null) return;

                EnsureLookedUp();
                if (bloodMageDef == null) return;

                bool hasFearborn = fearbornGeneDef != null
                                    && pawn.genes != null
                                    && pawn.genes.HasActiveGene(fearbornGeneDef);

                bool hasTrait = pawn.story.traits.HasTrait(bloodMageDef);
                bool hasShroudPriestess = shroudPriestessDef != null && pawn.story.traits.HasTrait(shroudPriestessDef);

                // Cleanup: the uniform Avir trait pool has no concept of "rare" - anything it
                // granted doesn't count, this patch decides for itself below. Also strips if the
                // pawn somehow ended up with both mutually-exclusive traits.
                if (hasTrait)
                {
                    Trait trait = pawn.story.traits.allTraits.Find(t => t.def == bloodMageDef);
                    if (trait != null) pawn.story.traits.RemoveTrait(trait);
                    hasTrait = false;
                }

                if (!hasTrait && hasFearborn && !hasShroudPriestess)
                {
                    bool isPriestess = avirPriestessKind != null && pawn.kindDef == avirPriestessKind;
                    float chance = isPriestess ? PriestessChance : RareChance;

                    if (Rand.Chance(chance))
                    {
                        pawn.story.traits.GainTrait(new Trait(bloodMageDef, 0, true));
                    }
                }

                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Blood Mage post-process failed: " + e, 0x4A51BA);
            }
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            bloodMageDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_BloodMage");
            shroudPriestessDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_ShroudPriestess");
            avirPriestessKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Priestess");
            fearbornGeneDef = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
        }
    }
}
