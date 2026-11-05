using RimWorld;
using UnityEngine;
using Verse;

namespace AvirHautsCompat
{
    public static class RendEffectUtility
    {
        public static void ApplyDrained(
            Pawn caster,
            Pawn victim,
            HediffDef drainedHediff,
            int baseDurationTicks, int minDurationTicks, int maxDurationTicks,
            int baseIntervalTicks, int minIntervalTicks, int maxIntervalTicks,
            int cutsPerInterval,
            float baseCutDamageMin, float baseCutDamageMax,
            float minDamageMult, float maxDamageMult,
            float minPsySens, float maxPsySens)
        {
            if (victim == null || victim.Dead || victim.health?.hediffSet == null || drainedHediff == null) return;

            float psySens = Mathf.Clamp(caster.GetStatValue(StatDefOf.PsychicSensitivity), minPsySens, maxPsySens);

            int durationTicks = Mathf.RoundToInt(Mathf.Clamp(baseDurationTicks * psySens, minDurationTicks, maxDurationTicks));
            int intervalTicks = Mathf.RoundToInt(Mathf.Clamp(baseIntervalTicks / psySens, minIntervalTicks, maxIntervalTicks));
            float damageMult = Mathf.Clamp(Mathf.Sqrt(psySens), minDamageMult, maxDamageMult);

            Hediff hediff = victim.health.hediffSet.GetFirstHediffOfDef(drainedHediff);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(drainedHediff, victim);
                victim.health.AddHediff(hediff);
            }

            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null) disappears.ticksToDisappear = durationTicks;

            HediffComp_PeriodicExternalCuts cutsComp = hediff.TryGetComp<HediffComp_PeriodicExternalCuts>();
            cutsComp?.Configure(intervalTicks, cutsPerInterval, baseCutDamageMin * damageMult, baseCutDamageMax * damageMult);
        }
    }
}
