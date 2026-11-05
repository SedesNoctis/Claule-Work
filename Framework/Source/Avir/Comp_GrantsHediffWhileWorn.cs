using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Attach to any Apparel ThingDef to apply a hediff for as long as it's actually worn. Pure
    /// data holder, same as CompProperties_GrantsAbilityWhileWorn - the real work happens in
    /// Patch_AvirApparelGrantsHediff (instant, via Pawn_ApparelTracker's
    /// Notify_ApparelAdded/Notify_ApparelRemoved) plus GameComponent_AvirApparelHediffSync's
    /// periodic safety net.
    /// </summary>
    public class CompProperties_GrantsHediffWhileWorn : CompProperties
    {
        public HediffDef hediffDef;

        public CompProperties_GrantsHediffWhileWorn()
        {
            compClass = typeof(Comp_GrantsHediffWhileWorn);
        }
    }

    public class Comp_GrantsHediffWhileWorn : ThingComp
    {
        public CompProperties_GrantsHediffWhileWorn Props => (CompProperties_GrantsHediffWhileWorn)props;
    }
}
