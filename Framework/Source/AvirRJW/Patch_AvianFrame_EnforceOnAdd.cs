using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirRJW
{
    /// <summary>
    /// What Gene_AvirAnatomy.PostAdd used to do, now that chimeric anatomy is part of the avian
    /// frame gene. The frame's own gene class is Gene_Quadrupede (the four-legged stance), and a
    /// gene has only one class, so the anatomy is enforced from here instead: when the avian frame
    /// is added to a pawn that already lives somewhere (dev tools, xenogerm, re-added on load),
    /// the parts are put right. Pawns still being generated are handled by the add_genitals /
    /// add_breasts / sexualize_pawn patches, exactly as before - see the Character Editor note in
    /// Genes.cs for why InWorld matters.
    ///
    /// Gene_AvirAnatomy itself stays in Genes.cs: saves made before the merge still name it as the
    /// class of their Avir_Anatomy genes, and the class must exist for those to load.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GeneTracker), nameof(Pawn_GeneTracker.AddGene), new[] { typeof(GeneDef), typeof(bool) })]
    public static class Patch_AvianFrame_EnforceOnAdd
    {
        public static void Postfix(Pawn_GeneTracker __instance, GeneDef geneDef)
        {
            if (geneDef == null || AvirRjwDefOf.Avir_AvianFrame == null || geneDef != AvirRjwDefOf.Avir_AvianFrame)
            {
                return;
            }
            Pawn pawn = __instance?.pawn;
            if (!Gene_AvirAnatomy.InWorld(pawn))
            {
                return;
            }
            RjwPartEnforcer.EnforceAvir(pawn);
        }
    }
}
