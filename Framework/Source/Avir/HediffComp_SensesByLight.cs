using RimWorld;
using Verse;

namespace KeenSensesGene
{
    /// <summary>
    /// FOUR light-states, decided from two independent facts: SUNLIGHT (not total glow, so
    /// artificial light never triggers the daytime penalty) and the pawn's current circadian
    /// rhythm (Avir_RhythmNocturnal / Avir_RhythmDiurnal - see AvirRhythm.cs).
    ///
    ///   nocturnal + sunlit -> sunlightSeverity        -> "sun-blinded"  (Sight -20%, cap 120%, +50% Hearing)
    ///   nocturnal + dark   -> darknessSeverity        -> "night-attuned" (Sight 175%)
    ///   diurnal   + sunlit -> diurnalSunlightSeverity -> "sun-adapted"  (no Sight penalty, +50% Hearing)
    ///   diurnal   + dark   -> diurnalDarknessSeverity -> "dark-attuned" (Sight 150%)
    ///
    /// The nocturnal pair is exactly what every Avir used to run before the rhythm system
    /// existed, so nothing regresses for a pawn that never changes its hours.
    ///
    /// Severity is never 0, so the hediff is never auto-removed. "Dark" means indoors, under
    /// torch/lamp light, at night, or in true darkness.
    ///
    /// THIS COMP ALSO DRIVES THE QUADRUPEDE HEDIFF. QuadrupedeManipulationPenalty used to carry
    /// its own ticking comp; it no longer does. HealthTick's cost is dominated by the NUMBER of
    /// CompPostTick calls, not by what each call does - the interval check happens INSIDE the
    /// call, so a comp that does nothing on 549 ticks out of 550 still pays 550 calls. Folding
    /// the quadrupede stage into this pass removes one comp per Avir per tick outright.
    ///
    /// Both hediffs stay on the pawn with their own labels and stages, so anything reading them
    /// from outside (RimTalk) sees exactly what it saw before.
    ///
    /// SAFE BY PREREQUISITE. Quadrupede requires Fearborn, Fearborn's geneClass is
    /// Gene_KeenSenses, and Gene_KeenSenses always grants KeenSensesStance - so a pawn carrying
    /// the quadrupede hediff always carries this comp to drive it. The lookup is still guarded:
    /// no quadrupede hediff, nothing happens.
    ///
    /// HYSTERESIS. A changed reading must repeat itself on the next check before it is committed.
    /// Every severity write fires Notify_HediffChanged and dirties the pawn's capacity cache, and
    /// some frameworks rebuild considerably more on that same notification. Walking past a window,
    /// or a lamp cutting out at dusk, used to buy two rebuilds for nothing. Now a state must hold
    /// across two consecutive checks to be believed: dawn and dusk land one interval later than
    /// before (~7.5s for senses) and nothing else about the behaviour changes.
    /// </summary>
    public class HediffCompProperties_SensesByLight : HediffCompProperties
    {
        /// <summary>Sky glow (0-1) at/above this, while unroofed, counts as direct sunlight.</summary>
        public float sunlightSkyThreshold = 0.5f;

        /// <summary>Severity used for the nocturnal daylight state.</summary>
        public float sunlightSeverity = 0.6f;

        /// <summary>Severity used for the nocturnal dark state - the default an Avir is born into.</summary>
        public float darknessSeverity = 1.0f;

        /// <summary>Severity used for the diurnal daylight state.</summary>
        public float diurnalSunlightSeverity = 0.3f;

        /// <summary>Severity used for the diurnal dark state.</summary>
        public float diurnalDarknessSeverity = 0.8f;

        /// <summary>How often (in ticks) to re-check. Doesn't need to be every tick.</summary>
        public int checkIntervalTicks = 450;

        /// <summary>Severity the quadrupede hediff rests at while inactive. Must be > 0.</summary>
        public float quadrupedeInactiveSeverity = 0.1f;

        public HediffCompProperties_SensesByLight()
        {
            compClass = typeof(HediffComp_SensesByLight);
        }
    }

    public class HediffComp_SensesByLight : HediffComp
    {
        public HediffCompProperties_SensesByLight Props => (HediffCompProperties_SensesByLight)props;

        // Packed state: bit 0 = in sunlight, bit 1 = diurnal. null = not yet checked.
        // Stored as a small int rather than two bools so the "nothing changed, do nothing"
        // early-out stays a single comparison.
        private int? lastState;

        // Last committed quadrupede stage. null = not yet checked.
        private bool? lastQuadrupedeActive;

