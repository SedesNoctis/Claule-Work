using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The textbook checked by itself (the author, Fix158: "with thousands of combinations I cannot test it all"):
    /// a dev action that writes every meeting - each pair, each of the 24 second-order meetings, each meeting of Life, and
    /// the reading-order cases of the rules - reads them back as the game does, and says what did not come out as the
    /// rules say. Nothing is cast; the codex text of each is made too, so a spell that would throw is caught here.</summary>
    public static class MagicSelfTest
    {
        private static RuneDef R(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);

        /// <summary>"Fire:Release|Barrier,Barrier" - an element and its sections (| between sections).</summary>
        private static SpellCircle C(string spec)
        {
            string[] p = spec.Split(':');
            var k = new SpellCircle { element = R(p[0]) };
            foreach (string sec in (p.Length > 1 ? p[1] : "Release").Split('|'))
            {
                var runes = sec.Split(',').Select(x => R(x.Trim())).Where(x => x != null).ToList();
                k.sections.Add(new RitualRing { runes = runes, slots = Math.Max(1, runes.Count) });
            }
            return k;
        }

        private static SpellCircle Weave(params string[] circles)
        {
            var root = new SpellCircle { binding = true };
            foreach (string c in circles) root.inner.Add(C(c));
            return root;
        }

        private static string Name(RuneKind k) => k.ToString();

        [LudeonTK.DebugAction("Avir magic", "Magic: self-test of every meeting", allowedGameStates = LudeonTK.AllowedGameStates.Playing)]
        public static void Run()
        {
            var bad = new List<string>();
            int n = 0;
            void Check(string what, bool ok, string got) { n++; if (!ok) bad.Add(what + " -> " + got); }
            void Explains(string what, SpellCircle w)
            {
                try { SpellText.Explain(new RitualSchema { name = what, weave = w }); }
                catch (Exception e) { bad.Add(what + " -> the codex text throws: " + e.GetType().Name + " " + e.Message); }
            }
            var smalls = new[] { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air };

            // 1. every pair, both ways round
            foreach (RuneKind a in smalls) foreach (RuneKind b in smalls)
            {
                if (a == b) continue;
                SpellCircle w = Weave(Name(a), Name(b));
                var got = Combos.Pairs(w).Values.Distinct().ToList();
                ComboKind want = Combos.Of(a, b);
                Check("pair " + a + "+" + b, got.Count == 1 && got[0] == want, string.Join(",", got));
                Explains("pair " + a + "+" + b, w);
                // a Closing in the first circle's outermost section keeps them apart
                SpellCircle sealedW = Weave(Name(a) + ":Release|Closing", Name(b));
                Check("sealed " + a + "|" + b, Combos.Pairs(sealedW).Count == 0, string.Join(",", Combos.Pairs(sealedW).Values));
            }

            // 2. every second-order meeting: the pair, then its third
            foreach (SecondKind y in Enum.GetValues(typeof(SecondKind)).Cast<SecondKind>().Where(y => y != SecondKind.None))
            {
                RuneKind[] pr = Combos.Parts(SecondOrder.PairOf(y));
                RuneKind third = SecondOrder.Third(y);
                SpellCircle w = Weave(Name(pr[0]), Name(pr[1]), Name(third));
                var got = SecondOrder.Of(w).Values.Distinct().ToList();
                Check("second " + y + " (" + pr[0] + "+" + pr[1] + " then " + third + ")", got.Count == 1 && got[0] == y, string.Join(",", got));
                Explains("second " + y, w);
                // the third before the pair meets it too
                SpellCircle w2 = Weave(Name(third), Name(pr[0]), Name(pr[1]));
                var got2 = SecondOrder.Of(w2).Values.Distinct().ToList();
                bool firstPairs = Combos.Of(third, pr[0]) != ComboKind.None;   // then the third pairs first, by the rule
                Check("second " + y + " third first", firstPairs || (got2.Count == 1 && got2[0] == y), string.Join(",", got2) + (firstPairs ? "" : ""));
            }

            // 3. the order of reading (the author's cases): pairs first, from the start; a third joins the pair before it
            {
                SpellCircle w = Weave("Fire", "Earth", "Fire", "Fire", "Water");
                var pairs = Combos.Pairs(w).Values.Distinct().OrderBy(x => x).ToList();
                var sec = SecondOrder.Of(w).Values.Distinct().ToList();
                Check("Fire,Earth,Fire,Fire,Water = magma (lava) + steam", pairs.SequenceEqual(new[] { ComboKind.Steam, ComboKind.Magma }.OrderBy(x => x)) && sec.Count == 1 && sec[0] == SecondKind.Lava,
                    "pairs " + string.Join(",", pairs) + "; second " + string.Join(",", sec));
                SpellCircle w2 = Weave("Fire", "Earth", "Fire", "Water", "Fire");
                var p2 = Combos.Pairs(w2).Values.Distinct().OrderBy(x => x).ToList();
                var s2 = SecondOrder.Of(w2).Values.Distinct().ToList();
                Check("Fire,Earth,Fire,Water,Fire = magma + steam (inferno)", p2.SequenceEqual(new[] { ComboKind.Steam, ComboKind.Magma }.OrderBy(x => x)) && s2.Count == 1 && s2[0] == SecondKind.Inferno,
                    "pairs " + string.Join(",", p2) + "; second " + string.Join(",", s2));
                SpellCircle w3 = Weave("Fire", "Earth", "Fire:Release|Closing", "Water", "Fire");
                var s3 = SecondOrder.Of(w3).Values.Distinct().OrderBy(x => x).ToList();
                Check("Fire,Earth,Fire|Closing,Water,Fire = lava + inferno", s3.SequenceEqual(new[] { SecondKind.Lava, SecondKind.Inferno }.OrderBy(x => x)), string.Join(",", s3));
            }

            // 4. the meetings of Life: a great circle of Life, an element inside
            foreach (RuneKind k in smalls)
            {
                var box = new SpellCircle { element = R("Life") };
                box.sections.Add(new RitualRing { runes = new List<RuneDef> { R("Drain") }.Where(x => x != null).ToList(), slots = 1 });
                box.inner.Add(C(Name(k)));
                var root = new SpellCircle { binding = true };
                root.inner.Add(box);
                RuneKind got = LifeMagic.PairOf(root, box);
                Check("life + " + k, got == k, got.ToString());
                Explains("life + " + k, root);
            }

            var sb = new StringBuilder();
            sb.AppendLine("Magic self-test: " + n + " checks, " + bad.Count + " not as the rules say.");
            foreach (string b in bad) sb.AppendLine("  - " + b);
            Log.Message("[Avir] " + sb);
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }
    }
}
