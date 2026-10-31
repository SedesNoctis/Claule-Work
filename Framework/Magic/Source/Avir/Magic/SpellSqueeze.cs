using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Magic
{
    /// <summary>Shaping turned round by the Thread (Магия-правила.md, "Нить"): the wall does not stand - it closes. A
    /// ring of stone rises round the target and steps inward a cell at a time; whoever stands where the next ring
    /// rises is shoved inside, and at the last step the stone meets in the middle and crushes whoever is caught there.
    /// The caster's own stone: the shut-in foe breaks at it to get out, and a wall broken leaves a gap in every ring after.</summary>
    public class MapComponent_Squeeze : MapComponent
    {
        public class Squeeze : IExposable
        {
            public Pawn caster;
            public Faction of;
            public IntVec3 center;
            public float radius, power;
            public int next, step;
            public bool magma, shaped;
            public List<IntVec3> walls = new List<IntVec3>();
            public List<IntVec3> broken = new List<IntVec3>();   // directions (from the centre) the foe broke through: a gap stays there

            public void ExposeData()
            {
                Scribe_References.Look(ref caster, "caster");
                Scribe_References.Look(ref of, "of");
                Scribe_Values.Look(ref center, "center");
                Scribe_Values.Look(ref radius, "radius");
                Scribe_Values.Look(ref power, "power", 1f);
                Scribe_Values.Look(ref next, "next");
                Scribe_Values.Look(ref step, "step", 150);
                Scribe_Values.Look(ref magma, "magma");
                Scribe_Values.Look(ref shaped, "shaped");
                Scribe_Collections.Look(ref walls, "walls", LookMode.Value);
                Scribe_Collections.Look(ref broken, "broken", LookMode.Value);
                if (walls == null) walls = new List<IntVec3>();
                if (broken == null) broken = new List<IntVec3>();
            }
        }

        public List<Squeeze> all = new List<Squeeze>();

        public MapComponent_Squeeze(Map map) : base(map) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref all, "squeezes", LookMode.Deep);
            if (all == null) all = new List<Squeeze>();
        }

        /// <summary>The ring a squeeze starts from: three cells out and one more for each Breadth.</summary>
        public static float StartRadius(int breadth) => 3f + breadth;

        /// <summary>A ring of stone round the point that will close on it. Holding and anchors slow it (a firmer grip,
        /// a slower, heavier close); power makes the last blow.</summary>
        public static void Begin(Pawn caster, Map map, IntVec3 center, float radius, float power, bool magma, int slow)
        {
            var c = map?.GetComponent<MapComponent_Squeeze>();
            if (c == null || !center.InBounds(map)) return;
            var q = new Squeeze
            {
                caster = caster, of = caster?.Faction, center = center, radius = radius, power = power, magma = magma,
                shaped = SpellSculpt.Active, step = 150 + 60 * slow, next = Find.TickManager.TicksGame + 150 + 60 * slow
            };
            c.all.Add(q);
            c.Raise(q);
        }

        public override void MapComponentTick()
        {
            if (all.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Squeeze q = all[i];
                if (now < q.next) continue;
                q.next = now + q.step;
                if (!Close(q)) all.RemoveAt(i);
            }
        }

        /// <summary>One step inward. False when it has met in the middle.</summary>
        private bool Close(Squeeze q)
        {
            // what the foe broke since the last step: that side stays open from now on
            foreach (IntVec3 c in q.walls)
                if (!(c.GetEdifice(map) is EarthWall)) q.broken.Add(c - q.center);
            Lower(q, false);
            q.radius -= 1f;
            if (q.radius < 0.5f) { Crush(q); return false; }
            Raise(q);
            return q.walls.Count > 0 || q.radius >= 1f;
        }

        /// <summary>A gap broken in a ring stays a gap: the cells of the new ring on the same side are not raised.</summary>
        private static bool OnBrokenSide(Squeeze q, IntVec3 c)
        {
            if (q.broken.Count == 0) return false;
            Vector3 d = (c - q.center).ToVector3();
            if (d.sqrMagnitude < 0.01f) return false;
            d.Normalize();
            foreach (IntVec3 b in q.broken)
            {
                Vector3 e = b.ToVector3();
                if (e.sqrMagnitude < 0.01f) continue;
                if (Vector3.Dot(d, e.normalized) > 0.92f) return true;
            }
            return false;
        }

        private void Raise(Squeeze q)
        {
            q.walls.Clear();
            List<IntVec3> ring = FireMagic.Ring(q.center, map, q.radius);
            // whoever stands where the stone rises is shoved inside - the ring does not part for them
            using (q.shaped ? SpellSculpt.Spare() : null)
                foreach (IntVec3 c in ring)
                {
                    if (OnBrokenSide(q, c)) continue;
                    foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList())
                    {
                        if (!MagicFx.Foe(p, q.caster)) { SpellHurl.Throw(p, q.center, 2, map); continue; }   // a friend is let out, not in
                        SpellHurl.Throw(p, q.center, -1, map, q.caster, q.power);
                    }
                }
            foreach (IntVec3 c in ring)
            {
                if (OnBrokenSide(q, c)) continue;
                if (!c.InBounds(map) || c.GetEdifice(map) != null || c.Impassable(map)) continue;
                Pawn stuck = c.GetFirstPawn(map);
                if (stuck != null)
                {
                    // too big or too firmly held to be moved: the stone grinds against them where they stand, and leaves the cell open
                    using (q.shaped ? SpellSculpt.Spare() : null)
                        if (MagicFx.Foe(stuck, q.caster)) SpellHurl.Squash(stuck, q.caster, q.power * 0.4f, 1, false);
                    continue;
                }
                EarthWall.Raise(c, map, 2500, q.of);
                if (c.GetEdifice(map) is EarthWall)
                {
                    q.walls.Add(c);
                    if (q.magma) FleckMaker.ThrowFireGlow(c.ToVector3Shifted(), map, 1f);
                }
            }
            if (q.walls.Count > 0) SoundDefOf.Building_Complete.PlayOneShot(new TargetInfo(q.center, map));
        }

        private void Lower(Squeeze q, bool rubble)
        {
            foreach (IntVec3 c in q.walls)
            {
                if (!(c.GetEdifice(map) is EarthWall w)) continue;
                w.Destroy(DestroyMode.Vanish);
                FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.2f, MagicFx.EarthDust);
                if (rubble) MagicFx.Rubble(c, map);
            }
            q.walls.Clear();
        }

        /// <summary>The stone meets: whoever is caught in the middle is crushed by it - through any plate, by their size
        /// and their weight; magma burns them as it closes.</summary>
        private void Crush(Squeeze q)
        {
            var cells = FireMagic.Point(q.center, map, 1.5f);
            using (q.shaped ? SpellSculpt.Spare() : null)
                foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                {
                    if (!MagicFx.Foe(p, q.caster)) continue;
                    SpellHurl.Squash(p, q.caster, q.power, 3, true);
                    if (q.magma && !p.Dead) p.TakeDamage(new DamageInfo(DamageDefOf.Flame, 8f * q.power, 0f, -1f, q.caster));
                }
            foreach (IntVec3 c in cells)
            {
                FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 2.2f, MagicFx.EarthDust);
                if (Rand.Chance(0.5f)) MagicFx.Rubble(c, map);
                if (q.magma && Rand.Chance(0.4f)) FireUtility.TryStartFireIn(c, map, 0.4f, q.caster);
            }
            FleckMaker.Static(q.center, map, FleckDefOf.PsycastAreaEffect, 2.4f);
        }
    }
}
