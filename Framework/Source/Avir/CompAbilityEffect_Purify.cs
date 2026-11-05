using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir
{
    public class CompProperties_Purify : CompProperties_AbilityEffect
    {
        public float arcaneHungerCastCost = 0.20f;
        public float minCastDistanceFromCaster = 25f;

        public CompProperties_Purify()
        {
            compClass = typeof(CompAbilityEffect_Purify);
        }
    }

    public class CompAbilityEffect_Purify : CompAbilityEffect
    {
        public new CompProperties_Purify Props => (CompProperties_Purify)props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
            {
                return false;
            }

            Pawn caster = parent.pawn;
            if (caster == null || !target.Cell.IsValid)
            {
                return false;
            }

            if (!Gene_ArcaneHunger.HasAtLeast(caster, Props.arcaneHungerCastCost))
            {
                if (throwMessages)
                {
                    Messages.Message("Avir_PurifyNotEnoughHunger".Translate(caster.Named("PAWN")), caster, MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            float distSq = (target.Cell - caster.Position).LengthHorizontalSquared;
            float minDistSq = Props.minCastDistanceFromCaster * Props.minCastDistanceFromCaster;
            if (distSq < minDistSq)
            {
                if (throwMessages)
                {
                    Messages.Message("Avir_PurifyTooClose".Translate(caster.Named("PAWN")), caster, MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            if (!HasFireAt(caster.Map, target.Cell))
            {
                if (throwMessages)
                {
                    Messages.Message("Avir_PurifyNoFire".Translate(caster.Named("PAWN")), caster, MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent.pawn;
            if (caster == null || caster.Map == null)
            {
                return;
            }

            if (!Gene_ArcaneHunger.Spend(caster, Props.arcaneHungerCastCost))
            {
                return;
            }

            int stacks = ApplyEcstaticFlames(caster);

            // Fires immediately: a real explosion at the center (Flame damage only), plus a
            // hollow ring of fire around it with a clean, untouched gap in between for fire to
            // spread through naturally. Any additional stacks repeat just the ring, 60 ticks
            // apart, at this same spot.
            GameComponent_AvirPurifyChains.DetonatePattern(caster.Map, target.Cell, caster);

            int additionalRepeats = stacks - 1;
            Current.Game.GetComponent<GameComponent_AvirPurifyChains>()
                ?.QueueChainRepeats(caster.Map, target.Cell, caster, additionalRepeats);
        }

        private static bool HasFireAt(Map map, IntVec3 cell)
        {
            if (map == null)
            {
                return false;
            }

            var things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Fire)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Adds one stack (capped at 10) and resets the 36h timer to full - manually
        /// resetting HediffComp_Disappears rather than removing/re-adding the hediff, which would
        /// lose the running stack count. Returns the resulting stack count.</summary>
        private static int ApplyEcstaticFlames(Pawn caster)
        {
            Hediff existing = caster.health.hediffSet.GetFirstHediffOfDef(AvirDefOf.Avir_EcstaticFlames);
            if (existing == null)
            {
                existing = HediffMaker.MakeHediff(AvirDefOf.Avir_EcstaticFlames, caster);
                existing.Severity = 1f;
                caster.health.AddHediff(existing);
            }
            else
            {
                existing.Severity = Mathf.Min(existing.Severity + 1f, 10f);
            }

            HediffComp_Disappears disappears = existing.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = 90000; // 36h - refreshed to full on every cast.
            }

            return Mathf.RoundToInt(existing.Severity);
        }
    }
}
