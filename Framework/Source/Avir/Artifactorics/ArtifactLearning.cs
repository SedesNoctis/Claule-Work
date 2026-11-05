using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    [DefOf]
    public static class ArtifactLearningDefOf
    {
        public static HediffDef Avir_ArtifactMentored;

        static ArtifactLearningDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactLearningDefOf)); }
    }

    /// <summary>
    /// Stage 8e - Learning.
    ///   3  mending study: while the garment carrying the artifact is damaged, the wearer's
    ///      experience repairs it (pawn 0%, repair 80%; the artifacts are credited as always).
    ///      1% of hit points costs (sharp armour + 0.5) x 50 experience - sturdier armour costs more.
    ///   7  attentive mind: mending costs 25% less; mentor - pawns working near the wearer learn
    ///      10% faster.
    ///   9  scholar's mail: mending also works on the rest of the worn armour at half strength;
    ///      insight - once a day one skill gain brings a burst of extra experience.
    /// What the repair does not need goes back to the pawn.
    /// </summary>
    public static class ArtifactLearning
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        private static readonly Dictionary<int, float> mendFraction = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> insightDay = new Dictionary<int, int>();
        private static SkillRecord pendingInsightSkill;
        private static float pendingInsightXp;

        /// <summary>Uses up to `budget` experience to repair. Returns how much was used.</summary>
        public static float Mend(Pawn p, float budget)
        {
            if (S == null || budget <= 0f || p.apparel == null) return 0f;
            int lvl = 0;
            Apparel main = null;
            foreach (MilestoneArm m in ArtifactMilestones.For(p))
            {
                if (!m.active || m.arm != ArtifactArmDefOf.ArtifactArm_Learning) continue;
                if (m.reached > lvl) lvl = m.reached;
                if (main == null && m.socket.parent is Apparel a && a.HitPoints < a.MaxHitPoints) main = a;
            }
            if (lvl < 3) return 0f;

            float used = 0f;
            float costFactor = lvl >= 7 ? S.mendCostFactorAt7 : 1f;
            // the garment(s) carrying a Learning artifact first, at full strength
            foreach (MilestoneArm m in ArtifactMilestones.For(p))
            {
                if (!m.active || m.arm != ArtifactArmDefOf.ArtifactArm_Learning) continue;
                if (m.socket.parent is Apparel a && a.HitPoints < a.MaxHitPoints)
                    used += Repair(a, budget - used, costFactor);
                if (used >= budget) return used;
            }
            // Learning 9: the rest of the worn armour at half strength
            if (lvl >= 9)
            {
                List<Apparel> worn = p.apparel.WornApparel;
                for (int i = 0; i < worn.Count && used < budget; i++)
                {
                    Apparel a = worn[i];
                    if (a.HitPoints >= a.MaxHitPoints || a.Socket()?.HasArtifact == true) continue;
                    used += Repair(a, budget - used, costFactor * 2f);
                }
            }
            return used;
        }

        public static float CostPerPercent(Thing a, float costFactor)
        {
            float armour = a.GetStatValue(StatDefOf.ArmorRating_Sharp);
            return Mathf.Max(1f, (armour + S.mendArmorOffset) * S.mendCostPerPercent * costFactor);
        }

        private static float Repair(Apparel a, float budget, float costFactor)
        {
            if (budget <= 0f) return 0f;
            float perPercent = CostPerPercent(a, costFactor);
            float hpPerXp = a.MaxHitPoints / 100f / perPercent;
            int missing = a.MaxHitPoints - a.HitPoints;
            mendFraction.TryGetValue(a.thingIDNumber, out float frac);
            float wantHp = Mathf.Min(missing - frac, budget * hpPerXp);
            if (wantHp <= 0f) return 0f;
            frac += wantHp;
            int whole = Mathf.FloorToInt(frac);
            if (whole > 0)
            {
                a.HitPoints = Mathf.Min(a.MaxHitPoints, a.HitPoints + whole);
                frac -= whole;
            }
            if (a.HitPoints >= a.MaxHitPoints) frac = 0f;
            mendFraction[a.thingIDNumber] = frac;
            return wantHp / hpPerXp;
        }

        public static void TryInsight(Pawn p, SkillRecord skill)
        {
            if (S == null || S.insightXp <= 0f) return;
            int day = GenDate.DaysPassed;
            if (insightDay.TryGetValue(p.thingIDNumber, out int last) && last == day) return;
            if (ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Learning) < 9) return;
            insightDay[p.thingIDNumber] = day;
            pendingInsightSkill = skill;
            pendingInsightXp = S.insightXp;
        }

        public static void FlushInsight(SkillRecord skill)
        {
            if (pendingInsightSkill != skill || pendingInsightXp <= 0f) return;
            float xp = pendingInsightXp;
            pendingInsightSkill = null;
            pendingInsightXp = 0f;
            skill.Learn(xp, true);   // direct: not split again
            Pawn p = skill.Pawn;
            if (p?.Faction == Faction.OfPlayer)
                Messages.Message(p.LabelShort + " has an insight: +" + xp.ToString("0") + " " + skill.def.label + " experience.",
                    p, MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>Learning 7 - mentor: called by the effects manager for every wearer.</summary>
        public static void RefreshMentor(Pawn mentor, IReadOnlyList<Pawn> pawns, int ticks)
        {
            float r2 = S.mentorRadius * S.mentorRadius;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == mentor || p.Dead || p.Faction != mentor.Faction || p.skills == null) continue;
                if ((p.Position - mentor.Position).LengthHorizontalSquared > r2) continue;
                if (p.health.hediffSet.GetFirstHediffOfDef(ArtifactLearningDefOf.Avir_ArtifactMentored) is Hediff_ArtifactTimed h)
                {
                    h.ticksLeft = Mathf.Max(h.ticksLeft, ticks);
                    continue;
                }
                var n = (Hediff_ArtifactTimed)HediffMaker.MakeHediff(ArtifactLearningDefOf.Avir_ArtifactMentored, p);
                n.ticksLeft = ticks;
                n.level = 7;
                p.health.AddHediff(n);
            }
        }
    }
}
