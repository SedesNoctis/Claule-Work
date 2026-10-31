using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using Verse;

namespace Avir.Magic
{
    /// <summary>A spell strikes whoever stands in it, friend or foe, unless a ward keeps them (Магия-правила.md). Two
    /// Barriers in the outermost section of a circle shape its blow round the caster's own people: it strikes foes only,
    /// and is weaker for it. Each circle needs its own pair; a circle without it, or with it written elsewhere, strikes
    /// everyone as usual - how the circle is built is the caster's business.</summary>
    public static class SpellSculpt
    {
        /// <summary>The share of strength a shaped blow keeps.</summary>
        public const float Keeps = 0.75f;

        private static int depth;
        /// <summary>A shaped blow is being dealt now: the caster's own people are spared.</summary>
        public static bool Active => depth > 0;

        private sealed class Scope : IDisposable { public void Dispose() { if (depth > 0) depth--; } }
        public static IDisposable Spare() { depth++; return new Scope(); }

        private static bool Marked(RitualRing outer) => outer?.runes != null && outer.runes.Count(x => x != null && x.kind == RuneKind.Barrier) >= 2;

        /// <summary>Is this circle shaped (two Barriers in its outermost section)?</summary>
        public static bool Of(SpellCircle c) => c?.sections != null && c.sections.Count > 0 && Marked(c.sections[c.sections.Count - 1]);

        /// <summary>A circle's schema (element ring first, then its sections): if shaped, the two Barriers are taken out
        /// - they shape the blow, they lay no ward - and true is returned.</summary>
        public static bool Take(RitualSchema s)
        {
            if (s?.rings == null || s.rings.Count < 2) return false;
            RitualRing outer = s.rings[s.rings.Count - 1];
            if (!Marked(outer)) return false;
            for (int n = 0; n < 2; n++) outer.runes.RemoveAt(outer.runes.FindLastIndex(x => x != null && x.kind == RuneKind.Barrier));
            if (outer.runes.Count == 0) s.rings.RemoveAt(s.rings.Count - 1);
            return true;
        }

        /// <summary>The pair put back on the outermost ring of a part cut off by a step: it is shaped too.</summary>
        public static void Mark(RitualSchema s)
        {
            if (s?.rings == null) return;
            RuneDef b = DefDatabase<RuneDef>.AllDefs.FirstOrDefault(x => x.kind == RuneKind.Barrier);
            if (b == null) return;
            if (s.rings.Count < 2) s.rings.Add(new RitualRing { runes = new List<RuneDef>() });
            RitualRing outer = s.rings[s.rings.Count - 1];
            outer.runes.Add(b); outer.runes.Add(b);
            outer.slots = Math.Max(outer.Slots, outer.runes.Count);
        }

        /// <summary>What a blast of the caster's passes over: the caster (if asked), and in a shaped blow every one of
        /// their own people within reach of it.</summary>
        public static List<Thing> Ignored(Pawn caster, Map map, IntVec3 at, float radius, bool keepCaster = true)
        {
            var l = new List<Thing>();
            if (caster != null && keepCaster) l.Add(caster);
            if (Active && caster != null && map != null)
                foreach (Pawn p in GenRadial.RadialDistinctThingsAround(at, map, radius + 1f, true).OfType<Pawn>())
                    if (p != caster && !p.HostileTo(caster)) l.Add(p);
            return l;
        }

        /// <summary>A copy of the spell with the shaping pairs taken out of every circle, for reading what it does.</summary>
        public static RitualSchema Stripped(RitualSchema s, out int shaped, out int plain)
        {
            shaped = plain = 0;
            if (s?.weave == null) return s;
            RitualSchema c = s.Copy();
            foreach (SpellCircle k in c.weave.All())
            {
                if (k.binding || k.sections == null || k.sections.Count == 0) continue;
                if (!Of(k)) { if (k.element != null) plain++; continue; }
                shaped++;
                RitualRing outer = k.sections[k.sections.Count - 1];
                for (int n = 0; n < 2; n++) outer.runes.RemoveAt(outer.runes.FindLastIndex(x => x != null && x.kind == RuneKind.Barrier));
            }
            return c;
        }
    }
}
