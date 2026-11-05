using HarmonyLib;
using Verse;

namespace Avir
{
    /// <summary>
    /// DO NOT LET VANILLA INVENT RELATIVES FOR AVIR. This is the root fix; everything else is a net.
    ///
    /// THE REAL CAUSE (the original comment on this file blamed empty caste backstory pools - that
    /// was wrong, and chasing it cost days):
    ///
    ///   To give a pawn a sibling, RimWorld GENERATES THE SHARED PARENTS, reusing the child's own
    ///   kindDef and demanding a gender:
    ///
    ///       GenerateParent(..., existingChild.kindDef, ..., fixedGender: Male)   <- the father
    ///
    ///   So it must be able to build a MALE Avir_Huntress to father a huntress. Anything that forces
    ///   the caste's gender unconditionally - PawnKindDef.fixedGender, or a postfix rewriting
    ///   pawn.gender - hands back the WRONG gender, and the game then does:
    ///
    ///       SetFather(<a female>)   ->  null  ->  NullReferenceException
    ///       SetMother(<a male>)     ->  null  ->  NullReferenceException
    ///
    ///   "Tried to set Sel-Cielrie with gender Female as Valcrissria's father."
    ///
    ///   and the exception aborts THE ENTIRE PAWN GROUP, not just the relation. The raid is
    ///   cancelled. Bigger raids roll more relations, so the HIGH-VALUE raids died most.
    ///
    /// THE FIX HERE: skip vanilla's relation generation for Avir pawns entirely. No invented
    /// siblings means no invented parents means nothing to get the gender wrong. VEF ships exactly
    /// this guard for its own gene-locked xenotypes (VanillaExpandedFramework_PawnGenerator_
    /// GeneratePawnRelations_Patch.DisableRelations) - it simply does not know about Avir.
    ///
    /// This only stops NEW relatives being fabricated during generation. Relations a pawn already
    /// has - quests, hand-placed pawns, existing family, colony births - are untouched.
    ///
    /// COMPILE NOTE: PawnGenerator.GeneratePawnRelations is PRIVATE. That is why
    ///
    ///     [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawnRelations))]
    ///
    /// failed with "'PawnGenerator' does not contain a definition for 'GeneratePawnRelations'" -
    /// nameof() can only see members C# is allowed to see, and a private one is not. Harmony has no
    /// such restriction: it finds the method by reflection. Name it with a plain STRING and the
    /// patch attaches fine. Do not "fix" this back into nameof().
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawnRelations")]
    public static class Patch_AvirDisableRelationGeneration
    {
        public static bool Prefix(Pawn pawn, ref PawnGenerationRequest request)
        {
            if (pawn == null) return true;

            if (AvirFaction.ShouldUseAvirContentAtGeneration(pawn))
                return false; // skip vanilla's relation generation entirely for this pawn

            return true;
        }
    }
}
