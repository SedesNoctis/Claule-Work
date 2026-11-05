using RimWorld;
using Verse;

namespace Avir
{
    [DefOf]
    public static class EssentiumBeakDefOf
    {
        public static HediffDef Avir_EssentiumBeakConditioning;
        public static HediffDef Avir_EssentiumBeak_1;
        public static HediffDef Avir_EssentiumBeak_2;
        public static HediffDef Avir_EssentiumBeak_3;
        public static BodyPartDef Jaw;

        static EssentiumBeakDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(EssentiumBeakDefOf));
        }
    }
}
