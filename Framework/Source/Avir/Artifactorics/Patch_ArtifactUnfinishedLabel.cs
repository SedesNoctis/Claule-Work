using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>
    /// UNFINISHED ARTIFACT LABEL. Artifact recipes have no vanilla products (the recipe worker makes
    /// the item itself), but vanilla UnfinishedThing builds its label and description from
    /// Recipe.products[0]. With an empty list that throws (label, flavour and detailed description), and the inspect pane dies on the label:
    /// no name, no "work left". These prefixes answer for artifact recipes before vanilla gets there.
    /// They return at once for every other unfinished thing.
    /// </summary>
    internal static class ArtifactUnfinishedText
    {
        public static ArtifactRecipeExtension Ext(UnfinishedThing t)
        {
            RecipeDef r = t.Recipe;
            if (r == null || !r.products.NullOrEmpty()) return null;
            return r.GetModExtension<ArtifactRecipeExtension>();
        }

        public static string Label(UnfinishedThing t, ArtifactRecipeExtension ext)
        {
            string metal = ext.metal?.label;
            if (metal.NullOrEmpty()) return t.def.label;
            switch (ext.step)
            {
                case ArtifactCraftStep.Heart: return t.def.label + " (" + metal + " heart)";
                case ArtifactCraftStep.Necklace: return "unfinished " + metal + " necklace";
                case ArtifactCraftStep.AmuletCore: return t.def.label + " (" + metal + " core)";
                default: return "unfinished " + metal + "-rimmed setting";
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_ArtifactUnfinishedLabel
    {
        // Only UnfinishedThing's own override: never the base Thing getter every item shares.
        static MethodBase Target() => AccessTools.DeclaredPropertyGetter(typeof(UnfinishedThing), "LabelNoCount");
        static bool Prepare() => Target() != null;
        static MethodBase TargetMethod() => Target();

        public static bool Prefix(UnfinishedThing __instance, ref string __result)
        {
            ArtifactRecipeExtension ext = ArtifactUnfinishedText.Ext(__instance);
            if (ext == null) return true;
            __result = ArtifactUnfinishedText.Label(__instance, ext);
            return false;
        }
    }

    [HarmonyPatch]
    public static class Patch_ArtifactUnfinishedDescription
    {
        // Only UnfinishedThing's own override: never the base Thing getter every item shares.
        static MethodBase Target() => AccessTools.DeclaredPropertyGetter(typeof(UnfinishedThing), "DescriptionFlavor");
        static bool Prepare() => Target() != null;
        static MethodBase TargetMethod() => Target();

        public static bool Prefix(UnfinishedThing __instance, ref string __result)
        {
            ArtifactRecipeExtension ext = ArtifactUnfinishedText.Ext(__instance);
            if (ext == null) return true;
            __result = __instance.def.description;
            return false;
        }
    }

    [HarmonyPatch]
    public static class Patch_ArtifactUnfinishedDescriptionDetailed
    {
        // Only UnfinishedThing's own override: never the base Thing getter every item shares.
        static MethodBase Target() => AccessTools.DeclaredPropertyGetter(typeof(UnfinishedThing), "DescriptionDetailed");
        static bool Prepare() => Target() != null;
        static MethodBase TargetMethod() => Target();

        public static bool Prefix(UnfinishedThing __instance, ref string __result)
        {
            ArtifactRecipeExtension ext = ArtifactUnfinishedText.Ext(__instance);
            if (ext == null) return true;
            __result = __instance.def.description;
            return false;
        }
    }
}
