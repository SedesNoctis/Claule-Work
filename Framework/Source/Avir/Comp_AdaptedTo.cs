using Verse;

namespace Avir
{
    /// <summary>
    /// Attach to any Apparel ThingDef to make it bond to the first pawn who wears it - after
    /// that, no OTHER pawn can wear it at all, only that one, until/unless it's released.
    ///
    /// This mirrors vanilla's own "biocoded" concept (the game already has a whole storage-filter
    /// category for it) rather than being a new idea, but is a small dedicated comp instead of
    /// vanilla's own CompBiocodable - full control over the label/messaging ("Adapted to", not
    /// "Biocoded"), without needing to verify whether vanilla's own UI text for that comp can be
    /// re-labelled at all.
    ///
    /// The actual enforcement (auto-bind on first wear, refuse anyone else) lives in
    /// Patch_AvirAdaptedTo.cs, hooking Pawn_ApparelTracker.Wear directly - this class is just the
    /// data holder.
    /// </summary>
    public class CompProperties_AdaptedTo : CompProperties
    {
        public CompProperties_AdaptedTo()
        {
            compClass = typeof(Comp_AdaptedTo);
        }
    }

    public class Comp_AdaptedTo : ThingComp
    {
        public Pawn adaptedPawn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref adaptedPawn, "avirAdaptedPawn");
        }

        public override string CompInspectStringExtra()
        {
            return adaptedPawn != null
                ? "Avir_AdaptedToInspect".Translate(adaptedPawn.Named("PAWN"))
                : "Avir_AdaptedToInspectUnbound".Translate();
        }
    }
}
