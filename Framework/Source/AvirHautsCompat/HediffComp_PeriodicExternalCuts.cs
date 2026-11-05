using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AvirHautsCompat
{
    public class HediffCompProperties_PeriodicExternalCuts : HediffCompProperties
    {
        public int intervalTicks = 120;
        public int cutsPerInterval = 3;
        public float cutDamageMin = 4f;
        public float cutDamageMax = 7f;

        public HediffCompProperties_PeriodicExternalCuts()
        {
            this.compClass = typeof(HediffComp_PeriodicExternalCuts);
        }
    }

    /// <summary>
    /// Every effectiveIntervalTicks, deals effectiveCutsPerInterval separate Cut hits (each its
    /// own random OUTSIDE-depth, torso/arms/legs/hands/feet/fingers/toes body part).
    ///
    /// Timing FIXED: was checking Find.TickManager.TicksGame % IntervalTicks == 0 - an exact
    /// divisibility check against the absolute game clock, which only reliably fires if this comp
    /// gets ticked every single tick. If the actual invocation cadence is coarser than that (which
    /// explains a full 20-second Drained duration producing zero cuts), hitting an exact multiple
    /// becomes down to luck, worse now that psychic-sensitivity scaling produces non-round
    /// interval values per caster instead of always the same fixed number. Now uses an
    /// ageTicks-based countdown instead ("has enough time passed since the last cut") - same
    /// robust pattern that already fixed Salvation's timer, and it can't miss regardless of
    /// exactly when in the tick sequence it gets invoked.
    /// </summary>
    public class HediffComp_PeriodicExternalCuts : HediffComp
    {
        private static List<BodyPartGroupDef> cachedGroups;

        public HediffCompProperties_PeriodicExternalCuts Props => (HediffCompProperties_PeriodicExternalCuts)this.props;

        private int effectiveIntervalTicks = -1;
        private int effectiveCutsPerInterval = -1;
        private float effectiveCutDamageMin = -1f;
        private float effectiveCutDamageMax = -1f;

        private int nextCutAtAgeTicks = -1;

        public void Configure(int intervalTicks, int cutsPerInterval, float cutDamageMin, float cutDamageMax)
        {
            this.effectiveIntervalTicks = Mathf.Max(1, intervalTicks);
            this.effectiveCutsPerInterval = Mathf.Max(1, cutsPerInterval);
            this.effectiveCutDamageMin = cutDamageMin;
            this.effectiveCutDamageMax = cutDamageMax;
        }

        private int IntervalTicks => this.effectiveIntervalTicks > 0 ? this.effectiveIntervalTicks : this.Props.intervalTicks;
        private int CutsPerInterval => this.effectiveCutsPerInterval > 0 ? this.effectiveCutsPerInterval : this.Props.cutsPerInterval;
        private float CutDamageMin => this.effectiveCutDamageMin >= 0f ? this.effectiveCutDamageMin : this.Props.cutDamageMin;
        private float CutDamageMax => this.effectiveCutDamageMax >= 0f ? this.effectiveCutDamageMax : this.Props.cutDamageMax;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref this.effectiveIntervalTicks, "effectiveIntervalTicks", -1);
            Scribe_Values.Look(ref this.effectiveCutsPerInterval, "effectiveCutsPerInterval", -1);
            Scribe_Values.Look(ref this.effectiveCutDamageMin, "effectiveCutDamageMin", -1f);
            Scribe_Values.Look(ref this.effectiveCutDamageMax, "effectiveCutDamageMax", -1f);
            Scribe_Values.Look(ref this.nextCutAtAgeTicks, "nextCutAtAgeTicks", -1);
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);

            Pawn pawn = this.parent.pawn;
            if (pawn == null || pawn.Dead || !pawn.Spawned) return;

            // First evaluation - schedule the first cut one interval from now rather than
            // instantly, so there's always a brief moment before the first hit lands.
            if (this.nextCutAtAgeTicks < 0)
            {
                this.nextCutAtAgeTicks = this.parent.ageTicks + this.IntervalTicks;
                return;
            }

            if (this.parent.ageTicks < this.nextCutAtAgeTicks) return;

            for (int i = 0; i < this.CutsPerInterval; i++)
            {
                if (pawn.Dead) break;

                BodyPartRecord part = PickCuttableExternalPart(pawn);
                if (part == null) continue;

                float amount = Rand.Range(this.CutDamageMin, this.CutDamageMax);
                DamageInfo dinfo = new DamageInfo(
                    DamageDefOf.Cut,
                    amount,
                    armorPenetration: 0.3f,
                    angle: -1f,
                    instigator: null,
                    hitPart: part,
                    weapon: null);

                pawn.TakeDamage(dinfo);
            }

            this.nextCutAtAgeTicks = this.parent.ageTicks + this.IntervalTicks;
        }

        private static BodyPartRecord PickCuttableExternalPart(Pawn pawn)
        {
            List<BodyPartRecord> candidates = new List<BodyPartRecord>();

            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside))
            {
                if (IsCuttableGroup(part)) candidates.Add(part);
            }

            if (candidates.Count == 0) return null;
            return candidates.RandomElement();
        }

        private static bool IsCuttableGroup(BodyPartRecord part)
        {
            if (part?.groups == null) return false;

            List<BodyPartGroupDef> whitelist = GetGroupWhitelist();
            for (int i = 0; i < part.groups.Count; i++)
            {
                if (whitelist.Contains(part.groups[i])) return true;
            }

            return false;
        }

        private static List<BodyPartGroupDef> GetGroupWhitelist()
        {
            if (cachedGroups != null) return cachedGroups;

            cachedGroups = new List<BodyPartGroupDef>();

            string[] candidateNames =
            {
                "Torso",
                "Arms", "LeftArm", "RightArm",
                "Legs", "LeftLeg", "RightLeg",
                "Hands", "LeftHand", "RightHand",
                "Feet", "LeftFoot", "RightFoot",
                "Fingers", "Toes"
            };

            foreach (string name in candidateNames)
            {
                BodyPartGroupDef def = DefDatabase<BodyPartGroupDef>.GetNamedSilentFail(name);
                if (def != null && !cachedGroups.Contains(def)) cachedGroups.Add(def);
            }

            return cachedGroups;
        }
    }
}
