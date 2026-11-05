using RimWorld;
using Verse;

namespace FlightGene
{
    [DefOf]
    public static class FlightDefOf
    {
        public static HediffDef Flight_Hediff;
        public static AbilityDef Flight_Ability;

        static FlightDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FlightDefOf));
        }
    }
}
