using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    // =====================================================================================
    // THE ORDER OF A SPELL - the same law as the rituals': everything flows from the centre outward, and what comes
    // first feeds what comes after.
    //   Circles: from the heart outward - the circles round the heart first (clockwise from the top, as they stand),
    //     then the circles inside the great ones. They go one after another: each waits until the one before it has
    //     ended (the mage has come up out of the ground or landed, the drawn-in have arrived) and strikes where it
    //     ended - by the mage after a step, by the mage after an aura or a pull, at the target otherwise.
    //     Darkness and Space turn it the other way: counter-clockwise from the top.
    //   Runes in a circle: from the element outward. Verbs work in that order; a drain that has verbs after it feeds
    //     them at once (as the last, it is kept for the next spell).
    // =====================================================================================
    public class MapComponent_SpellChain : MapComponent
    {
        private const int Gap = 20;          // between two circles that end at once: the eye sees the order
        private const int Patience = 900;    // the longest a circle waits for the one before it

        private class Chain
        {
            public Pawn caster;
            public SpellCircle root;
            public List<SpellCircle> links;
            public int next;
            public float power;
            public bool storm;
            public IntVec3 wallTo = IntVec3.Invalid;
            public LocalTargetInfo target;
            public int at;
            public bool waitCaster;
            public List<Pawn> waitFor;
            public int giveUp;
            public bool endByMage;
        }

        private readonly List<Chain> chains = new List<Chain>();

        public MapComponent_SpellChain(Map map) : base(map) { }

        /// <summary>Darkness and Space turn the spell the other way: its chain runs counter-clockwise. (So will a
        /// distorted path, once it is made.)</summary>
        public static bool Widdershins(SpellCircle root) =>
            root.All().Any(c => c.element != null && (c.element.kind == RuneKind.Darkness || c.element.kind == RuneKind.Space));

        /// <summary>The circles that act, in the order they act.</summary>
        public static List<SpellCircle> Order(SpellCircle root)
        {
            if (!root.binding) return WeaveSpell.Small(root);
            bool back = Widdershins(root);
            var list = Turn(root.inner, back).Where(c => !c.Great).ToList();
            foreach (SpellCircle g in Turn(root.inner, back).Where(c => c.Great)) list.AddRange(Turn(g.inner, back).Where(c => !c.Great));
            return list.Where(c => c.element != null).ToList();
        }

        /// <summary>The circles of a ring from the top: clockwise as they stand, or counter-clockwise (the top one
        /// still first, then the one to its left).</summary>
        private static IEnumerable<SpellCircle> Turn(List<SpellCircle> ring, bool back)
        {
            if (!back || ring.Count < 2) return ring;
            return new[] { ring[0] }.Concat(Enumerable.Range(1, ring.Count - 1).Reverse().Select(i => ring[i]));
        }

        public static void Start(Pawn caster, SpellCircle root, LocalTargetInfo target, float power)
        {
            var c = new Chain
            {
                caster = caster, root = root, links = Order(root), power = power, target = target,
                storm = WeaveSpell.Storm(root), wallTo = FireMagic.WallTo
            };
            // the first circle at once - a spell of one circle is over in this very moment
            Run(c);
            if (c.next < c.links.Count || Waiting(c)) caster.Map?.GetComponent<MapComponent_SpellChain>()?.chains.Add(c);
            else Finish(c);
        }

        private static bool Waiting(Chain c) => c.waitCaster || c.waitFor != null;

        private static void Run(Chain c)
        {
            if (c.next >= c.links.Count) return;
            SpellCircle circle = c.links[c.next++];
            Pawn caster = c.caster;
            if (caster == null || caster.Dead || !caster.Spawned) return;
            float p = c.power * (1f + 0.25f * (WeaveSpell.Resonance(c.root, circle.element) - 1));
            RitualSchema one = circle.AsSchema();
            RitualReading r = RitualEvaluator.Read(one);
            AirMagic.Storm = c.storm;
            FireMagic.WallTo = c.wallTo;
            SpellDash.LastPulled = null;
            try
            {
                if (FireMagic.Is(circle.element)) FireMagic.Cast(caster, one, r, c.target, p);
                else if (WaterMagic.Is(circle.element)) WaterMagic.Cast(caster, one, r, c.target, p);
                else if (EarthMagic.Is(circle.element)) EarthMagic.Cast(caster, one, r, c.target, p);
                else if (AirMagic.Is(circle.element)) AirMagic.Cast(caster, one, r, c.target, p);
            }
            finally { AirMagic.Storm = false; FireMagic.WallTo = IntVec3.Invalid; }
            // where this circle ends, the next one strikes
            bool step = r.verbs.Any(v => v.kind == RuneKind.Step);
            bool aura = r.verbs.Any(v => v.kind == RuneKind.Aura);
            int now = Find.TickManager.TicksGame;
            c.waitCaster = step && r.thread == 0 && !caster.Spawned;
            c.waitFor = step && r.thread > 0 && SpellDash.LastPulled != null && SpellDash.LastPulled.Count > 0 ? SpellDash.LastPulled : null;
            c.endByMage = aura || (step && r.thread > 0);
            if (!c.waitCaster && !c.endByMage && !step) c.target = new LocalTargetInfo(c.target.Cell);
            if (!c.waitCaster && c.endByMage) c.target = new LocalTargetInfo(caster.Position);
            c.giveUp = now + Patience;
            c.at = now + Gap;
            SpellDash.LastPulled = null;
        }

        private static void Finish(Chain c)
        {
            if (c.caster == null || c.caster.Dead || !c.caster.Spawned) return;
            WeaveSpell.AfterCircles(c.caster, c.root, c.target, c.power, c.caster.Map);
        }

        public override void MapComponentTick()
        {
            if (chains.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = chains.Count - 1; i >= 0; i--)
            {
                Chain c = chains[i];
                if (c.caster == null || c.caster.Dead) { chains.RemoveAt(i); continue; }
                if (Waiting(c))
                {
                    bool done = now >= c.giveUp
                                || (c.waitCaster && c.caster.Spawned)
                                || (c.waitFor != null && c.waitFor.All(p => p.Dead || p.Spawned));
                    if (!done) continue;
                    if (c.waitCaster) c.target = new LocalTargetInfo(c.caster.Position);
                    else if (c.endByMage) c.target = new LocalTargetInfo(c.caster.Position);
                    c.waitCaster = false; c.waitFor = null;
                    c.at = now + Gap;
                    continue;
                }
                if (now < c.at || !c.caster.Spawned || c.caster.Map != map) continue;
                if (c.next >= c.links.Count) { chains.RemoveAt(i); Finish(c); continue; }
                Run(c);
            }
        }
    }
}
