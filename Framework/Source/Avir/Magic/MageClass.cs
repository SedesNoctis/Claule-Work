using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The caster's class (Магия-правила.md, the author's decisions): a main element for everyone - the path a
    /// ritual gives, or for a sorcerer the element born with; experience in each element apart, from casting (and, later,
    /// rituals and magic work) and a little from time; the more experienced in an element, the stronger, steadier and
    /// quicker its spells. A meeting of two elements is not on the scale - its experience is shared between the two.
    /// A wizard learns every element alike, a little better their own (x1, x1.1); a sorcerer is born strong in theirs and
    /// learns it fastest (x1.2), the others badly (x0.5). The level of the core is a hidden skill grown from all of it; each
    /// level brings a point of the path (what the points buy is still to be laid out - the constellation of paths).</summary>
    public static class MageClass
    {
        public static readonly string[] StageNames = { "Novice", "Apprentice", "Adept", "Expert", "Master", "Grandmaster" };
        public static readonly float[] StageAt = { 0f, 500f, 2000f, 5000f, 12000f, 25000f };
        // what each stage does to a spell of that element: strength, the chance of a slip, the time of the cast
        private static readonly float[] PowerAt = { 0.85f, 0.95f, 1f, 1.1f, 1.2f, 1.3f };
        private static readonly float[] SlipAt = { 1.2f, 1.05f, 1f, 0.9f, 0.8f, 0.7f };
        private static readonly float[] CastAt = { 1.15f, 1.05f, 1f, 0.9f, 0.8f, 0.7f };

        /// <summary>A born sorcerer starts as an adept in the element born with.</summary>
        public const float SorcererStart = 2000f;
        /// <summary>A failed hold still teaches - half as much.</summary>
        public const float SlipShare = 0.5f;

        public static float Pace => AvirMod.Settings != null ? AvirMod.Settings.magicXpPace : 1f;

        public static int Stage(float xp) { int i = 0; while (i < StageAt.Length - 1 && xp >= StageAt[i + 1]) i++; return i; }
        public static string StageName(float xp) => StageNames[Stage(xp)];

        /// <summary>The main element: the path a ritual gave; a sorcerer is born with one.</summary>
        public static RuneDef Main(Hediff_ArcaneCore c)
        {
            if (c == null) return null;
            if (c.path == null && c.Sorcerer)
            {
                List<RuneDef> made = MagePath.Made;
                if (made.Count > 0)
                {
                    c.path = made.RandomElement();
                    if (XpIn(c, c.path.kind) < SorcererStart) c.xp[c.path.kind] = SorcererStart;
                    c.Note("Born to " + c.path.label + ".");
                }
            }
            return c.path;
        }

        public static float XpIn(Hediff_ArcaneCore c, RuneKind k) => c != null && c.xp.TryGetValue(k, out float v) ? v : 0f;

        /// <summary>How much of a gain the mage takes in this element.</summary>
        public static float Learns(Hediff_ArcaneCore c, RuneKind k)
        {
            bool own = Main(c)?.kind == k;
            return c.Sorcerer ? (own ? 1.2f : 0.5f) : (own ? 1.1f : 1f);
        }

        /// <summary>Experience into one element (raw: before the mage's learning and the pace).</summary>
        public static void Gain(Hediff_ArcaneCore c, RuneKind k, float raw)
        {
            if (c == null || raw <= 0f) return;
            float before = XpIn(c, k);
            float after = before + raw * Learns(c, k) * Pace;
            c.xp[k] = after;
            if (Stage(after) > Stage(before) && c.pawn.Faction == Faction.OfPlayer)
                Messages.Message(c.pawn.LabelShortCap + " is now " + Article(StageName(after)) + " of " + Label(k) + ".", c.pawn, MessageTypeDefOf.PositiveEvent, false);
            Grow(c);
        }

        private static string Article(string s) => ("AEIOU".IndexOf(s[0]) >= 0 ? "an " : "a ") + s.ToLower();

        public static string Label(RuneKind k) => DefDatabase<RuneDef>.AllDefsListForReading.FirstOrDefault(r => r.role == RuneRole.Element && r.kind == k)?.label ?? k.ToString().ToLower();

        /// <summary>The level of the core: grown from all the experience - 250 for the second, then each a little dearer
        /// (level = 1 + sqrt(all / 250)). It never falls; a core grown otherwise (a ritual) keeps its level.</summary>
        public static int LevelOf(float all) => 1 + Mathf.FloorToInt(Mathf.Sqrt(Mathf.Max(0f, all) / 250f));
        public static float ForLevel(int level) => 250f * (level - 1) * (level - 1);

        public static float All(Hediff_ArcaneCore c) => c.xp.Values.Sum();

        public static void Grow(Hediff_ArcaneCore c)
        {
            int l = LevelOf(All(c));
            if (l <= c.level) return;
            int got = l - c.level;
            c.level = l;
            c.points += got;
            GameComponent_ArcaneMemory.Remember(c.pawn, c.level);
            c.Note("The core grew to level " + l + ".");
            if (c.pawn.Faction == Faction.OfPlayer)
                Messages.Message(c.pawn.LabelShortCap + "'s arcane core grew to level " + l + ": the reserve is deeper, and a point of the path is free.", c.pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>The elements a spell is cast in: one share for each circle (a meeting of two circles gives one to each).</summary>
        public static List<RuneKind> Shares(RitualSchema s)
        {
            if (s.weave != null) return WeaveSpell.Small(s.weave).Where(k => k.element != null).Select(k => k.element.kind).ToList();
            return RitualEvaluator.Read(s).elements.Select(e => e.kind).ToList();
        }

        /// <summary>A spell cast: its Arcana, twice over, is the experience - shared among its circles' elements.</summary>
        public static void Cast(Hediff_ArcaneCore c, RitualSchema s, float cost, bool slipped)
        {
            if (c == null || s == null) return;
            if (c.path == null && !c.Sorcerer)
            {
                // a wizard who walks no path yet: the first element they cast in becomes their own
                RuneDef first = Spells.Element(s);
                if (first != null && first.role == RuneRole.Element) { c.path = first; c.Note("Took " + first.label + " for their own."); }
            }
            List<RuneKind> sh = Shares(s);
            if (sh.Count == 0) return;
            float each = 2f * cost * (slipped ? SlipShare : 1f) / sh.Count;
            foreach (RuneKind k in sh) Gain(c, k, each);
        }

        /// <summary>What the experience does to a spell: every element in it adds its own good and its own bad
        /// (the author, Fix118) - not an average. Each distinct element brings its stage's departure from 1; an element
        /// the mage has never touched (no experience at all) weighs a little worse than a novice's. Clamped.</summary>
        private static float Over(Hediff_ArcaneCore c, RitualSchema s, float[] table, float unknown, float min, float max)
        {
            if (c == null || s == null) return 1f;
            List<RuneKind> sh = Shares(s).Distinct().ToList();
            if (sh.Count == 0) return 1f;
            float f = 1f;
            foreach (RuneKind k in sh) { float xp = XpIn(c, k); f += table[Stage(xp)] - 1f + (xp <= 0f ? unknown : 0f); }
            return Mathf.Clamp(f, min, max);
        }

        public static float PowerFactor(Hediff_ArcaneCore c, RitualSchema s) => Over(c, s, PowerAt, -0.05f, 0.4f, 2.2f);
        public static float SlipFactor(Hediff_ArcaneCore c, RitualSchema s) => Over(c, s, SlipAt, 0.15f, 0.4f, 3f);
        public static float CastFactor(Hediff_ArcaneCore c, RitualSchema s) => Over(c, s, CastAt, 0.1f, 0.5f, 2.5f);

        /// <summary>Time: a mage keeps thinking in their own element - a little each day.</summary>
        public static void Days(Hediff_ArcaneCore c, float days)
        {
            RuneDef m = Main(c);
            if (m != null) Gain(c, m.kind, 10f * days);
        }

        /// <summary>The lines of the path page: what the mage is in each element, and what it does.</summary>
        public static string Explain(Hediff_ArcaneCore c)
        {
            RuneDef m = Main(c);
            var lines = new List<string>
            {
                (c.Sorcerer ? "Sorcerer" : "Wizard") + " · main element: " + (m != null ? m.LabelCap.ToString() : "none yet (the first cast decides)"),
                "Core level " + c.level + " · free points of the path: " + c.points + " · reserve " + Mathf.FloorToInt(c.Capacity) + " Arcana (a point not wanted on a path may go into the reserve: +" + (100f * ArcanaIntake.PerPoint).ToString("0") + "% of the first)",
                ""
            };
            foreach (RuneDef e in MagePath.Made)
            {
                float x = XpIn(c, e.kind); int st = Stage(x);
                string next = st < StageAt.Length - 1 ? " / " + StageAt[st + 1].ToString("0") : "";
                lines.Add(e.LabelCap + ": " + StageNames[st] + " (" + x.ToString("0") + next + ") · power x" + PowerAt[st].ToString("0.##")
                    + " · slip x" + SlipAt[st].ToString("0.##") + " · cast x" + CastAt[st].ToString("0.##") + " · learns x" + Learns(c, e.kind).ToString("0.#"));
            }
            lines.Add("");
            lines.Add("A meeting of two elements is not on the scale: its experience goes to both, shared.");
            return string.Join("\n", lines);
        }
    }
}
