using Verse;

namespace FlightGene
{
    public class HediffCompProperties_FlightMonitor : HediffCompProperties
    {
        // Auto-disable flight when rest drops to/below this fraction (0..1).
        public float restThreshold = 0.05f;

        public HediffCompProperties_FlightMonitor()
        {
            compClass = typeof(HediffComp_FlightMonitor);
        }
    }
}
