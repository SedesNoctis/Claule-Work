using RimWorld;
using Verse;

namespace FlightGene
{
    public class CompProperties_AbilityToggleFlight : CompProperties_AbilityEffect
    {
        // The hediff that represents "in flight". Set in XML.
        public HediffDef hediffDef;

        // Refuse to take off if rest is at or below this fraction (0..1).
        public float restThreshold = 0.05f;

        public CompProperties_AbilityToggleFlight()
        {
            compClass = typeof(CompAbilityEffect_ToggleFlight);
        }
    }
}
