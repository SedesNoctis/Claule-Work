using RimWorld;
using Verse;

namespace Avir.Traits
{
    public class HediffCompProperties_SoulmarkPending : HediffCompProperties
    {
        public int ticksUntilExpire = 60000; // 24 in-game hours

        public HediffCompProperties_SoulmarkPending()
        {
            this.compClass = typeof(HediffComp_SoulmarkPending);
        }
    }

    /// <summary>
    /// Lives on the Architect between the first cast (victim chosen) and the second
    /// (mechanoid chosen). Same countdown shape as HediffComp_Salvation, using
    /// parent.ageTicks instead of a manual tick counter. If the window closes without a
    /// second cast, the mark fails entirely - this removes both itself and the victim's
    /// Avir_Soulmarked hediff.
    /// </summary>
    public class HediffComp_SoulmarkPending : HediffComp
    {
        public HediffCompProperties_SoulmarkPending Props => (HediffCompProperties_SoulmarkPending)this.props;

        public Pawn victim;

        private bool expired;

        public override string CompTipStringExtra
        {
            get
            {
                if (this.victim == null) return null;
                int remaining = this.Props.ticksUntilExpire - this.parent.ageTicks;
                if (remaining <= 0) return "Window closing: " + this.victim.LabelShortCap;
                return "Bind a mechanoid to " + this.victim.LabelShortCap + " within: " + TicksToSimpleString(remaining);
            }
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);

            if (this.expired) return;
            if (this.parent.ageTicks < this.Props.ticksUntilExpire) return;

            this.expired = true;
            ExpireMark();
        }

        private void ExpireMark()
        {
            Pawn architect = this.parent?.pawn;

            if (this.victim != null && !this.victim.Destroyed)
            {
                HediffDef soulmarkedDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Soulmarked");
                Hediff h = soulmarkedDef != null ? this.victim.health?.hediffSet?.GetFirstHediffOfDef(soulmarkedDef) : null;

                // Only strip it if it never got bonded to a mechanoid - a completed bond has
                // its own lifecycle from here on and shouldn't be touched by this expiry.
                HediffComp_Soulmarked markedComp = h?.TryGetComp<HediffComp_Soulmarked>();
                if (h != null && (markedComp == null || markedComp.mechanoid == null))
                {
                    this.victim.health.RemoveHediff(h);
                    if (architect != null)
                    {
                        Messages.Message("The soulmark on " + this.victim.LabelShortCap + " faded before " + architect.LabelShortCap + " could bind it to anything.", this.victim, MessageTypeDefOf.NeutralEvent, false);
                    }
                }
            }

            if (architect != null && architect.health != null)
            {
                architect.health.RemoveHediff(this.parent);
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
            Scribe_References.Look(ref this.victim, "soulmarkVictim");
            Scribe_Values.Look(ref this.expired, "expired", false);
        }
    }
}
