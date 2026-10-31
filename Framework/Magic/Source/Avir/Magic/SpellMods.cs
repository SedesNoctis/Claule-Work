using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using Verse;

namespace Avir.Magic
{
    // =====================================================================================
    // WHERE A MODIFIER STANDS - the same law as the order of a spell: from the centre outward. A modifier works on what
    // stands just inward of it: the nearest verb or form towards the element. Written next to the element (before any
    // verb or form) it works on the whole circle - as every modifier did before.
    //   Amplify, Breadth, Anchor - on that verb or form only (or on the whole circle).
    //   Thread - only outward of a step: it turns the step round. Anywhere else it does nothing.
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
                    case RuneKind.Thread: if (holder != null) b.thread++; break;
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
            if (r == null || cur == null || r.schema == null || r.schema != now || v == null || !cur.own.TryGetValue(v, out Bound b)) return r;
            if (b.amplify == 0 && b.breadth == 0 && b.anchor == 0 && b.thread == 0) return r;
            RitualReading c = With(r, new Bound(), false);
            c.amplify += b.amplify; c.breadth += b.breadth; c.anchor += b.anchor; c.thread += b.thread;
            return c;
        }

        /// <summary>A ward on the mage: a barrier on the very edge of the circle, after its verbs.</summary>
        public static bool WardOnMage => cur != null && cur.wardOnMage;

        /// <summary>Does a step in this circle turn round (a thread outward of it)?</summary>
        public static bool Pulls(RitualSchema s)
        {
            Reading rd = ReadOf(s);
            return rd.own.Any(kv => kv.Key.kind == RuneKind.Step && kv.Value.thread > 0);
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
                if (kv.Value.thread > 0) parts.Add(kv.Key.kind == RuneKind.Step ? "thread (the step turns round)" : "thread");
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
