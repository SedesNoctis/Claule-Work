using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
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

        /// <summary>What a blast of the caster's passes over: in a shaped blow every one of their own people within reach
        /// of it, the caster too; unshaped, the caster stands in their own blast unwarded at their peril.</summary>
        public static List<Thing> Ignored(Pawn caster, Map map, IntVec3 at, float radius, bool keepCaster = false)
        {
            var l = new List<Thing>();
            if (caster != null && (keepCaster || Active)) l.Add(caster);
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

    /// <summary>How deep a blow goes through armour does not come from piling power on (circles of one element, focus -
    /// those make it stronger, not deeper): from Amplify runes on its own verbs, each less than the last (1 + the square
    /// root of their number), the map's stream and stone at hand; no hard ceiling but the safety cap in the settings. And it is
    /// deepest at the heart of the blow: towards its edge, by half (Магия-правила.md).</summary>
    public static class SpellDepth
    {
        public static int Amplify;
        public static IntVec3 Heart = IntVec3.Invalid;
        public static float Reach = 0f;

        /// <summary>Stone at hand under an earthen blow (earth, magma, bog, dust): it strikes with the rock about it.</summary>
        public static float Stone = 1f;

        /// <summary>Amplify: 1 + the square root of their number (1 - x2, 4 - x3, 9 - x4); the stream: +15% a tier; stone at
        /// hand: x1.2. A caster at their peak - nine Amplify on one verb, a strong stream, rock about - breaks a cataphract's plate.</summary>
        public static float Factor(Map map) => Mathf.Min(Mathf.Max(1f, AvirMod.Settings.magicPenDepthMax),
            (1f + Mathf.Sqrt(Mathf.Max(0, Amplify))) * (1f + 0.15f * Spells.FlowTier(map)) * Stone);

        /// <summary>The share of depth left where the struck one stands: 1 at the heart, half at the edge and past it.</summary>
        public static float At(IntVec3 c)
        {
            if (!Heart.IsValid || Reach <= 0.5f || !c.IsValid) return 1f;
            return Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(c.DistanceTo(Heart) / Reach));
        }
    }

    /// <summary>The wear of a repeated verb (Магия-правила.md, author's rule): the same verb written again in one section
    /// wears - the second acts at half, the third at a quarter, the rest not at all (they still cost Arcana). In
    /// different sections it does not wear: each section is its own act. Spread a verb over the sections, don't pile it.</summary>
    public static class SpellWear
    {
        public static float Share(int copy) => copy <= 1 ? 1f : copy == 2 ? 0.5f : copy == 3 ? 0.25f : 0f;

        /// <summary>Takes the repeats within a section out of a part (the first of each verb stays in place) and returns
        /// them as parts of their own - the element, the delivery and the modifiers of that section, with that one verb -
        /// and the share each acts at. Null when nothing wears.</summary>
        public static List<KeyValuePair<RitualSchema, float>> Split(ref RitualSchema part)
        {
            if (part?.rings == null || part.rings.Count < 2) return null;
            if (!part.rings.Skip(1).Any(g => g.runes.Where(x => x != null && x.role == RuneRole.Verb).GroupBy(x => x.kind).Any(z => z.Count() > 1))) return null;
            RitualSchema main = part.Copy();
            var repeats = new List<KeyValuePair<RitualSchema, float>>();
            foreach (RitualRing g in main.rings.Skip(1))
            {
                // what carries a repeat: the forms of the section (not a step - the caster does not step again) and its modifiers
                var carry = g.runes.Where(x => x != null && ((x.role == RuneRole.Form && x.kind != RuneKind.Step) || x.role == RuneRole.Modifier)).ToList();
                var seen = new Dictionary<RuneKind, int>();
                for (int n = 0; n < g.runes.Count; n++)
                {
                    RuneDef x = g.runes[n];
                    if (x == null || x.role != RuneRole.Verb) continue;
                    seen.TryGetValue(x.kind, out int c);
                    seen[x.kind] = ++c;
                    if (c == 1) continue;
                    g.runes.RemoveAt(n--);
                    float share = Share(c);
                    if (share <= 0f) continue;
                    var one = new RitualSchema { name = part.name, size = part.size };
                    one.rings.Add(new RitualRing { runes = part.rings[0].runes.ToList(), slots = 1 });
                    one.rings.Add(new RitualRing { runes = carry.Concat(new[] { x }).ToList() });
                    repeats.Add(new KeyValuePair<RitualSchema, float>(one, share));
                }
            }
            main.rings.RemoveAll(g => g != main.rings[0] && g.runes.Count == 0);
            part = main;
            return repeats;
        }

        /// <summary>For the codex: the verbs that wear in a circle's sections, and how much of them is left.</summary>
        public static string Explain(SpellCircle c)
        {
            if (c?.sections == null) return null;
            var lines = new List<string>();
            for (int i = 0; i < c.sections.Count; i++)
                foreach (var g in c.sections[i].runes.Where(x => x != null && x.role == RuneRole.Verb).GroupBy(x => x.kind).Where(z => z.Count() > 1))
                {
                    float sum = 0f; int dead = 0;
                    for (int n = 1; n <= g.Count(); n++) { float s = Share(n); sum += s; if (s <= 0f) dead++; }
                    lines.Add("Section " + (i + 1) + ": " + g.First().LabelCap + " x" + g.Count() + " acts as x" + sum.ToString("0.##") + (dead > 0 ? " (" + dead + " worn to nothing)" : ""));
                }
            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }
    }
}
