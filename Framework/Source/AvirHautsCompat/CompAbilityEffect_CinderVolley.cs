using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    public class CompProperties_CinderVolley : CompProperties_AbilityEffect
    {
        public HediffDef cinderDrawnHediff;

        public CompProperties_CinderVolley()
        {
            this.compClass = typeof(CompAbilityEffect_CinderVolley);
        }
    }

    public class CompAbilityEffect_CinderVolley : CompAbilityEffect
    {
        public new CompProperties_CinderVolley Props => (CompProperties_CinderVolley)this.props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = this.parent.pawn;
            if (caster == null || caster.health?.hediffSet == null || this.Props.cinderDrawnHediff == null) return;

            // Non-stacking: refresh if already active (e.g. cast again right as it's about to
            // expire via some future rebalance) rather than adding a second instance.
            Hediff existing = caster.health.hediffSet.GetFirstHediffOfDef(this.Props.cinderDrawnHediff);
            if (existing != null)
            {
                HediffComp_Disappears disappears = existing.TryGetComp<HediffComp_Disappears>();
                if (disappears != null)
                {
                    HediffCompProperties_Disappears dp = disappears.props as HediffCompProperties_Disappears;
                    if (dp != null)
                    {
                        disappears.ticksToDisappear = dp.disappearsAfterTicks.RandomInRange;
                    }
                }
                return;
            }

            Hediff hediff = HediffMaker.MakeHediff(this.Props.cinderDrawnHediff, caster);
            caster.health.AddHediff(hediff);
        }
    }
}
