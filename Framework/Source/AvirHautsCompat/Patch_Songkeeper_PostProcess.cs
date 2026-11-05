using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// 30% on Avir_Artist, 1% on any other Avir. Same GeneratePawn timing as every other
    /// post-process patch in this folder - genes have to actually be assigned before Fearborn
    /// can be checked, which rules out the earlier generation hooks.
    ///
    /// FEARBORN IS A HARD REQUIREMENT ON BOTH PATHS, and not for flavour. Both hymns spend Arcane
    /// Hunger through Gene_ArcaneHunger, so a non-Fearborn Songkeeper would carry two gizmos that
    /// can never be pressed - HasAtLeast returns false with no gene, Valid fails, and the player
    /// gets a permanently greyed-out button with no explanation of why.
    ///
    /// THE CLEANUP BLOCK IS NOT REDUNDANT. The shared Avir trait pool has no concept of "restricted
    /// to one caste" - it will hand this trait to ordinary Avir on its own schedule. Anything it
    /// granted is stripped here first, and then this patch makes the only decision that counts.
    /// Without that, the effective rate on non-Artists would be the pool's rate plus 1%.
    ///
    /// NO MUTUAL EXCLUSION with Blood Mage or Shroud Priestess - deliberate for now, see the trait
    /// file. If it should be added later it goes here as a paired check on both patches, because
    /// Harmony does not guarantee which of two postfixes on the same method runs first, and each
    /// side has to be able to see the other's work.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    public static class Patch_Songkeeper_PostProcess
    {
        // Tune freely.
        private const float ArtistChance = 0.30f;
        private const float RareChance = 0.01f;

        private static TraitDef songkeeperDef;
        private static PawnKindDef avirArtistKind;
        private static GeneDef fearbornGeneDef;
        private static bool lookedUp;

        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || pawn.story == null || pawn.story.traits == null) return;

                EnsureLookedUp();
                if (songkeeperDef == null) return;

                bool hasFearborn = fearbornGeneDef != null
                                    && pawn.genes != null
                                    && pawn.genes.HasActiveGene(fearbornGeneDef);

                bool hasTrait = pawn.story.traits.HasTrait(songkeeperDef);

                if (hasTrait)
                {
                    Trait trait = pawn.story.traits.allTraits.Find(t => t.def == songkeeperDef);
                    if (trait != null) pawn.story.traits.RemoveTrait(trait);
                    hasTrait = false;
                }

                if (!hasTrait && hasFearborn)
                {
                    bool isArtist = avirArtistKind != null && pawn.kindDef == avirArtistKind;
                    float chance = isArtist ? ArtistChance : RareChance;

                    if (Rand.Chance(chance))
                    {
                        pawn.story.traits.GainTrait(new Trait(songkeeperDef, 0, true));
                    }
                }

                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Songkeeper post-process failed: " + e, 0x4A51CB);
            }
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            songkeeperDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Songkeeper");
            avirArtistKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Artist");
            fearbornGeneDef = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
        }
    }
}
