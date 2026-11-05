using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// LOWERED EXPECTATIONS.
    ///
    /// RimWorld has no "expectations" stat to lower - expectations are computed from colony
    /// wealth and applied to everyone equally. So the honest way to model an Avir who simply
    /// WANTS LESS is a standing mood offset that cancels part of that pressure out. A bowed
    /// Avir is content in conditions that would have a predator screaming about the floor.
    /// </summary>
    public class ThoughtWorker_AvirBowed : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.story == null || p.story.traits == null) return ThoughtState.Inactive;

            TraitDef bowed = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Bowed");
            if (bowed == null || !p.story.traits.HasTrait(bowed)) return ThoughtState.Inactive;

            return ThoughtState.ActiveAtStage(0);
        }
    }

    /// <summary>
    /// WARMTH TOWARD THE OPPOSING GENDER.
    ///
    /// A bowed Avir thinks well of Fearborn of the other gender - and means it. The circle is
    /// female-led and meritocratic, and a bowed pawn finds that restful rather than galling:
    /// somebody else is in charge, somebody else decides, and that is FINE.
    ///
    /// It cuts both ways by design. A bowed male warms to the matriarchs; a bowed female warms
    /// to the talons who do the killing she does not have to do. The trait is about accepting a
    /// place in an order, not about which end of it you are on.
    /// </summary>
    public class ThoughtWorker_AvirBowedRegard : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null || p == other) return ThoughtState.Inactive;
            if (p.Dead || other.Dead) return ThoughtState.Inactive;

            if (p.story == null || p.story.traits == null) return ThoughtState.Inactive;

            TraitDef bowed = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Bowed");
            if (bowed == null || !p.story.traits.HasTrait(bowed)) return ThoughtState.Inactive;

            // Only toward other Avir. The ground-bound do not get this.
            if (!AvirFaction.HasFearborn(other)) return ThoughtState.Inactive;

            // The OPPOSING gender, specifically.
            if (other.gender == p.gender) return ThoughtState.Inactive;
            if (other.gender == Gender.None || p.gender == Gender.None) return ThoughtState.Inactive;

            return ThoughtState.ActiveAtStage(0);
        }
    }
}
