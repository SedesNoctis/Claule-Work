using RimWorld;
using Verse;

namespace Avir
{
    [DefOf]
    public static class EssentiumClawDefOf
    {
        public static HediffDef Avir_EssentiumClaws;
        public static HediffDef Avir_EssentiumTalons;
        public static BodyPartDef Hand;
        public static BodyPartDef Foot;

        static EssentiumClawDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(EssentiumClawDefOf));
        }
    }
}
