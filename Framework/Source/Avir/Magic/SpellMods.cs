using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    // =====================================================================================
    // WHERE A MODIFIER STANDS - the same law as the order of a spell: from the centre outward. A modifier works on what
    // stands just inward of it: the nearest verb or form towards the element. Written next to the element (before any
    // verb or form) it works on the whole circle - as every modifier did before.
    //   Amplify, Breadth, Anchor - on that verb or form only (or on the whole circle).
    //   Thread - turns what is inward of it round: a step draws the target in, a lift presses down, a shaping closes in.
    //   Barrier - next to the element or in the middle: a ward where the spell strikes; on the very edge of the circle,
    //             after its verbs: a ward on the mage, to cover the way back.
    //   Reach - always the whole spell: it is how far the spell is cast, not a part of its pattern.
    // =====================================================================================
    public static class SpellMods
    {
        private class Bound { public int amplify, breadth, anchor, barrier, thread; }

        private class Reading
        {
            public Bound wide = new Bound();
            public readonly Dictionary<RuneDef, Bound> own = new Dictionary<RuneDef, Bound>();
            public bool wardOnMage;
        }

        private static RitualSchema now;
        private static Reading cur;

        /// <summary>The runes of a circle in order: the element, then the sections from the centre outward.</summary>
        private static List<RuneDef> Sequence(RitualSchema s) => s?.rings.SelectMany(g => g.runes).Where(x => x != null).ToList() ?? new List<RuneDef>();

        private static bool Acts(RuneDef x) => x.role == RuneRole.Verb || x.role == RuneRole.Form;

        private static Reading ReadOf(RitualSchema s)
        {
            var rd = new Reading();
            List<RuneDef> seq = Sequence(s);
            RuneDef holder = null;
            for (int i = 0; i < seq.Count; i++)
            {
                RuneDef x = seq[i];
                if (Acts(x)) { holder = x; if (!rd.own.ContainsKey(x)) rd.own[x] = new Bound(); continue; }
                Bound b = holder == null ? rd.wide : rd.own[holder];
                switch (x.kind)
                {
                    case RuneKind.Amplify: b.amplify++; break;
                    case RuneKind.Breadth: b.breadth++; break;
                    case RuneKind.Anchor: b.anchor++; break;
                    case RuneKind.Thread: if (holder != null && !SpellHops.PairThread(seq, i)) b.thread++; break;   // after a Reach: the pair leaps, it turns nothing
                    case RuneKind.Barrier:
                        if (holder != null && i == seq.Count - 1) rd.wardOnMage = true;
                        else rd.wide.barrier++;
                        break;
                }
            }
            return rd;
        }

        /// <summary>The start of a circle's cast: its reading with only the modifiers that work on the whole circle.</summary>
        public static RitualReading Begin(RitualSchema s, RitualReading r)
        {
            now = s; cur = ReadOf(s);
            return With(r, cur.wide, true);
        }

        /// <summary>The reading for one verb or form: the whole circle's modifiers and its own.</summary>
        public static RitualReading Verb(RitualReading r, RuneDef v)
        {
            RitualReading c = VerbCore(r, v);
            // strict modifiers (the author's rule, on trial): the depth of the blow is this verb's own, not the circle's
            if (Strict && c != null && SpellWards.AtWork) SpellDepth.Amplify = c.amplify;
            return c;
        }

        private static RitualReading VerbCore(RitualReading r, RuneDef v)
        {
            if (r == null || cur == null || r.schema == null || r.schema != now || v == null || !cur.own.TryGetValue(v, out Bound b)) return r;
            if (b.amplify == 0 && b.breadth == 0 && b.anchor == 0 && b.thread == 0) return r;
            RitualReading c = With(r, new Bound(), false);
            c.amplify += b.amplify; c.breadth += b.breadth; c.anchor += b.anchor; c.thread += b.thread;
            return c;
        }

        /// <summary>The author's strict rule of modifiers, on trial (a setting).</summary>
        public static bool Strict => AvirMod.Settings != null && AvirMod.Settings.magicStrictMods;

        /// <summary>The Amplify runes beside the element: the whole circle's.</summary>
        public static int WideAmplify(RitualSchema s) => ReadOf(s).wide.amplify;

        /// <summary>What an echo carries: the modifiers of a verb's pattern - not Reach (it is how far the spell is cast, the
        /// whole spell's), nor the flow runes.</summary>
        private static bool Echoed(RuneDef x) => x.role == RuneRole.Modifier && x.kind != RuneKind.Reach;

        /// <summary>Strict modifiers: every later copy of a verb is cut out into a part of its own - the element, the
        /// circle-wide modifiers, the forms that carry it (with their own modifiers), the verb with the modifiers written
        /// after it, and the echo of the copy before it: half of what that copy came to (its own and its echo) - so it builds up;
        /// what is less than a whole rune counts as strength (Amplify) or width (Breadth). In the section of the copy before it, it also wears (the Tautology). Returns the parts and the
        /// share of strength each acts at; null when no verb repeats.</summary>
        public static List<KeyValuePair<RitualSchema, float>> Echoes(ref RitualSchema part)
        {
            if (part?.rings == null || part.rings.Count < 2) return null;
            // the runes with their sections
            var seq = new List<KeyValuePair<RuneDef, int>>();
            for (int g = 1; g < part.rings.Count; g++) foreach (RuneDef x in part.rings[g].runes) if (x != null) seq.Add(new KeyValuePair<RuneDef, int>(x, g));
            if (!seq.Where(kv => kv.Key.role == RuneRole.Verb).GroupBy(kv => kv.Key.kind).Any(z => z.Count() > 1)) return null;
            int firstAct = seq.FindIndex(kv => Acts(kv.Key));
            var wide = seq.Take(firstAct < 0 ? 0 : firstAct).Select(kv => kv.Key).Where(x => x.kind != RuneKind.Barrier).ToList();
            // each act with what is written after it
            var acts = new List<(RuneDef act, int sec, List<RuneDef> own, int at)>();
            for (int i = Mathf.Max(0, firstAct); i < seq.Count; i++)
            {
                if (!Acts(seq[i].Key)) { if (acts.Count > 0) acts[acts.Count - 1].own.Add(seq[i].Key); continue; }
                acts.Add((seq[i].Key, seq[i].Value, new List<RuneDef>(), i));
            }
            var carriers = new List<RuneDef>();
            foreach (var a in acts) if (a.act.role == RuneRole.Form && a.act.kind != RuneKind.Step) { carriers.Add(a.act); carriers.AddRange(a.own.Where(x => x.kind != RuneKind.Barrier)); }
            var outParts = new List<KeyValuePair<RitualSchema, float>>();
            var cut = new HashSet<int>();   // places in seq taken out of the main part
            // what each verb's last copy came to: its own modifiers and the echo it heard - the next copy hears half of it,
            // so the echo builds up: four releases, each with a Breadth, burst each wider than the one before
            var last = new Dictionary<RuneKind, Dictionary<RuneKind, float>>();
            var inSection = new Dictionary<(RuneKind, int), int>();
            Dictionary<RuneKind, float> Count(List<RuneDef> own) => own.Where(Echoed).GroupBy(x => x.kind).ToDictionary(g => g.Key, g => (float)g.Count());
            foreach (var a in acts)
            {
                if (a.act.role != RuneRole.Verb) continue;
                inSection.TryGetValue((a.act.kind, a.sec), out int n); inSection[(a.act.kind, a.sec)] = ++n;
                if (!last.TryGetValue(a.act.kind, out var prev)) { last[a.act.kind] = Count(a.own); continue; }
                var total = Count(a.own);
                var echo = new List<RuneDef>();
                float share = SpellWear.Share(n), wideFrac = 0f;
                foreach (var kv in prev)
                {
                    float e = kv.Value * 0.5f;
                    if (e <= 0f) continue;
                    total.TryGetValue(kv.Key, out float t); total[kv.Key] = t + e;
                    RuneDef rune = DefDatabase<RuneDef>.AllDefs.FirstOrDefault(x => x.kind == kv.Key && x.role == RuneRole.Modifier);
                    int whole = Mathf.FloorToInt(e);
                    for (int k = 0; k < whole && rune != null; k++) echo.Add(rune);
                    float frac = e - whole;
                    // what is less than a whole rune still counts: Amplify as strength, Breadth as width
                    if (kv.Key == RuneKind.Amplify) share *= 1f + 0.3f * frac;
                    else if (kv.Key == RuneKind.Breadth) wideFrac = frac;
                }
                last[a.act.kind] = total;
                cut.Add(a.at);
                for (int k = 1; k <= a.own.Count; k++) cut.Add(a.at + k);
                if (share <= 0f) continue;
                var one = new RitualSchema { name = part.name, size = part.size, echoBreadth = wideFrac };
                one.rings.Add(new RitualRing { runes = part.rings[0].runes.ToList(), slots = 1 });
                one.rings.Add(new RitualRing { runes = wide.Concat(carriers).Concat(new[] { a.act }).Concat(a.own).Concat(echo).ToList() });
                outParts.Add(new KeyValuePair<RitualSchema, float>(one, share));
            }
            if (cut.Count == 0) return null;
            // the main part without the copies
            RitualSchema main = new RitualSchema { name = part.name, size = part.size };
            main.rings.Add(part.rings[0].Copy());
            for (int g = 1; g < part.rings.Count; g++) main.rings.Add(new RitualRing { slots = part.rings[g].slots });
            for (int i = 0; i < seq.Count; i++) if (!cut.Contains(i)) main.rings[seq[i].Value].runes.Add(seq[i].Key);
            main.rings.RemoveAll(g => g != main.rings[0] && g.runes.Count == 0);
            part = main;
            return outParts;
        }

        /// <summary>A ward on the mage: a barrier on the very edge of the circle, after its verbs.</summary>
        public static bool WardOnMage => cur != null && cur.wardOnMage;

        /// <summary>Does a step in this circle turn round (a thread outward of it)?</summary>
        public static bool Pulls(RitualSchema s)
        {
            Reading rd = ReadOf(s);
            return rd.own.Any(kv => kv.Key.kind == RuneKind.Step && kv.Value.thread > 0);
        }

        /// <summary>Does a shaping in this circle close in (a thread outward of it)?</summary>
        public static bool Squeezes(RitualSchema s)
        {
            Reading rd = ReadOf(s);
            return rd.own.Any(kv => kv.Key.kind == RuneKind.Shape && kv.Value.thread > 0);
        }

        /// <summary>The modifiers written on one verb or form of a circle (outward of it, before the next one), and
        /// those beside the element that work on the whole circle.</summary>
        public static void Own(RitualSchema s, RuneKind kind, out int amplify, out int breadth, out int anchor, out int thread)
        {
            Reading rd = ReadOf(s);
            amplify = rd.wide.amplify; breadth = rd.wide.breadth; anchor = rd.wide.anchor; thread = 0;
            foreach (var kv in rd.own)
            {
                if (kv.Key.kind != kind) continue;
                amplify += kv.Value.amplify; breadth += kv.Value.breadth; anchor += kv.Value.anchor; thread += kv.Value.thread;
            }
        }

        /// <summary>In words: which modifier works on which verb or form, when not on the whole circle.</summary>
        public static List<string> Bindings(RitualSchema s)
        {
            Reading rd = ReadOf(s);
            var lines = new List<string>();
            foreach (var kv in rd.own)
            {
                var parts = new List<string>();
                if (kv.Value.amplify > 0) parts.Add("amplify" + (kv.Value.amplify > 1 ? " ×" + kv.Value.amplify : ""));
                if (kv.Value.breadth > 0) parts.Add("breadth" + (kv.Value.breadth > 1 ? " ×" + kv.Value.breadth : ""));
                if (kv.Value.anchor > 0) parts.Add("anchor" + (kv.Value.anchor > 1 ? " ×" + kv.Value.anchor : ""));
                if (kv.Value.thread > 0) parts.Add(kv.Key.kind == RuneKind.Step ? "thread (the step turns round)" : kv.Key.kind == RuneKind.Shape ? "thread (the wall closes in)" : kv.Key.kind == RuneKind.Lift ? "thread (it presses down instead)" : "thread");
                if (parts.Count > 0) lines.Add(string.Join(", ", parts) + " - on " + kv.Key.label + " only");
            }
            if (rd.wardOnMage) lines.Add("barrier on the edge - a ward on the caster");
            return lines;
        }

        private static RitualReading With(RitualReading r, Bound b, bool replace)
        {
            var c = new RitualReading
            {
                schema = r.schema, verbs = r.verbs, elements = r.elements, reach = r.reach, safety = r.safety, closing = r.closing,
                focus = r.focus, cost = r.cost, instability = r.instability, actWeight = r.actWeight,
                amplify = replace ? b.amplify : r.amplify, breadth = replace ? b.breadth : r.breadth, anchor = replace ? b.anchor : r.anchor,
                barrier = replace ? b.barrier : r.barrier, thread = replace ? b.thread : r.thread
            };
            return c;
        }
    }
}
