using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>
    /// What a metal will do to the paths BEFORE it is spent. The creation panel used to show only
    /// amounts and skills; the focus a heart or rim gives was visible only on the finished artifact.
    /// Numbers come from the same tables the finished artifact reads (FixedFocus, RimFocus), so the
    /// preview and the spiral never disagree.
    /// </summary>
    public static class ArtifactMetalPreview
    {
        private static readonly ArtifactCarrier[] Carriers = { ArtifactCarrier.Apparel, ArtifactCarrier.Weapon };

        public const string FocusMeaning =
            "Focus changes a path's strength: every cell bonus and milestone number of that path is multiplied by (1 + focus).";

        private static string CarrierWord(ArtifactCarrier c) => c == ArtifactCarrier.Weapon ? "In a weapon" : "In armour";

        private static string Join(IEnumerable<KeyValuePair<ArtifactArmDef, float>> list)
        {
            var parts = list.Where(kv => System.Math.Abs(kv.Value) > 0.0001f)
                            .OrderByDescending(kv => kv.Value)
                            .Select(kv => kv.Key.label + " " + ArtifactFocus.Signed(kv.Value)).ToList();
            return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
        }

        /// <summary>The metal as a heart: its full table, weaknesses included, and its psychic rule.</summary>
        public static string HeartText(ArtifactMetalDef m)
        {
            if (m == null) return "";
            var sb = new StringBuilder("As a heart:");
            if (m.allArmsBonus != 0f) sb.Append("\n  Every path ").Append(ArtifactFocus.Signed(m.allArmsBonus)).Append('.');
            foreach (ArtifactCarrier c in Carriers)
            {
                var own = ArtifactoricsDatabase.ArmsFor(c)
                    .Select(a => new KeyValuePair<ArtifactArmDef, float>(a, m.FixedFocus(a) - m.allArmsBonus));
                string line = Join(own);
                if (line != "nothing") sb.Append("\n  ").Append(CarrierWord(c)).Append(": ").Append(line).Append('.');
            }
            if (m.IsPsychic) sb.Append("\n  ").Append(PsychicText(m.psychic));
            else if (m.allArmsBonus == 0f && sb.ToString() == "As a heart:") sb.Append(" no focus.");
            return sb.ToString();
        }

        /// <summary>The metal as a rim: positives only, at rim strength.</summary>
        public static string RimText(ArtifactMetalDef m)
        {
            if (m == null) return "";
            if (!m.allowedAsRim) return "Cannot be a rim.";
            float rs = ArtifactoricsSettingsDef.Main?.rimStrength ?? 0.5f;
            var sb = new StringBuilder("As a rim (" + rs.ToStringPercent() + " of the heart table, bonuses only):");
            bool any = false;
            foreach (ArtifactCarrier c in Carriers)
            {
                string line = Join(ArtifactoricsDatabase.ArmsFor(c).Select(a => new KeyValuePair<ArtifactArmDef, float>(a, m.RimFocus(a))));
                if (line == "nothing") continue;
                sb.Append("\n  ").Append(CarrierWord(c)).Append(": ").Append(line).Append('.');
                any = true;
            }
            if (!any) sb.Append(" nothing.");
            return sb.ToString();
        }

        /// <summary>The artifact that rim + heart will make: focus per path, both spirals.</summary>
        public static string CombinedText(ArtifactMetalDef rim, ArtifactMetalDef heart)
        {
            var sb = new StringBuilder();
            foreach (ArtifactCarrier c in Carriers)
            {
                var arms = ArtifactoricsDatabase.ArmsFor(c);
                var focus = arms.Select(a => new KeyValuePair<ArtifactArmDef, float>(a,
                    (heart?.FixedFocus(a) ?? 0f) + (rim?.RimFocus(a) ?? 0f)));
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(CarrierWord(c)).Append(": ").Append(Join(focus)).Append('.');
                if (heart != null && !heart.IsPsychic)
                {
                    var closed = arms.Where(a => a.requiresPsychicHeart).Select(a => a.label).ToList();
                    if (closed.Count > 0) sb.Append(" Closed: ").Append(string.Join(", ", closed)).Append(" (needs a psychic heart).");
                }
            }
            if (heart == null) sb.Append("\nNo heart yet: only the rim is counted. The heart gives most of the focus.");
            else if (heart.IsPsychic) sb.Append("\n").Append(heart.LabelCap).Append(": ").Append(PsychicText(heart.psychic));
            return sb.ToString();
        }

        /// <summary>The psychic rule of essentium / tisserium in words.</summary>
        public static string PsychicText(ArtifactPsychicFocus ps)
        {
            if (ps == null) return "";
            string cond = ps.thresholdWithFearborn > 0f
                ? "Works from " + ps.thresholdWithFearborn.ToStringPercent() + " psychic sensitivity with Fearborn, " + ps.thresholdWithoutFearborn.ToStringPercent() + " without."
                : "Works for any Fearborn wearer; without Fearborn only from " + ps.thresholdWithoutFearborn.ToStringPercent() + " psychic sensitivity.";
            string steps = ArtifactFocus.Signed(ps.perStepBonus) + " for every " + ps.stepSize.ToStringPercent() + " of sensitivity above " + ps.stepsFrom.ToStringPercent();
            string gives;
            if (ps.dilutesAcrossArms)
                gives = ArtifactFocus.Signed(ps.baseBonus) + " and " + steps + ", shared between the paths you develop by their cells (a second path dilutes the first).";
            else
                gives = ArtifactFocus.Signed(ps.baseBonus) + " to " + (ps.baseBonusCombatOnly ? "every combat path" : "every path")
                        + ", and " + steps + " to " + (ps.stepBonusAllArms ? "every path" : "the same paths") + ".";
            string below = System.Math.Abs(ps.belowThresholdPenalty) > 0.0001f
                ? " Below it: " + ArtifactFocus.Signed(ps.belowThresholdPenalty) + " to every path."
                : " Below it the heart gives nothing, and takes nothing.";
            return "Psychic: " + gives + " " + cond + below + " Depends on the wearer, so it is not in the numbers above.";
        }
    }
}
