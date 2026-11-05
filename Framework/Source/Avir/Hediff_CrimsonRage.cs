using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Drives the whole Crimson Rage effect: which stage the pawn is in (severity, with its
    /// stat block now carried directly by this hediff's own stages - the earlier split into a
    /// visible tracker + hidden effects hediff was merged back after a third-party mod threw
    /// exceptions trying to read stat data off the hidden one), and the forced mental break on
    /// a relapse dose.
    ///
    /// IngestionOutcomeDoer_CrimsonRage is the only thing that should call ApplyNormalDose or
    /// TriggerRelapse - it's the one deciding, based on lastDoseTick, which of the two this
    /// drink is.
    /// </summary>
    public class Hediff_CrimsonRage : HediffWithComps
    {
        public int lastDoseTick = -999999;
        private bool pendingMentalBreak;

        /// <summary>
        /// Confirmed via decompiled Hediff.cs: the displayed label is built as
        /// LabelBase + " (" + LabelInBrackets + ")" - these are TWO SEPARATE properties, and
        /// both default to reading CurStage.label independently. Overriding only LabelBase
        /// (what was shipped before) changes the first part but LabelInBrackets still
        /// independently appends the stage name in parentheses - that's exactly why
        /// "Crimson rage (Crimson Rage)" didn't go away. Overriding both: LabelBase becomes
        /// the stage name, LabelInBrackets is forced empty so nothing gets appended at all.
        /// </summary>
        public override string LabelBase => CurStage?.label ?? def.label;
        public override string LabelInBrackets => null;

        private static MentalStateDef crimsonRageBreak;
        private static MentalStateDef CrimsonRageBreak =>
            crimsonRageBreak ?? (crimsonRageBreak = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_CrimsonRageBreak"));

        /// <summary>Normal use: the "clean" relief effect, Crimson Patience (4/5).</summary>
        public void ApplyNormalDose()
        {
            Severity = 0.80f;
            lastDoseTick = Find.TickManager.TicksGame;
        }

        /// <summary>Second vial within 48h: full severity, forced Crimson Rage (5/5), and
        /// queues the mental break to fire on the next tick.</summary>
        public void TriggerRelapse()
        {
            Severity = 1f;
            lastDoseTick = Find.TickManager.TicksGame;
            pendingMentalBreak = true;
        }

        /// <summary>True if a dose taken right now would count as a relapse (second vial
        /// within 48 game-hours of the last one). Ticks-per-hour = GenDate.TicksPerHour.</summary>
        public bool WouldBeRelapseNow()
        {
            return Find.TickManager.TicksGame - lastDoseTick <= GenDate.TicksPerHour * 48;
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            TryFireMentalBreak();
        }

        public override void Tick()
        {
            base.Tick();
            TryFireMentalBreak();
        }

        private void TryFireMentalBreak()
        {
            if (!pendingMentalBreak) return;
            pendingMentalBreak = false;

            MentalStateDef breakDef = CrimsonRageBreak;
            if (breakDef == null || pawn?.mindState?.mentalStateHandler == null) return;
            if (pawn.mindState.mentalStateHandler.CurStateDef == breakDef) return;

            bool started = pawn.mindState.mentalStateHandler.TryStartMentalState(
                breakDef,
                reason: "Avir_CrimsonRageBreakReason".Translate(pawn.LabelShortCap),
                forceWake: true);

            if (started && pawn.Spawned)
            {
                Find.LetterStack.ReceiveLetter(
                    "Avir_CrimsonRageBreakLetterLabel".Translate(),
                    "Avir_CrimsonRageBreakLetterText".Translate(pawn.LabelShortCap),
                    LetterDefOf.ThreatBig,
                    pawn);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastDoseTick, "lastDoseTick", -999999);
            Scribe_Values.Look(ref pendingMentalBreak, "pendingMentalBreak", false);
        }
    }
}
