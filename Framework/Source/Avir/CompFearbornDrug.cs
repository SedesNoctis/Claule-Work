using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Identical to CompProperties_Drug in every field - exists purely so the comp it spawns
    /// (Comp_FearbornDrug) can be swapped in wherever a drug effect should only ever apply to
    /// Fearborn (gene-carrying) pawns. Used on Shadowsong Wine for both its ordinary Alcohol
    /// content and (separately) its Avir_ShadowsongEssence dependency, so a non-Fearborn pawn
    /// gets neither: they vomit the whole glass back up before either ever reaches their
    /// system.
    /// </summary>
    public class CompProperties_FearbornDrug : CompProperties_Drug
    {
        public CompProperties_FearbornDrug()
        {
            compClass = typeof(Comp_FearbornDrug);
        }
    }

    /// <summary>
    /// A CompDrug that simply refuses to run its normal tolerance/addiction pipeline (and
    /// whatever else CompDrug.PostIngested does) unless the pawn drinking has an active
    /// Arcane Hunger gene. Everything else about the comp behaves exactly like vanilla
    /// CompDrug for the pawns it does apply to.
    /// </summary>
    public class Comp_FearbornDrug : CompDrug
    {
        public override void PostIngested(Pawn ingester)
        {
            if (Gene_ArcaneHunger.Of(ingester) == null) return;
            base.PostIngested(ingester);
        }
    }
}
