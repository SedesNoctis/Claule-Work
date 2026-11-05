using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// INTEGRATION: Fantasy Biotech (vr.fantasy.biotech). Self-disabling.
    ///
    /// Gives the two High Artificier lineages - Architect and Artist - a dedicated torso
    /// suit instead of the placeholder Elves wardrobe (Tunic/Gambeson/CloakHigh) they used
    /// to borrow from the ordinary Artificer caste while no real look existed for them.
    ///
    ///   Architect   artificelord suit (VR_Apparel_ArtificerSuit_Lord)
    ///   Artist      artificer suit     (VR_Apparel_ArtificerSuit_Basic)
    ///
    /// NO HEADGEAR AT ALL, either caste - that is the whole point of this pass. Neither Dress
    /// nor anything downstream ever puts anything on a High Artificier's head.
    ///
    /// Stuff follows the same rule as every other Avir piece: Essentium-adjacent leather/cloth,
    /// never the war-metal. The suit's stuffCategories are Leathery/Fabric, so it takes the
    /// Avir featheredfur or Cloudcloth, whichever the mod finds first.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirFantasyBiotechGear
    {
        public static readonly bool Active;

        private static ThingDef ArchitectSuit; // Lord
        private static ThingDef ArtistSuit;    // Basic

        private static ThingDef FeatheredFur, Cloudcloth;

        static AvirFantasyBiotechGear()
        {
            ArchitectSuit = Find("VR_Apparel_ArtificerSuit_Lord");
            ArtistSuit    = Find("VR_Apparel_ArtificerSuit_Basic");

            FeatheredFur = Find("Leather_AvirFeatheredFur");
            Cloudcloth   = Find("Cloudcloth");

            Active = ArchitectSuit != null || ArtistSuit != null;

            if (Active) Log.Message("[Avir] Fantasy Biotech detected - High Artificier gear integration active.");
        }

        private static ThingDef Find(string n) { return DefDatabase<ThingDef>.GetNamedSilentFail(n); }

        private static ThingDef StuffFor(ThingDef ap)
        {
            if (ap == null || ap.stuffCategories == null) return null;

            foreach (StuffCategoryDef c in ap.stuffCategories)
            {
                if (c == StuffCategoryDefOf.Leathery && FeatheredFur != null) return FeatheredFur;
                if (c == StuffCategoryDefOf.Fabric && Cloudcloth != null) return Cloudcloth;
            }
            return null;
        }

        /// <summary>
        /// Dresses the torso suit only - no headgear, no cloak, no hood. Returns false (and
        /// wears nothing) if the relevant suit isn't available or can't be stuffed, so the
        /// caller can fall back to the old placeholder wardrobe.
        /// </summary>
        public static bool Dress(Pawn pawn, bool isArchitect)
        {
            if (!Active || pawn == null || pawn.apparel == null) return false;

            ThingDef suit = isArchitect ? ArchitectSuit : ArtistSuit;
            if (suit == null) return false;

            ThingDef stuff = null;
            if (suit.MadeFromStuff)
            {
                stuff = StuffFor(suit);
                if (stuff == null) return false;
            }

            Apparel a = (Apparel)ThingMaker.MakeThing(suit, stuff);

            CompQuality q = a.TryGetComp<CompQuality>();
            if (q != null) q.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Outsider);

            AvirGearUtil.Weather(a);
            pawn.apparel.Wear(a, false);
            return true;
        }
    }
}