        // Hysteresis buffers. A reading that disagrees with what is applied is parked here and
        // only committed if the NEXT check agrees with it. Deliberately not saved: a reload costs
        // at most one interval of re-confirmation, and a stale pending value would be worse.
        private int pendingState = -1;
        private int pendingQuadrupede = -1; // -1 = none, 0 = inactive, 1 = active

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            int interval = Props.checkIntervalTicks > 0 ? Props.checkIntervalTicks : 450;
            if ((Find.TickManager.TicksGame + pawn.thingIDNumber) % interval != 0)
            {
                return;
            }

            UpdateSenses(pawn);
            UpdateQuadrupede(pawn);
        }

        private void UpdateSenses(Pawn pawn)
        {
            bool inSunlight = InSunlight(pawn);

            // Anything without a nocturnal marker is treated as diurnal ONLY if it actually
            // carries the diurnal one. A pawn with no marker at all - a non-Avir wearing this
            // hediff through some other route, or an Avir in the single tick before
            // Patch_AvirRhythmOnSpawn runs - falls back to the nocturnal pair, which is the
            // behaviour this hediff had before the rhythm system existed.
            bool diurnal = !Avir.AvirRhythmUtility.IsNocturnal(pawn)
                           && Avir.AvirRhythmUtility.Get(pawn) != null;

            int state = (inSunlight ? 1 : 0) | (diurnal ? 2 : 0);

            if (lastState.HasValue && lastState.Value == state)
            {
                pendingState = -1; // reading agrees with what's applied - drop any pending flip
                return;
            }

            // The very first evaluation commits at once: a pawn should not spend an interval in
            // whatever stage the def's initialSeverity happened to land on. Only later changes
            // have to be confirmed.
            if (lastState.HasValue && pendingState != state)
            {
                pendingState = state;
                return;
            }

            pendingState = -1;
            lastState = state;
            parent.Severity = SeverityFor(inSunlight, diurnal);
        }

        private void UpdateQuadrupede(Pawn pawn)
        {
            Hediff quad = pawn.health?.hediffSet?.GetFirstHediffOfDef(
                QuadrupedeGene.QuadrupedeDefOf.QuadrupedeManipulationPenalty);
            if (quad == null)
            {
                return;
            }

            bool active = QuadrupedeGene.QuadrupedeStance.ShouldBeActive(pawn);
            int reading = active ? 1 : 0;

            if (lastQuadrupedeActive.HasValue && lastQuadrupedeActive.Value == active)
            {
                pendingQuadrupede = -1;
                return;
            }

            if (lastQuadrupedeActive.HasValue && pendingQuadrupede != reading)
            {
                pendingQuadrupede = reading;
                return;
            }

            pendingQuadrupede = -1;
            lastQuadrupedeActive = active;

            // Active -> stage 1 (-20% Manipulation). Inactive -> stage 0, but NEVER severity 0,
            // or RimWorld would delete the hediff and it could never come back.
            float inactive = Props.quadrupedeInactiveSeverity > 0f ? Props.quadrupedeInactiveSeverity : 0.1f;
            quad.Severity = active ? 1f : inactive;
        }

        private float SeverityFor(bool inSunlight, bool diurnal)
        {
            if (diurnal)
            {
                if (inSunlight)
                {
                    return Props.diurnalSunlightSeverity > 0f ? Props.diurnalSunlightSeverity : 0.3f;
                }

                return Props.diurnalDarknessSeverity > 0f ? Props.diurnalDarknessSeverity : 0.8f;
            }

            if (inSunlight)
            {
                return Props.sunlightSeverity > 0f ? Props.sunlightSeverity : 0.6f;
            }

            return Props.darknessSeverity > 0f ? Props.darknessSeverity : 1f;
        }

        // True only in real sunlight: standing on an unroofed cell while the sky is bright.
        // Artificial light (torches, lamps) does NOT count, so indoors is always dark-attuned.
        private bool InSunlight(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Map == null)
            {
                return false;
            }

            return !pawn.Position.Roofed(pawn.Map)
                   && pawn.Map.skyManager.CurSkyGlow >= Props.sunlightSkyThreshold;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            // Deliberately a NEW key. The old "sensesByLightInSunlight" was a bool? and its
            // meaning has changed; letting a stale value load would leave a pawn one interval
            // behind on the wrong stage. Dropping it means every existing save re-evaluates
            // once on the first check after load, which is the correct outcome anyway.
            Scribe_Values.Look(ref lastState, "sensesByLightState", null);
            Scribe_Values.Look(ref lastQuadrupedeActive, "sensesByLightQuadrupedeActive", null);
        }
    }
}
