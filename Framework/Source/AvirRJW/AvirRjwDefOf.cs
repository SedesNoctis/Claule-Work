using RimWorld;
using Verse;

namespace AvirRJW
{
    [DefOf]
    public static class AvirRjwDefOf
    {
        [MayRequire("rim.job.world")] public static GeneDef Avir_AvianFrame;   // chimeric anatomy now lives in avian frame

        static AvirRjwDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirRjwDefOf));
        }
    }
}
