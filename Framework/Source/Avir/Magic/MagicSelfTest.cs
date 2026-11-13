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

        [LudeonTK.DebugAction("Magic", "Magic: self-test of every meeting", allowedGameStates = LudeonTK.AllowedGameStates.Playing)]
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

            // 0. every meeting of three keeps a short name of its own, and an explanation that is not its name (Fix255: an
            // explanation written over the name twice - the typhoon's, then the dry wind's and the marsh haze's - floated
            // whole paragraphs over the battlefield instead of a word)
            foreach (SecondKind y in Enum.GetValues(typeof(SecondKind)).Cast<SecondKind>().Where(y => y != SecondKind.None))
            {
                string lb = SecondOrder.Label(y), ds = SecondOrder.Text(y);
                Check("name of " + y, !lb.NullOrEmpty() && lb.Length <= 20 && !lb.Contains(":") && !lb.Contains("."), lb);
                Check("explanation of " + y, !ds.NullOrEmpty() && ds.Length > 40 && ds != lb, ds.NullOrEmpty() ? "none" : ds.Substring(0, Math.Min(30, ds.Length)));
            }

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
                // the fourth of an element already in it strengthens it, not a new meeting
                SpellCircle w2 = Weave(Name(pr[0]), Name(pr[1]), Name(third), Name(pr[0]));
                var got2 = SecondOrder.Of(w2);
                Check("second " + y + " + " + pr[0] + " strengthens", got2.Count == 4 && got2.Values.All(x => x == y), got2.Count + " circles in it");
            }

            // 3. the order of reading (the author, Fix159): in order from the start; the same element again strengthens
            void Case(string what, string[] circles, ComboKind[] pairsWant, SecondKind[] secWant)
            {
                SpellCircle w = Weave(circles);
                var ms = Combos.Read(w);
                var p = ms.Where(m => m.pair != ComboKind.None && m.second == SecondKind.None).Select(m => m.pair).OrderBy(x => x).ToList();
                var y = ms.Where(m => m.second != SecondKind.None).Select(m => m.second).OrderBy(x => x).ToList();
                Check(what, p.SequenceEqual(pairsWant.OrderBy(x => x)) && y.SequenceEqual(secWant.OrderBy(x => x)),
                    string.Join(" / ", ms.Select(m => m.second != SecondKind.None ? m.second.ToString() + " x" + m.circles.Count : m.pair != ComboKind.None ? m.pair.ToString() + " x" + m.circles.Count : m.first + " alone x" + m.circles.Count)));
                Explains(what, w);
            }
            Case("Fire,Earth,Fire,Fire,Water = lava (strengthened) + lone water", new[] { "Fire", "Earth", "Fire", "Fire", "Water" }, new ComboKind[0], new[] { SecondKind.Lava });
            Case("Fire,Earth,Fire,[Closing]Fire,Water = lava + steam", new[] { "Fire", "Earth", "Fire", "Fire:Closing,Release", "Water" }, new[] { ComboKind.Steam }, new[] { SecondKind.Lava });
            Case("Fire,Earth,Fire,Water,Fire = lava + steam", new[] { "Fire", "Earth", "Fire", "Water", "Fire" }, new[] { ComboKind.Steam }, new[] { SecondKind.Lava });
            Case("Fire,Fire,Earth = magma (strengthened)", new[] { "Fire", "Fire", "Earth" }, new[] { ComboKind.Magma }, new SecondKind[0]);
            Case("Fire,[Closing]Earth = nothing met", new[] { "Fire", "Earth:Closing,Release" }, new ComboKind[0], new SecondKind[0]);
            Case("Fire[Closing at the end],Earth = nothing met", new[] { "Fire:Release,Closing", "Earth" }, new ComboKind[0], new SecondKind[0]);
            Case("Fire,Earth,[Closing]Fire = magma + lone fire", new[] { "Fire", "Earth", "Fire:Closing,Release" }, new[] { ComboKind.Magma }, new SecondKind[0]);

            // 4. the meetings of Life: a great circle of Life, an element inside
            foreach (RuneKind k in smalls)
            {
                var box = new SpellCircle { element = R("Life") };
                box.sections.Add(new RitualRing { runes = new List<RuneDef> { R("Drain") }.Where(x => x != null).ToList(), slots = 1 });
                box.inner.Add(C(Name(k)));
                var root = new SpellCircle { binding = true };
                root.inner.Add(box);
                // one circle: the wide meeting (Warmth, Sap, Frame, Breath); two: the deep one (Fix165)
                RuneKind wide = LifeMagic.AspectOf(root, box), deep0 = LifeMagic.PairOf(root, box);
                Check("life + " + k + " = " + LifeMagic.AspectName(k), wide == k && deep0 == LifeMagic.None, "wide " + wide + ", deep " + deep0);
                Explains("life + " + k, root);
                box.inner.Add(C(Name(k)));
                RuneKind deep = LifeMagic.PairOf(root, box);
                Check("life + " + k + " + " + k + " = " + LifeMagic.PairName(k), deep == k && LifeMagic.AspectOf(root, box) == LifeMagic.None, "deep " + deep);
                Explains("life + " + k + " + " + k, root);
            }

            // 5. the mixed triples (Fix249): two different elements in an unsealed Life box meet as Life's mix, never as a
            // plain pair (water + air there is Nerve, not a storm); sealed, they meet as themselves
            foreach (RuneKind a in smalls) foreach (RuneKind b in smalls)
            {
                if ((int)a >= (int)b) continue;
                var box = new SpellCircle { element = R("Life") };
                box.sections.Add(new RitualRing { runes = new List<RuneDef> { R("Drain") }.Where(x => x != null).ToList(), slots = 1 });
                box.inner.Add(C(Name(a))); box.inner.Add(C(Name(b)));
                var root = new SpellCircle { binding = true };
                root.inner.Add(box);
                LifeMix mx = LifeMagic.MixOf(root, box);
                Check("life + " + a + " + " + b + " = mix, no plain pair", mx == LifeMagic.Mix(a, b) && Combos.Pairs(root).Count == 0,
                    "mix " + mx + ", pairs " + string.Join(",", Combos.Pairs(root).Values));
                box.sections[0].runes.Add(R("Closing"));
                Check("sealed life + " + a + " + " + b + " = the plain pair", LifeMagic.MixOf(root, box) == LifeMix.None && Combos.Pairs(root).Values.Contains(Combos.Of(a, b)),
                    "pairs " + string.Join(",", Combos.Pairs(root).Values));
            }

            // 6. healing grammar (Fix258): Restoration is a verb; alone it heals at the point by the element,
            // beside a form it stays a verb of its own, and it never stands for "strike"
            RuneDef heal = R("Restore"), rel = R("Release"), beam = R("Beam");
            Check("restoring is a verb", heal != null && heal.role == RuneRole.Verb, heal == null ? "no rune" : heal.role.ToString());
            if (heal != null && rel != null && beam != null)
            {
                var v1 = new List<RuneDef> { heal }; var f1 = new List<RuneDef>();
                SpellHeal.AsVerb(v1, f1);
                Check("lone restoring heals at the point", v1.Count == 0 && f1.Count == 1 && f1[0] == heal, v1.Count + " verbs, " + f1.Count + " forms");
                var v2 = new List<RuneDef> { heal, rel }; var f2 = new List<RuneDef> { beam };
                SpellHeal.AsVerb(v2, f2);
                Check("restoring beside a form stays a verb", v2.Contains(heal) && f2.Count == 1, v2.Count + " verbs, " + f2.Count + " forms");
                Check("only heals", SpellHeal.OnlyHeals(new List<RuneDef> { heal }) && !SpellHeal.OnlyHeals(new List<RuneDef> { heal, rel }), "");
            }

            // 7. the pure forms of Light (Fix262): the first small circle of a form's element goes into Light; it meets no one
            if (R("Light") != null)
            {
                SpellCircle LightBox(params string[] inner)
                {
                    var box = new SpellCircle { element = R("Light") };
                    box.sections.Add(new RitualRing { runes = new List<RuneDef> { R("Release") }.Where(x => x != null).ToList(), slots = 1 });
                    foreach (string i in inner) box.inner.Add(C(i));
                    return box;
                }
                var b1 = LightBox("Water", "Fire"); var r1 = new SpellCircle { binding = true }; r1.inner.Add(b1);
                Check("light + water = Mirror", LightMagic.Mirror(b1) && !LightMagic.Crystal(b1), "form " + LightMagic.FormCircle(b1)?.element?.defName);
                Check("Mirror's water makes no steam", Combos.Pairs(r1).Count == 0, string.Join(",", Combos.Pairs(r1).Values));
                var b2 = LightBox("Earth", "Water"); var r2 = new SpellCircle { binding = true }; r2.inner.Add(b2);
                Check("light + earth + water = Crystal (the first decides)", LightMagic.Crystal(b2) && !LightMagic.Mirror(b2), "form " + LightMagic.FormCircle(b2)?.element?.defName);
            }

            var sb = new StringBuilder();
            sb.AppendLine("Magic self-test: " + n + " checks, " + bad.Count + " not as the rules say.");
            foreach (string b in bad) sb.AppendLine("  - " + b);
            Log.Message("[Avir] " + sb);
            Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
        }
    }
}
