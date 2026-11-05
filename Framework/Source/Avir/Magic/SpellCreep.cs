using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>What a meeting leaves spreads: magma runs outward, a bog creeps, steam rolls on, a storm's rain
    /// widens - ring after ring from where it was let out, until it has gone as far as its strength carries it.</summary>
    public class MapComponent_Creep : MapComponent
    {
        public class Creep : IExposable
        {
            public ComboKind kind; public Pawn caster; public IntVec3 at; public float power = 1f, r, max; public int life;
            public void ExposeData()
            {
                Scribe_Values.Look(ref kind, "kind"); Scribe_References.Look(ref caster, "caster"); Scribe_Values.Look(ref at, "at");
                Scribe_Values.Look(ref power, "power", 1f); Scribe_Values.Look(ref r, "r"); Scribe_Values.Look(ref max, "max"); Scribe_Values.Look(ref life, "life");
            }
        }

        public List<Creep> creeps = new List<Creep>();
        public MapComponent_Creep(Map map) : base(map) { }

        /// <summary>from - the radius it already covers; to - how far it will go; life - how long what it lays lasts.</summary>
        public static void Start(Map map, ComboKind k, Pawn caster, IntVec3 at, float from, float to, float power, int life)
        {
            var c = map?.GetComponent<MapComponent_Creep>();
            if (c == null || !at.InBounds(map) || to <= from) return;
            c.creeps.Add(new Creep { kind = k, caster = caster, at = at, power = power, r = from, max = Mathf.Min(to, GenRadial.MaxRadialPatternRadius - 1f), life = life });
        }

        public override void MapComponentTick()
        {
            if (creeps.Count == 0 || Find.TickManager.TicksGame % 90 != 0) return;
            foreach (Creep c in creeps.ToList())
            {
                float r0 = c.r, r1 = Mathf.Min(c.max, c.r + 1f);
                var ring = GenRadial.RadialCellsAround(c.at, r1, true).Where(x => x.InBounds(map) && x.DistanceTo(c.at) > r0).ToList();
                Lay(c, ring);
                c.r = r1;
                if (c.r >= c.max - 0.01f) creeps.Remove(c);
            }
        }

        private void Lay(Creep c, List<IntVec3> ring)
        {
            switch (c.kind)
            {
                case ComboKind.Magma:
                    foreach (IntVec3 x in ring) if (Rand.Chance(0.55f)) MagmaPool.Pour(x, map, c.caster, c.power, c.life, false);
                    if (ring.Count > 0 && Rand.Chance(0.5f)) ComboMagic.Ash(map, ring.RandomElement().ToVector3Shifted(), 1.4f);
                    break;
                case ComboKind.Bog:
                    foreach (IntVec3 x in ring) if (Rand.Chance(0.7f)) MirePatch.Spread(x, map, c.caster, c.power, c.life);
                    break;
                case ComboKind.Steam:
                    foreach (IntVec3 x in ring) if (Rand.Chance(0.3f)) Gas_ScaldingSteam.Release(x, map, 1);
                    ComboMagic.Smoke(map, ring, 0.25f);
                    break;
                case ComboKind.Storm:
                    foreach (IntVec3 x in ring) if (Rand.Chance(0.35f)) WaterMagic.Puddle(x, map);
                    // now and then the widening storm strikes someone at its edge
                    var foes = MagicFx.PawnsIn(ring, map).Where(p => c.caster != null && MagicFx.Foe(p, c.caster)).ToList();
                    if (foes.Count > 0 && Rand.Chance(0.35f))
                        using (SpellWards.Of(null, ComboKind.Storm, true)) AirMagic.Chain(c.caster, new List<IntVec3> { foes.RandomElement().Position }, map, 1, c.power * 0.6f, true);
                    break;
                case ComboKind.Haze:
                case ComboKind.Dust:
                    MapComponent_ComboFields.Raise(map, c.kind, c.caster, c.at, c.r + 1f, c.power, Mathf.Min(c.life, 900), false);
                    break;
            }
            ComboMagic.Puffs(c.kind, map, ring, 0.12f);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref creeps, "creeps", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && creeps == null) creeps = new List<Creep>();
        }
    }
}
