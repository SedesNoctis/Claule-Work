using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    internal static class AvirThoughtUtil
    {
        public static bool Valid(Pawn p)
        {
            return p != null && p.Spawned && p.Map != null && !p.Dead;
        }

        public static float Glow(Pawn p)
        {
            return p.Map.glowGrid.GroundGlowAt(p.Position);
        }

        public static bool InSunlight(Pawn p)
        {
            // Outdoors, during the day, under real sun - not lamplight.
            return !p.Position.Roofed(p.Map)
                   && GenLocalDate.DayPercent(p) > 0.25f
                   && GenLocalDate.DayPercent(p) < 0.75f
                   && p.Map.skyManager.CurSkyGlow > 0.6f;
        }
    }

    /// Skyhungry: miserable under a roof, delighted under open sky.
    public class ThoughtWorker_AvirSkyhunger : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirThoughtUtil.Valid(p)) return ThoughtState.Inactive;

            return p.Position.Roofed(p.Map)
                ? ThoughtState.ActiveAtStage(0)
                : ThoughtState.ActiveAtStage(1);
        }
    }

    /// Deepdark: loves the black, resents the glare.
    public class ThoughtWorker_AvirDeepdark : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirThoughtUtil.Valid(p)) return ThoughtState.Inactive;

            if (AvirThoughtUtil.Glow(p) < 0.3f) return ThoughtState.ActiveAtStage(0);
            if (AvirThoughtUtil.InSunlight(p)) return ThoughtState.ActiveAtStage(1);
            return ThoughtState.Inactive;
        }
    }

    /// Sunwarmed: the exact inverse.
    public class ThoughtWorker_AvirSunwarmed : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirThoughtUtil.Valid(p)) return ThoughtState.Inactive;

            if (AvirThoughtUtil.InSunlight(p)) return ThoughtState.ActiveAtStage(0);
            if (AvirThoughtUtil.Glow(p) < 0.3f) return ThoughtState.ActiveAtStage(1);
            return ThoughtState.Inactive;
        }
    }

    /// Bare-plumed: proud of the down, and only while actually unclothed.
    public class ThoughtWorker_AvirBarePlumed : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!AvirThoughtUtil.Valid(p)) return ThoughtState.Inactive;
            if (p.apparel == null) return ThoughtState.Inactive;

            return p.apparel.WornApparelCount == 0
                ? ThoughtState.ActiveAtStage(0)
                : ThoughtState.Inactive;
        }
    }

    /// Moult-fretter: a low, permanent irritation. Worse when the coat is shedding hard,
    /// which the FeatheredFur gene tracks; if that hediff isn't present it's just constant.
    public class ThoughtWorker_AvirMoultfret : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.Dead) return ThoughtState.Inactive;
            return ThoughtState.ActiveAtStage(0);
        }
    }
}
