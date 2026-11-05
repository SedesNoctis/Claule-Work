using Verse;

namespace Avir.Traits
{
    public class HediffCompProperties_Salvation : HediffCompProperties
    {
        public int ticksUntilDeath = 7500;

        public HediffCompProperties_Salvation()
        {
            this.compClass = typeof(HediffComp_Salvation);
        }
    }

    /// <summary>
    /// Carries over watchedPawn/crowWatcher from the WatchingOver hediff it replaces (set via
    /// Initialize right after creation, BEFORE AddHediff - see Patch_CrowDeath_Effects). Downed
    /// state comes from the XML capMod, not from code here - this only handles the countdown-to-
    /// death, using parent.ageTicks.
    ///
    /// heldCorpse: set post-death by Patch_CrowDeath_Effects.CaptureAndHoldCorpseIfAtRisk if the
    /// watched pawn died alone on a map with no colonists (at risk of being torn down before the
    /// 3-hour wait finishes, taking the corpse with it). If set, the corpse has been despawned
    /// and exists only as this reference until resurrection respawns it - see
    /// ResurrectionEffectUtility.
    /// </summary>
    public class HediffComp_Salvation : HediffComp
    {
        public HediffCompProperties_Salvation Props => (HediffCompProperties_Salvation)this.props;

        public Pawn watchedPawn;
        public Pawn crowWatcher;
        public Corpse heldCorpse;

        private bool deathTriggered;
        private bool firstTickLogged;
        private int lastProgressLogTick = -1;

        public void Initialize(Pawn watchedPawnIn, Pawn crowWatcherIn)
        {
            this.watchedPawn = watchedPawnIn;
            this.crowWatcher = crowWatcherIn;
            Log.Message("[Avir] Salvation initialized on " + this.parent?.pawn?.LabelShortCap
                + " - watchedPawn: " + (watchedPawnIn != null ? watchedPawnIn.LabelShortCap : "NULL")
                + " | crowWatcher: " + (crowWatcherIn != null ? crowWatcherIn.LabelShortCap : "NULL"));
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);

            if (!this.firstTickLogged)
            {
                this.firstTickLogged = true;
                Log.Message("[Avir] Salvation on " + (this.parent?.pawn?.LabelShortCap ?? "?")
                    + " is ticking - ageTicks=" + this.parent.ageTicks + ", ticksUntilDeath=" + this.Props.ticksUntilDeath
                    + ", watchedPawn set: " + (this.watchedPawn != null));
            }

            if (this.deathTriggered) return;

            Pawn crow = this.parent.pawn;
            if (crow == null || crow.Dead) return;

            if (this.lastProgressLogTick < 0 || this.parent.ageTicks - this.lastProgressLogTick >= 2500)
            {
                this.lastProgressLogTick = this.parent.ageTicks;
                int remaining = this.Props.ticksUntilDeath - this.parent.ageTicks;
                Log.Message("[Avir] Salvation progress on " + crow.LabelShortCap
                    + ": ageTicks=" + this.parent.ageTicks + "/" + this.Props.ticksUntilDeath
                    + " (" + System.Math.Max(0, remaining) + " ticks remaining)");
            }

            if (this.parent.ageTicks >= this.Props.ticksUntilDeath)
            {
                this.deathTriggered = true;
                Log.Message("[Avir] Salvation countdown complete on " + crow.LabelShortCap + " - calling Kill() now.");
                crow.Kill(null, this.parent);
            }
        }

        // Same risk category as CompDescriptionExtra earlier: if this fails to compile (CS0115),
        // HediffComp doesn't expose this member in your version - delete this property override,
        // nothing else in this file depends on it.
        public override string CompTipStringExtra
        {
            get
            {
                int remaining = this.Props.ticksUntilDeath - this.parent.ageTicks;
                if (remaining <= 0) return "Resurrection: imminent";
                return "Resurrection in: " + TicksToSimpleString(remaining);
            }
        }

        private static string TicksToSimpleString(int ticks)
        {
            int totalMinutes = ticks / (2500 / 60);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;

            if (hours > 0) return hours + "h " + minutes + "m";
            return minutes + "m";
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref this.watchedPawn, "watchedPawn");
            Scribe_References.Look(ref this.crowWatcher, "crowWatcher");
            Scribe_References.Look(ref this.heldCorpse, "heldCorpse");
            Scribe_Values.Look(ref this.deathTriggered, "deathTriggered", false);
        }
    }
}
