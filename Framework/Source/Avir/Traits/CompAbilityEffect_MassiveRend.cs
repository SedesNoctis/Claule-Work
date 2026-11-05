using System.Collections.Generic;
using Avir;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Traits
{
    public class CompProperties_MassiveRend : CompProperties_AbilityEffect
    {
        public float arcaneHungerCost = 0.10f;
        public HediffDef drainedHediff;
        public float radius = 2.5f;

        public CompProperties_MassiveRend()
        {
            this.compClass = typeof(CompAbilityEffect_MassiveRend);
        }
    }

    public class CompAbilityEffect_MassiveRend : CompAbilityEffect
    {
        private const float MinPsySens = 0.1f;
        private const float MaxPsySens = 3f;

        private const int BaseDurationTicks = 600;   // 10s @ sensitivity 1.0
        private const int MinDurationTicks = 300;    // 5s floor
        private const int MaxDurationTicks = 1200;   // 20s hard cap

        private const int BaseIntervalTicks = 150;   // a burst every 2.5s @ sensitivity 1.0 (was 5s)
        private const int MinIntervalTicks = 75;     // never faster than every 1.25s
        private const int MaxIntervalTicks = 300;    // never slower than every 5s

        private const int CutsPerInterval = 3;        // 3 separate cuts per burst, not 1

        private const float BaseCutDamageMin = 1.5f;
        private const float BaseCutDamageMax = 3f;
        private const float MinDamageMult = 0.7f;
        private const float MaxDamageMult = 1.5f;

        public new CompProperties_MassiveRend Props => (CompProperties_MassiveRend)this.props;

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
            Map map = caster.Map;
            if (map == null) return;

            IntVec3 targetCell = target.Cell.IsValid ? target.Cell : caster.Position;

            Gene_ArcaneHunger hunger = Gene_ArcaneHunger.Of(caster);
            if (hunger != null)
            {
                hunger.Value = Mathf.Clamp01(hunger.Value - this.Props.arcaneHungerCost);
            }

            HashSet<Pawn> affected = new HashSet<Pawn>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(targetCell, this.Props.radius, true))
            {
                if (!cell.InBounds(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn p) affected.Add(p);
                }
            }

            foreach (Pawn victim in affected)
            {
                if (victim == null || victim.Dead) continue;

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
}
