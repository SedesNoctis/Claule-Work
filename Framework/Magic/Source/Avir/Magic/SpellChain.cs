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

        private class Chain : IExposable
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

            public void ExposeData()
            {
                Scribe_References.Look(ref caster, "caster");
                Scribe_Deep.Look(ref root, "root");
                Scribe_Values.Look(ref next, "next");
                Scribe_Values.Look(ref power, "power", 1f);
                Scribe_Values.Look(ref storm, "storm");
                Scribe_Values.Look(ref wallTo, "wallTo", IntVec3.Invalid);
                Scribe_TargetInfo.Look(ref target, "target");
                Scribe_Values.Look(ref at, "at");
                Scribe_Values.Look(ref waitCaster, "waitCaster");
                Scribe_Collections.Look(ref waitFor, "waitFor", LookMode.Reference);
                Scribe_Values.Look(ref giveUp, "giveUp");
                Scribe_Values.Look(ref endByMage, "endByMage");
                if (Scribe.mode == LoadSaveMode.PostLoadInit && root != null) links = Order(root);
            }
        }

        private List<Chain> chains = new List<Chain>();

        /// <summary>A chain broken off by a save goes on after the load, from the circle it had reached.</summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref chains, "chains", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (chains == null) chains = new List<Chain>();
                chains.RemoveAll(c => c == null || c.root == null || c.links == null);
                foreach (Chain c in chains) c.waitFor?.RemoveAll(p => p == null);
            }
        }

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
            finally { AirMagic.Storm = false; FireMagic.WallTo = IntVec3.Invalid; MagicFx.Unburn(caster); }
            // where this circle ends, the next one strikes
            bool step = r.verbs.Any(v => v.kind == RuneKind.Step);
            bool aura = r.verbs.Any(v => v.kind == RuneKind.Aura);
            int now = Find.TickManager.TicksGame;
            bool pulls = SpellMods.Pulls(one);
            c.waitCaster = step && !pulls && !caster.Spawned;
            c.waitFor = step && pulls && SpellDash.LastPulled != null && SpellDash.LastPulled.Count > 0 ? SpellDash.LastPulled : null;
            c.endByMage = aura || (step && pulls);
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

        /// <summary>While a chain runs, everyone sees where its next circle will strike - time to raise a ward or run.</summary>
        public override void MapComponentUpdate()
        {
            if (chains.Count == 0 || Find.CurrentMap != map) return;
            foreach (Chain c in chains)
            {
                if (c.next >= c.links.Count || c.caster == null || c.caster.Dead) continue;
                IntVec3 mageAt = c.caster.Spawned ? c.caster.Position : c.target.Cell;
                IntVec3 at = c.waitCaster || c.endByMage ? mageAt : c.target.Cell;
                if (!at.InBounds(map)) continue;
                float widest = 0f;
                Color col = c.links[c.next].element?.color ?? Color.white;
                var cells = SpellPreview.LinkCells(c.caster, c.links[c.next].AsSchema(), at, mageAt, map, col, ref widest);
                if (cells.Count > 0) GenDraw.DrawFieldEdges(cells.Where(x => x.InBounds(map)).Distinct().ToList(), col);
            }
        }

        public override void MapComponentTick()
        {
            if (chains.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = chains.Count - 1; i >= 0; i--)
            {
                Chain c = chains[i];
                if (c.caster == null || c.caster.Dead) { chains.RemoveAt(i); continue; }
                if (now % 10 == 0) MagicFx.Unburn(c.caster);
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
