using HarmonyLib;
using Verse;

namespace AvirColors
{
    /// <summary>
    /// Compatibility fix for FemaleBodyFurVariants (bundled in tiagocc0.FemaleBodyVariants'
    /// 1.6 folder as FemaleBodyFurVariants.dll).
    ///
    /// Anthrosonae's own tinting patch (PawnRenderNode_Fur_GraphicFor_Patch, priority
    /// int.MinValue) only applies gene colors if:
    ///     __result.path == pawn.story.furDef.GetFurBodyGraphicPath(pawn)
    /// FemaleBodyFurVariants patches FurDef.GetFurBodyGraphicPath itself to redirect
    /// female thin/fat/hulk pawns to a different texture path. That breaks the string
    /// match above for those pawns, so Anthrosonae's tint step silently no-ops - no
    /// exception, just wrong/default colors.
    ///
    /// This patch applies the tint unconditionally for any PawnRenderNode_Fur whose
    /// gene is a FurGene, without relying on that path comparison. It runs at normal
    /// priority, which executes before Anthrosonae's int.MinValue postfix, so our tint
    /// lands first; their own check then runs harmlessly on an already-tinted result.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderNode_Fur), "GraphicFor")]
    public static class PawnRenderNode_Fur_GraphicFor_CompatPatch
    {
        public static void Postfix(PawnRenderNode_Fur __instance, Pawn pawn, ref Graphic __result)
        {
            if (__result != null
                && __instance.gene is Anthrosonae.FurGene furGene
                && pawn.Drawer.renderer.CurRotDrawMode != RotDrawMode.Dessicated)
            {
                __result = furGene.GetGraphicOverriden(__result, __instance, pawn);
            }
        }
    }
}
