using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>
    /// Stage 6 - focus. How strongly each arm of an artifact works:
    ///   strength = max(0, 1 + focus) x heart quality factor
    ///   focus    = heart metal (fixed table, or the psychic rule) + rim (half, positives only)
    /// The psychic metals read the WEARER's psychic sensitivity live, so a hungry Avir's
    /// essentium and tisserium weaken on their own (decided: that is intended).
    /// </summary>
    public static class ArtifactFocus
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        private static bool reading;   // guards against a psychic-sensitivity stat that depends on artifacts

        public static bool HasFearborn(Pawn p)
        {
            return p?.genes != null && S?.avirGene != null && p.genes.HasActiveGene(S.avirGene);
        }

        public static float Sensitivity(Pawn p)
        {
            if (p == null || reading) return 1f;
            reading = true;
            try { return p.GetStatValue(StatDefOf.PsychicSensitivity); }
            finally { reading = false; }
        }

        /// <summary>Focus of one arm as a fraction (+0.25 = +25%). `wearer` may be null (loose
        /// artifact): the psychic part then counts as 0 and `explain` says it depends on the wearer.</summary>
        public static float Focus(ArtifactState st, ArtifactArmDef arm, Pawn wearer, StringBuilder explain = null)
        {
            if (st?.heartMetal == null || arm == null) return 0f;
            float total = 0f;
            ArtifactMetalDef h = st.heartMetal;

            float fixedPart = h.FixedFocus(arm);
            if (Mathf.Abs(fixedPart) > 0.0001f)
            {
                total += fixedPart;
                explain?.AppendLine("Heart (" + h.label + "): " + Signed(fixedPart));
            }

            if (h.IsPsychic)
            {
                if (wearer == null)
                {
                    explain?.AppendLine("Heart (" + h.label + "): depends on the wearer's psychic sensitivity");
                }
                else
                {
                    float p = PsychicPart(st, arm, wearer, out string why);
                    total += p;
                    explain?.AppendLine("Heart (" + h.label + "): " + Signed(p) + " - " + why);
                }
            }

            if (st.stone?.metal != null && ArtifactoricsSettingsDef.Main != null)
            {
                float sf = StoneFocus(st.stone.metal, arm);
                if (sf > 0.0001f)
                {
                    total += sf;
                    explain?.AppendLine("Ability stone (" + st.stone.metal.label + "): " + Signed(sf));
                }
            }

            if (st.rimMetal != null)
            {
                float r = st.rimMetal.RimFocus(arm);
                if (r > 0.0001f)
                {
                    total += r;
                    explain?.AppendLine("Rim (" + st.rimMetal.label + "): " + Signed(r));
                }
            }
            return total;
        }

        private static float PsychicPart(ArtifactState st, ArtifactArmDef arm, Pawn wearer, out string why)
        {
            ArtifactPsychicFocus ps = st.heartMetal.psychic;
            float sens = Sensitivity(wearer);
            bool fearborn = HasFearborn(wearer);
            float threshold = fearborn ? ps.thresholdWithFearborn : ps.thresholdWithoutFearborn;
            if (!ps.IsActive(sens, fearborn))
            {
                why = "sensitivity " + sens.ToStringPercent() + " is below " + threshold.ToStringPercent()
                      + (fearborn ? "" : " (no Fearborn)");
                return ps.belowThresholdPenalty;
            }
            int steps = ps.Steps(sens);
            float stepBonus = steps * ps.perStepBonus;

            if (ps.dilutesAcrossArms)
            {
                // essentium: base + steps form one pool, shared by invested arms by their cells
                int armCells = st.OpenedIn(arm);
                // only the arms of the same spiral share it (armour or weapon)
                int allCells = st.opened == null ? 0 : st.opened.Where(kv => kv.Value > 0 && kv.Key.carrier == arm.carrier).Sum(kv => kv.Value);
                float pool = ps.baseBonus + stepBonus;
                if (allCells <= 0)
                {
                    why = "sensitivity " + sens.ToStringPercent() + "; " + pool.ToStringPercent() + " goes to the first arm you develop";
                    return 0f;
                }
                float share = (float)armCells / allCells;
                why = "sensitivity " + sens.ToStringPercent() + "; " + share.ToStringPercent() + " of " + pool.ToStringPercent()
                      + " (shared by the cells of developed arms)";
                return pool * share;
            }

            float v = 0f;
            bool baseApplies = !ps.baseBonusCombatOnly || arm.IsCombat;
            if (baseApplies) v += ps.baseBonus;
            if (ps.stepBonusAllArms || baseApplies) v += stepBonus;
            why = "sensitivity " + sens.ToStringPercent() + (baseApplies ? "" : "; peaceful arm, no base bonus")
                  + (steps > 0 ? "; " + steps + " step" + (steps == 1 ? "" : "s") + " above " + ps.stepsFrom.ToStringPercent() : "");
            return v;
        }

        /// <summary>Multiplier applied to an arm's passive bonuses.</summary>
        public static float Strength(ArtifactState st, ArtifactArmDef arm, Pawn wearer)
        {
            if (S == null) return 1f;
            return Mathf.Max(0f, 1f + Focus(st, arm, wearer)) * S.HeartQualityFactor(st.heartQuality);
        }

        /// <summary>The stone's metal: its focus table at stoneFocusStrength, positives only.</summary>
        public static float StoneFocus(ArtifactMetalDef m, ArtifactArmDef arm)
        {
            float f = m.allArmsBonus > 0f ? m.allArmsBonus : 0f;
            foreach (ArtifactFocusEntry e in m.FocusFor(arm.carrier))
                if (e.arm == arm && e.factor > 0f) f += e.factor;
            return f * ArtifactoricsSettingsDef.Main.stoneFocusStrength;
        }

        public static string Signed(float f)
        {
            return (f >= 0f ? "+" : "") + f.ToStringPercent();
        }
    }

    /// <summary>
    /// Stage 7 - passive arm bonuses. Totals per pawn, rebuilt at most every 250 ticks and at once
    /// when a garment is put on or off or a cell is opened.
    /// </summary>
    public static class ArtifactBonusCache
    {
        public struct Mod
        {
            public float offset;
            public float factor;   // additive percent: final multiplier = 1 + factor
        }

        private class Entry
        {
            public int tick = -99999;
            public readonly Dictionary<StatDef, Mod> mods = new Dictionary<StatDef, Mod>();
        }

        private static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();
        private const int Interval = 250;

        /// <summary>Stats any arm touches; StatPart_ArtifactArms is attached to these.</summary>
        public static readonly HashSet<StatDef> AffectedStats = new HashSet<StatDef>();

        public static void ClearAll()
        {
            cache.Clear();
            ArtifactMilestones.ClearAll();
        }

        public static void Invalidate(Pawn p)
        {
            if (p != null) cache.Remove(p.thingIDNumber);
            ArtifactMilestones.Invalidate(p);
        }

        public static bool TryGet(Pawn p, StatDef stat, out Mod mod)
        {
            mod = default;
            if (p == null) return false;
            List<CompArtifactSocket> worn = ArtifactWearCache.Worn(p);
            if (worn.Count == 0) return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (!cache.TryGetValue(p.thingIDNumber, out Entry e))
            {
                if (cache.Count > 2000) cache.Clear();
                e = new Entry();
                cache[p.thingIDNumber] = e;
            }
            if (now - e.tick >= Interval || now < e.tick)
            {
                e.tick = now;
                Rebuild(p, worn, e.mods);
            }
            return e.mods.TryGetValue(stat, out mod);
        }

        private static void Rebuild(Pawn p, List<CompArtifactSocket> worn, Dictionary<StatDef, Mod> mods)
        {
            mods.Clear();
            for (int i = 0; i < worn.Count; i++)
            {
                ArtifactState st = worn[i].inlaid;
                if (st == null || !st.HasHeart || st.opened == null) continue;
                foreach (KeyValuePair<ArtifactArmDef, int> kv in st.opened)
                {
                    if (kv.Value <= 0 || kv.Key.cellStats.NullOrEmpty() || !worn[i].Uses(kv.Key)) continue;
                    float k = ArtifactFocus.Strength(st, kv.Key, p) * kv.Value;
                    foreach (ArtifactCellStat cs in kv.Key.cellStats)
                    {
                        if (cs.stat == null) continue;
                        mods.TryGetValue(cs.stat, out Mod m);
                        m.offset += cs.offsetPerCell * k;
                        m.factor += cs.factorPerCell * k;
                        mods[cs.stat] = m;
                    }
                }
            }
        }

        /// <summary>Per-artifact lines for the stat explanation.</summary>
        public static IEnumerable<string> Explain(Pawn p, StatDef stat)
        {
            foreach (CompArtifactSocket sock in ArtifactWearCache.Worn(p).ToList())
            {
                ArtifactState st = sock.inlaid;
                if (st?.opened == null || !st.HasHeart) continue;
                foreach (KeyValuePair<ArtifactArmDef, int> kv in st.opened.OrderBy(x => x.Key.armNumber))
                {
                    if (kv.Value <= 0 || !sock.Uses(kv.Key)) continue;
                    foreach (ArtifactCellStat cs in kv.Key.cellStats)
                    {
                        if (cs.stat != stat) continue;
                        float k = ArtifactFocus.Strength(st, kv.Key, p) * kv.Value;
                        string val = cs.offsetPerCell != 0f
                            ? stat.ValueToString(cs.offsetPerCell * k, ToStringNumberSense.Offset)
                            : "x" + (1f + cs.factorPerCell * k).ToStringPercent();
                        yield return "    " + sock.parent.LabelNoCount + ", " + kv.Key.label + " " + kv.Value + " cells: " + val;
                    }
                }
            }
        }
    }

    /// <summary>Adds the worn artifacts' arm bonuses to a pawn stat.</summary>
    public class StatPart_ArtifactArms : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn p)) return;
            if (!ArtifactBonusCache.TryGet(p, parentStat, out ArtifactBonusCache.Mod m)) return;
            val += m.offset;
            val *= Mathf.Max(0f, 1f + m.factor);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is Pawn p)) return null;
            if (!ArtifactBonusCache.TryGet(p, parentStat, out _)) return null;
            List<string> lines = ArtifactBonusCache.Explain(p, parentStat).ToList();
            if (lines.Count == 0) return null;
            return "Artifacts:\n" + string.Join("\n", lines);
        }
    }

    [StaticConstructorOnStartup]
    public static class ArtifactStatPartInjector
    {
        static ArtifactStatPartInjector()
        {
            foreach (ArtifactArmDef arm in DefDatabase<ArtifactArmDef>.AllDefsListForReading)
                foreach (ArtifactCellStat cs in arm.cellStats)
                    if (cs.stat != null) ArtifactBonusCache.AffectedStats.Add(cs.stat);

            foreach (StatDef stat in ArtifactBonusCache.AffectedStats)
            {
                if (stat == StatDefOf.PsychicSensitivity)
                {
                    Log.Error("[Avir] Artifactorics: an arm may not change psychic sensitivity (the psychic metals read it). Skipped.");
                    continue;
                }
                if (stat.parts == null) stat.parts = new List<StatPart>();
                if (stat.parts.Any(x => x is StatPart_ArtifactArms)) continue;
                stat.parts.Add(new StatPart_ArtifactArms { parentStat = stat });
            }
            Log.Message("[Avir] Artifactorics: arm bonuses attached to " + ArtifactBonusCache.AffectedStats.Count + " stats.");
        }
    }
}
