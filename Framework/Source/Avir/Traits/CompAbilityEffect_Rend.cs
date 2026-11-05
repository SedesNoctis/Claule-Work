using Avir;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Traits
{
    public class CompProperties_Rend : CompProperties_AbilityEffect
    {
        public float arcaneHungerCost = 0.05f;
        public HediffDef drainedHediff;

        public CompProperties_Rend()
        {
            this.compClass = typeof(CompAbilityEffect_Rend);
        }
    }

    public class CompAbilityEffect_Rend : CompAbilityEffect
    {
        private const float MinPsySens = 0.1f;
        private const float MaxPsySens = 3f;

        private const int BaseDurationTicks = 1200;  // 20s @ sensitivity 1.0
        private const int MinDurationTicks = 600;    // 10s floor
        private const int MaxDurationTicks = 2400;   // 40s hard cap

        private const int BaseIntervalTicks = 120;   // a burst every 2s @ sensitivity 1.0 (was 4s)
        private const int MinIntervalTicks = 60;     // never faster than every 1s
        private const int MaxIntervalTicks = 240;    // never slower than every 4s

        private const int CutsPerInterval = 3;        // 3 separate cuts per burst, not 1

        private const float BaseCutDamageMin = 3f;
        private const float BaseCutDamageMax = 5f;
        private const float MinDamageMult = 0.7f;
        private const float MaxDamageMult = 1.5f;

        public new CompProperties_Rend Props => (CompProperties_Rend)this.props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages)) return false;

            Pawn caster = this.parent.pawn;
            Gene_ArcaneHunger hunger = Gene_ArcaneHunger.Of(caster);

            if (hunger == null || !hunger.Active || hunger.Value < this.Props.arcaneHungerCost)
            {
                if (throwMessages)
                {
                    Messages.Message(
                        caster.LabelShortCap + " doesn't have enough arcane hunger to cast this.",
                        caster,
                        MessageTypeDefOf.RejectInput,
                        false);
                }
                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = this.parent.pawn;
            Pawn victim = target.Thing as Pawn;
            if (victim == null || victim.Dead) return;

            Gene_ArcaneHunger hunger = Gene_ArcaneHunger.Of(caster);
            if (hunger != null)
            {
                hunger.Value = Mathf.Clamp01(hunger.Value - this.Props.arcaneHungerCost);
            }

            RendEffectUtility.ApplyDrained(
                caster, victim, this.Props.drainedHediff,
                BaseDurationTicks, MinDurationTicks, MaxDurationTicks,
                BaseIntervalTicks, MinIntervalTicks, MaxIntervalTicks,
                CutsPerInterval,
                BaseCutDamageMin, BaseCutDamageMax,
                MinDamageMult, MaxDamageMult,
                MinPsySens, MaxPsySens);
        }
    }
}
