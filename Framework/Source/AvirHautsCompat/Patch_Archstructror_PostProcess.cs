using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Simpler than Patch_BloodMage_PostProcess/Patch_EyeOfTheCrow_PostProcess - only one gate
    /// here, not two. Those roll on a Fearborn gene check with a Priestess-vs-rare split;
    /// Archstructror is gated purely on PawnKindDef == Avir_Architect, since that kind is
    /// already the intended pool by design (both Avir_HighNesting and AShialis field the same
    /// Avir_Architect PawnKindDef - see Faction_AShialis.xml's pawnGroupMakers - so this covers
    /// both factions automatically without checking faction at all).
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    public static class Patch_Archstructror_PostProcess
    {
        private const float ArchitectChance = 0.15f;

        private static TraitDef archstructrorDef;
        private static PawnKindDef architectKind;
        private static bool lookedUp;

        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || pawn.story == null || pawn.story.traits == null) return;

                EnsureLookedUp();
                if (archstructrorDef == null || architectKind == null) return;

                bool isArchitect = pawn.kindDef == architectKind;
                bool hasTrait = pawn.story.traits.HasTrait(archstructrorDef);

                // Cleanup: the uniform Avir trait pool has no concept of "restricted to one
                // kind" - if it somehow granted this anyway, or the pawn's kind changed
                // underneath it, strip it here rather than trust anything upstream.
                if (hasTrait && !isArchitect)
                {
                    Trait trait = pawn.story.traits.allTraits.Find(t => t.def == archstructrorDef);
                    if (trait != null) pawn.story.traits.RemoveTrait(trait);
                    hasTrait = false;
                }

                if (!hasTrait && isArchitect && Rand.Chance(ArchitectChance))
                {
                    pawn.story.traits.GainTrait(new Trait(archstructrorDef, 0, true));
                }

                pawn.Notify_DisabledWorkTypesChanged();
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Archstructror post-process failed: " + e, 0x4A51E7);
            }
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            archstructrorDef = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Archstructror");
            architectKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Architect");
        }
    }
}
