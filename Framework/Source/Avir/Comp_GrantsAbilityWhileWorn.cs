using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Attach to any Apparel ThingDef to grant an ability for as long as it's actually worn.
    /// Just a data holder - see Comp_GrantsAbilityWhileWorn below for why the granting logic
    /// itself lives in a Harmony patch instead of here.
    /// </summary>
    public class CompProperties_GrantsAbilityWhileWorn : CompProperties
    {
        public AbilityDef abilityDef;

        public CompProperties_GrantsAbilityWhileWorn()
        {
            compClass = typeof(Comp_GrantsAbilityWhileWorn);
        }
    }

    /// <summary>
    /// Pure data holder now - attach to an Apparel ThingDef's comps to declare "grants this
    /// ability while worn". All the actual granting/revoking happens in
    /// Patch_AvirApparelGrantsAbility (instant, via Pawn_ApparelTracker's own
    /// Notify_ApparelAdded/Notify_ApparelRemoved - the same proven pattern
    /// GameComponent_PridefulApparelSync/Patch_PridefulApparelMood already use elsewhere in this
    /// mod) plus GameComponent_AvirApparelAbilitySync's slow periodic sweep as a safety net.
    ///
    /// Earlier version of this comp tried to do the sync itself via CompTick() - that does not
    /// reliably fire for worn apparel, which is exactly the problem this mod's Prideful Apparel
    /// system already solved via Harmony hooks on the apparel tracker instead of ThingComp
    /// ticking. This follows that same, working pattern rather than repeating the mistake.
    /// </summary>
    public class Comp_GrantsAbilityWhileWorn : ThingComp
    {
        public CompProperties_GrantsAbilityWhileWorn Props => (CompProperties_GrantsAbilityWhileWorn)props;
    }
}
