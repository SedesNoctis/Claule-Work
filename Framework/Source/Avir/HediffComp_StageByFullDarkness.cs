using Verse;

namespace Avir
{
    /// <summary>
    /// Same shape as the mod's other light-conditional hediff comps (HediffComp_SensesByLight,
    /// HediffComp_StageBySunlightOrDarkness), but keyed specifically to PITCH darkness - ground
    /// glow at the pawn's own cell equal to 0, not merely "shaded" or "indoors". Built for Living
    /// Shadow: the darkness-only stage is meant to be a strong, situational payoff, not something
    /// that's active any time a pawn steps indoors.
    ///
    /// Severity is never driven to 0, so the hediff (whose own lifetime is controlled separately,
    /// e.g. by HediffCompProperties_Disappears) never gets auto-removed just for standing in the
    /// light.
    /// </summary>
    public class HediffCompProperties_StageByFullDarkness : HediffCompProperties
    {
        /// <summary>Severity used while NOT in full darkness. Must be > 0.</summary>
        public float litSeverity = 0.1f;

        /// <summary>Severity used while standing in full (0-glow) darkness.</summary>
        public float darkSeverity = 1.0f;

        /// <summary>How often (in ticks) to re-check. Doesn't need to be every tick.</summary>
        public int checkIntervalTicks = 60;

        public HediffCompProperties_StageByFullDarkness()
        {
            compClass = typeof(HediffComp_StageByFullDarkness);
        }
    }

    public class HediffComp_StageByFullDarkness : HediffComp
    {
        public HediffCompProperties_StageByFullDarkness Props =>
            (HediffCompProperties_StageByFullDarkness)props;

        // null = not yet checked; true/false = last applied state.
        private bool? lastKnownInDarkness;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            int interval = Props.checkIntervalTicks > 0 ? Props.checkIntervalTicks : 60;
            // Staggered per-pawn (by thingIDNumber) so carriers don't all recompute on the same tick.
            if ((Find.TickManager.TicksGame + pawn.thingIDNumber) % interval != 0)
            {
                return;
            }

            bool inDarkness = IsInFullDarkness(pawn);

            if (lastKnownInDarkness.HasValue && lastKnownInDarkness.Value == inDarkness)
            {
                return;
            }

            lastKnownInDarkness = inDarkness;

            float lit = Props.litSeverity > 0f ? Props.litSeverity : 0.1f;
            parent.Severity = inDarkness ? Props.darkSeverity : lit;
        }

        private static bool IsInFullDarkness(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Map == null)
            {
                return false;
            }

            return pawn.Map.glowGrid.GroundGlowAt(pawn.Position) <= 0f;
        }
    }
}
