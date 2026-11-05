using RimWorld;
using Verse;

namespace Avir
{
    [DefOf]
    public static class FeatheredFurDefOf
    {
        public static GeneDef Avir_FeatheredFur;
        public static HediffDef Avir_FeatheredFurShed;
        public static ThingDef Leather_AvirFeatheredFur;

        // vanilla things we replace. Meat is NOT replaced - an Avir butchers into
        // human meat like anything else humanlike. Only the leather changes.
        public static ThingDef Leather_Human;

        // MO-only (may be null without Medieval Overhaul)
        [MayRequire("DankPyon.Medieval.Overhaul")] public static ThingDef Avir_ChimeraHide;
        [MayRequire("DankPyon.Medieval.Overhaul")] public static ThingDef DankPyon_Leather_Rawhide;

        static FeatheredFurDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FeatheredFurDefOf));
        }
    }
}
