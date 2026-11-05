using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Was a postfix on PawnGenerator.GenerateTraits - moved to PawnGenerator.GeneratePawn
    /// instead. GenerateTraits only guarantees traits have been rolled at that point, not that
    /// genes have been assigned yet; checking pawn.genes.HasActiveGene(...) that early can read
    /// as "no genes at all" regardless of what the pawn ends up with once generation finishes.
    /// GeneratePawn is the outer method - by the time it returns, everything (traits, genes,
    /// backstory, gear) is done, so this is the safe point to make trait decisions that depend on
    /// genes.
    ///
    /// Same three jobs as before, just running at the right time now:
    /// 1. CLEANUP: strip Avir_Cinderfletched from anyone who got it via the normal uniform trait
    ///    roll, or who has it while CE is inactive.
    /// 2. FORCE-GRANT: guaranteed on a Fearborn Avir_Strider.
    /// 3. RARE ROLL: tiny independent chance on any other Fearborn pawn.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    public static class Patch_Cinderfletched_PostProcess
    {
        private const float RareChance = 0.005f;

        private static TraitDef cinderfletchedDef;
        private static PawnKindDef avirStriderKind;
        private static GeneDef fearbornGeneDef;
        private static bool lookedUp;

        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || pawn.story == null || pawn.story.traits == null) return;

                EnsureLookedUp();
                if (cinderfletchedDef == null) return;

                bool ceActive = ModsConfig.IsActive("CETeam.CombatExtended");

                bool hasFearborn = fearbornGeneDef != null
                                    && pawn.genes != null
                                    && pawn.genes.HasActiveGene(fearbornGeneDef);

                bool hasTrait = pawn.story.traits.HasTrait(cinderfletchedDef);

                if (hasTrait && (!hasFearborn || !ceActive))
                {
                    Trait trait = pawn.story.traits.allTraits.Find(t => t.def == cinderfletchedDef);
                    if (trait != null) pawn.story.traits.RemoveTrait(trait);
                    hasTrait = false;
                }

                if (!hasTrait && hasFearborn && ceActive)
                {
                    bool isStrider = avirStriderKind != null && pawn.kindDef == avirStriderKind;

                    if (isStrider || Rand.Chance(RareChance))
                    {
                        pawn.story.traits.GainTrait(new Trait(cinderfletchedDef, 0, true));
                    }
                }

                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Cinderfletched post-process failed: " + e, 0x4A51B9);
            }
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            cinderfletchedDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Cinderfletched");
            avirStriderKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Strider");
            fearbornGeneDef = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
        }
    }
}
