using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Same chance-based shape as Patch_BloodMage_PostProcess (moderate on Priestess, tiny on
    /// other Fearborn), plus an explicit Avir_BloodMage check - conflictingTraits on the TraitDef
    /// only gets enforced by vanilla's own trait-roll pipeline, not by a direct GainTrait() call
    /// like these post-process patches make, so exclusion has to be checked here in code.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    public static class Patch_ShroudPriestess_PostProcess
    {
        private const float PriestessChance = 0.15f;
        private const float RareChance = 0.01f;

        private static TraitDef shroudPriestessDef;
        private static TraitDef bloodMageDef;
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
                if (shroudPriestessDef == null) return;

                bool hasFearborn = fearbornGeneDef != null
                                    && pawn.genes != null
                                    && pawn.genes.HasActiveGene(fearbornGeneDef);

                bool hasTrait = pawn.story.traits.HasTrait(shroudPriestessDef);
                bool hasBloodMage = bloodMageDef != null && pawn.story.traits.HasTrait(bloodMageDef);

                // Cleanup: uniform pool grant, or ended up with both mutually-exclusive traits.
                if (hasTrait && (!hasFearborn || hasBloodMage))
                {
                    Trait trait = pawn.story.traits.allTraits.Find(t => t.def == shroudPriestessDef);
                    if (trait != null) pawn.story.traits.RemoveTrait(trait);
                    hasTrait = false;
                }

                if (!hasTrait && hasFearborn && !hasBloodMage)
                {
                    bool isPriestess = avirPriestessKind != null && pawn.kindDef == avirPriestessKind;
                    float chance = isPriestess ? PriestessChance : RareChance;

                    if (Rand.Chance(chance))
                    {
                        pawn.story.traits.GainTrait(new Trait(shroudPriestessDef, 0, true));
                    }
                }

                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Shroud Priestess post-process failed: " + e, 0x4A51C4);
            }
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            shroudPriestessDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_ShroudPriestess");
            bloodMageDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_BloodMage");
            avirPriestessKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Priestess");
            fearbornGeneDef = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
        }
    }
}
