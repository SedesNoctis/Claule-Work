using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Same shape as Patch_BloodMage_PostProcess - chance-based on both ends, not guaranteed
    /// like Predator/Strider. Gated on the Crow ThingDef existing at all (Odyssey DLC), same
    /// principle as Cinderfletched requiring CE - never grants a trait whose whole ability is a
    /// dead end.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    public static class Patch_EyeOfTheCrow_PostProcess
    {
        private const float PriestessChance = 0.15f;
        private const float RareChance = 0.01f;

        private static TraitDef eyeOfTheCrowDef;
        private static PawnKindDef avirPriestessKind;
        private static GeneDef fearbornGeneDef;
        private static ThingDef crowDef;
        private static bool lookedUp;

        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || pawn.story == null || pawn.story.traits == null) return;

                EnsureLookedUp();
                if (eyeOfTheCrowDef == null) return;

                bool odysseyActive = crowDef != null;

                bool hasFearborn = fearbornGeneDef != null
                                    && pawn.genes != null
                                    && pawn.genes.HasActiveGene(fearbornGeneDef);

                bool hasTrait = pawn.story.traits.HasTrait(eyeOfTheCrowDef);

                if (hasTrait && (!hasFearborn || !odysseyActive))
                {
                    Trait trait = pawn.story.traits.allTraits.Find(t => t.def == eyeOfTheCrowDef);
                    if (trait != null) pawn.story.traits.RemoveTrait(trait);
                    hasTrait = false;
                }

                if (!hasTrait && hasFearborn && odysseyActive)
                {
                    bool isPriestess = avirPriestessKind != null && pawn.kindDef == avirPriestessKind;
                    float chance = isPriestess ? PriestessChance : RareChance;

                    if (Rand.Chance(chance))
                    {
                        pawn.story.traits.GainTrait(new Trait(eyeOfTheCrowDef, 0, true));
                    }
                }

                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Eye of the Crow post-process failed: " + e, 0x4A51C1);
            }
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            eyeOfTheCrowDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_EyeOfTheCrow");
            avirPriestessKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Priestess");
            fearbornGeneDef = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
            crowDef = DefDatabase<ThingDef>.GetNamedSilentFail("Crow");
        }
    }
}
