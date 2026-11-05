using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Default-deny gate on trait acquisition. A TraitDef tagged with a
    /// TraitCategoryExtension.category becomes unobtainable by every pawn in the game unless
    /// that pawn's PawnKindDef carries a PawnKindTraitCategoryExtension listing the same
    /// category. Untagged traits are never touched - they roll exactly as vanilla always has.
    ///
    /// First use: the two High Artificer lineages. Architect-only traits are tagged
    /// "HighArtificierArch", Artist-only traits "HighArtificierArt" - see
    /// Traits_Avir_HighArtificier.xml. Nothing stops a later pawnkind listing BOTH categories
    /// if a trait pool ever needs to be shared, or a third category being introduced the same
    /// way; nothing here is specific to these two beyond what's declared in the defs.
    ///
    /// See Avir_TraitCategoryRestriction.md for the full design rationale.
    /// </summary>
    public class TraitCategoryExtension : DefModExtension
    {
        public string category;
    }

    public class PawnKindTraitCategoryExtension : DefModExtension
    {
        public List<string> allowedCategories;
    }

    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.GainTrait))]
    public static class Patch_Avir_TraitCategoryRestriction
    {
        public static bool Prefix(Trait trait, Pawn ___pawn)
        {
            TraitCategoryExtension traitExt = trait.def.GetModExtension<TraitCategoryExtension>();
            if (traitExt == null) return true; // untagged trait - not part of this system

            PawnKindTraitCategoryExtension kindExt =
                ___pawn?.kindDef?.GetModExtension<PawnKindTraitCategoryExtension>();

            // Default: blocked. Only a pawnkind carrying a matching allowedCategories entry
            // gets through. NOTE: vanilla's random trait roll does not retry on rejection - a
            // vetoed roll during generation is a lost slot, not a replaced one. Cosmetic, not
            // functional; an occasional pawn of a restricted kind may end up with one fewer
            // trait than usual. forcedTraits on a PawnKindDef bypasses this patch entirely,
            // which is fine - that's still the right vanilla tool for a guaranteed trait.
            return kindExt != null && kindExt.allowedCategories != null
                && kindExt.allowedCategories.Contains(traitExt.category);
        }
    }
}
