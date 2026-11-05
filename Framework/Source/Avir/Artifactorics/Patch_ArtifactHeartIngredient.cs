using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>
    /// RIM AND HEART NEVER SHARE A METAL (decided Oct 2, 2026). The creation panel already locks
    /// the rim's own metal in the heart list, but a heart order can also be added from the vanilla
    /// bills tab - and those are plain bills. This postfix on the base ingredient check makes every
    /// heart recipe, from any source, refuse a setting whose rim is its own heart metal, and refuse
    /// anything that already has a heart. It returns at once for every non-artifact bill.
    /// </summary>
    [HarmonyPatch(typeof(Bill), nameof(Bill.IsFixedOrAllowedIngredient), new[] { typeof(Thing) })]
    public static class Patch_ArtifactHeartIngredient
    {
        public static void Postfix(Bill __instance, Thing __0, ref bool __result)
        {
            if (!__result || __0 == null) return;
            if (__0.def != ArtifactoricsDefOf.Avir_ArtifactSetting) return;
            ArtifactRecipeExtension ext = __instance.recipe?.GetModExtension<ArtifactRecipeExtension>();
            if (ext == null || ext.step != ArtifactCraftStep.Heart) return;
            CompArtifact c = __0.TryGetComp<CompArtifact>();
            if (c == null) return;
            if (c.HasHeart || c.rimMetal == ext.metal) __result = false;
        }
    }
}
