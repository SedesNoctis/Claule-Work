using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Lives exactly as long as the pawn's current mental state does - not a fixed timer on
    /// itself, so it tracks correctly even if the break ends early for an unrelated reason
    /// (arrested, downed, tended). The moment the pawn is no longer in ANY mental state, this
    /// hediff removes itself and grants survivedThought once.
    ///
    /// ALSO now actively caps the mental state's duration: since Dazzling Scream forces vanilla's
    /// own PanicFlee/WanderPsychotic (whose real durations run much longer than we want), once
    /// this hediff's age passes forcedDurationTicks, it calls pawn.MentalState.RecoverFromState()
    /// to end the break early. Set forcedDurationTicks to -1 (the default) to disable this and
    /// just track/reward normally, uncapped - useful if this comp ever gets reused somewhere that
    /// doesn't want the forced cutoff.
    /// </summary>
    public class HediffCompProperties_ScreamMarked : HediffCompProperties
    {
        public ThoughtDef survivedThought;
        public int forcedDurationTicks = -1;

        public HediffCompProperties_ScreamMarked()
        {
            this.compClass = typeof(HediffComp_ScreamMarked);
        }
    }

    public class HediffComp_ScreamMarked : HediffComp
    {
        public HediffCompProperties_ScreamMarked Props => (HediffCompProperties_ScreamMarked)this.props;

        // Guards against granting the thought on the very first interval, before the mental state
        // that triggered this hediff has had a chance to actually register as active.
        private bool everConfirmedActive;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);

            Pawn pawn = this.parent.pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned) return;

            if (pawn.MentalState != null)
            {
                this.everConfirmedActive = true;

                if (this.Props.forcedDurationTicks > 0 && this.parent.ageTicks >= this.Props.forcedDurationTicks)
                {
                    pawn.MentalState.RecoverFromState();
                }

                return;
            }

            if (!this.everConfirmedActive) return;

            if (this.Props.survivedThought != null && pawn.needs?.mood?.thoughts?.memories != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(this.Props.survivedThought);
            }

            pawn.health.RemoveHediff(this.parent);
        }
    }
}
