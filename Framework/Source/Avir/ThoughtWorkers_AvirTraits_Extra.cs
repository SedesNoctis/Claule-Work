using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Generic "always active while the pawn carries the trait" flavour mood. Several of the new
    /// Avir personality traits are emotionally constant - they don't depend on weather, light or
    /// company, only on having the trait at all - so they all share this one worker instead of
    /// each getting a bespoke copy of the same three lines. Which trait, which stage text and
    /// which mood number are entirely up to the ThoughtDef's own requiredTraits/stages.
    /// </summary>
    public class ThoughtWorker_AvirFlavor : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.Dead) return ThoughtState.Inactive;
            return ThoughtState.ActiveAtStage(0);
        }
    }

    /// Stormwise: content in foul weather, restless without it.
    public class ThoughtWorker_AvirStormwise : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirThoughtUtil.Valid(p)) return ThoughtState.Inactive;
            if (p.Position.Roofed(p.Map)) return ThoughtState.Inactive;

            return p.Map.weatherManager.RainRate > 0.1f
                ? ThoughtState.ActiveAtStage(0)
                : ThoughtState.Inactive;
        }
    }

    /// Moonstruck: calm and sharp under open night sky specifically - not just dark, and not
    /// just outdoors, but both together.
    public class ThoughtWorker_AvirMoonstruck : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirThoughtUtil.Valid(p)) return ThoughtState.Inactive;
            if (p.Position.Roofed(p.Map)) return ThoughtState.Inactive;

            float dayPct = GenLocalDate.DayPercent(p);
            bool night = dayPct < 0.2f || dayPct > 0.8f;
            return night ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    /// Painglad: a hurt sharpens the mood instead of souring it.
    public class ThoughtWorker_AvirPainglad : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.Dead || p.health == null) return ThoughtState.Inactive;
            return p.health.hediffSet.PainTotal > 0.10f
                ? ThoughtState.ActiveAtStage(0)
                : ThoughtState.Inactive;
        }
    }

    /// Loneflyer: content alone, worn down by a crowd. Counts nearby humanlikes rather than
    /// relying on room roles, so it works outdoors as well as in.
    public class ThoughtWorker_AvirLoneflyer : ThoughtWorker
    {
        private const float Radius = 12f;
        private const int CrowdedAt = 4;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirThoughtUtil.Valid(p)) return ThoughtState.Inactive;

            int nearby = 0;
            foreach (Pawn other in p.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == p || other.Dead || other.RaceProps == null || !other.RaceProps.Humanlike) continue;
                if (other.Position.DistanceTo(p.Position) <= Radius) nearby++;
                if (nearby >= CrowdedAt) break;
            }

            if (nearby == 0) return ThoughtState.ActiveAtStage(0);
            if (nearby >= CrowdedAt) return ThoughtState.ActiveAtStage(1);
            return ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// The judgement Fearborn society passes on the rare Avir who takes more than one mate.
    /// This is a SOCIAL situational thought: CurrentSocialStateInternal is called with
    /// p = the pawn forming the opinion (the observer) and other = the pawn being judged
    /// (the subject). Every pawn carrying the Fearborn gene holds this opinion of anyone with
    /// the Avir_ManyMated trait, full stop - no exceptions for their own relations, no decay,
    /// no forgiveness. That harshness is the point: this is a real taboo, not a mild disapproval.
    /// </summary>
    public class ThoughtWorker_Social_AvirManyMated : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null || p.Dead || other.Dead) return ThoughtState.Inactive;
            if (!AvirFaction.HasFearborn(p)) return ThoughtState.Inactive;
            if (other.story == null || other.story.traits == null) return ThoughtState.Inactive;
            if (!other.story.traits.HasTrait(AvirDefOf.Avir_ManyMated)) return ThoughtState.Inactive;

            return ThoughtState.ActiveAtStage(0);
        }
    }
}
