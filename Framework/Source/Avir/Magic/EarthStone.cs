using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Earth's own rule - stone remains: what earth spells leave (rubble, chunks, walls, mounds) lies on, and the
    /// next earth spell takes up the stone at hand - stronger and cheaper. Like water's ground of wet, only of stone.
    /// Its meetings take the same rule from their own ground: magma from magma and basalt, bog from mire and pools,
    /// dust from rubble, chunks and shards. Loose stone is flung and gone; rock, basalt and walls stay and give half.
    /// Counted only when a spell is cast, in three cells round its target - nothing is watched tick by tick.</summary>
    public static class EarthStone
    {
        public const float Radius = 3f, MaxUnits = 10f, PowerPer = 0.05f, CostPer = 0.03f;

        private static ThingDef rubble;
        public static ThingDef Rubble => rubble ?? (rubble = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpellRubble"));

        /// <summary>Is this element's ground stone at all (earth, magma, bog, dust)?</summary>
        public static bool Heavy(ComboKind k) => k == ComboKind.None || k == ComboKind.Magma || k == ComboKind.Bog || k == ComboKind.Dust;

        private static bool Loose(ComboKind k, Thing t)
        {
            // what the colony keeps in its stockpiles and shelves is not the ground's: it is never taken
            if (t.def.category == ThingCategory.Item && t.IsInAnyStorage()) return false;
            switch (k)
            {
                case ComboKind.Magma: return t is MagmaPool;
                case ComboKind.Bog: return t is MirePatch || t.def.defName == "Avir_SpellPuddle" || t.def == ThingDefOf.Filth_Water;
                default:
                    if (t.def == Rubble || t.def == ThingDefOf.Filth_RubbleRock || t.def.defName == "Filth_RubbleBuilding") return true;
                    if (t.def.thingCategories != null && t.def.thingCategories.Contains(ThingCategoryDefOf.StoneChunks)) return true;
                    return k == ComboKind.Dust && t is ShardGround;
            }
        }

        private static float Weight(ComboKind k, Thing t)
        {
            if (Loose(k, t)) return t.def.thingCategories != null && t.def.thingCategories.Contains(ThingCategoryDefOf.StoneChunks) ? 2f : 1f;
            // what stays where it is: rock (basalt among it) and stone walls - half; dust cannot lift rock
            if (k == ComboKind.Dust || k == ComboKind.Bog) return 0f;
            if (t.def.building != null && t.def.building.isNaturalRock) return 0.5f;
            if (k == ComboKind.None && t is EarthWall) return 0.5f;
            return 0f;
        }

        /// <summary>How much stone is at hand round a point, up to ten.</summary>
        public static float Units(Map map, IntVec3 at, ComboKind k)
        {
            if (map == null || !at.InBounds(map) || !Heavy(k)) return 0f;
            float u = 0f;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, Radius, true))
            {
                if (!c.InBounds(map)) continue;
                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count && u < MaxUnits; i++) u += Weight(k, things[i]);
                if (u >= MaxUnits) return MaxUnits;
            }
            return u;
        }

        /// <summary>The spell takes up the stone at hand: the loose is flung at the target and gone. Returns its strength (1 + 5% a unit).</summary>
        public static float Gather(Map map, IntVec3 at, ComboKind k)
        {
            float u = Units(map, at, k);
            if (u <= 0f) return 1f;
            float taken = 0f;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, Radius, true))
            {
                if (!c.InBounds(map) || taken >= MaxUnits) continue;
                foreach (Thing t in c.GetThingList(map).Where(t => Loose(k, t)).ToList())
                {
                    if (taken >= MaxUnits) break;
                    taken += Weight(k, t);
                    FleckMaker.ThrowDustPuffThick(t.DrawPos, map, 1f, k == ComboKind.Magma ? new Color(1f, 0.4f, 0.1f, 0.6f) : k == ComboKind.Bog ? new Color(0.35f, 0.4f, 0.2f, 0.6f) : MagicFx.EarthDust);
                    t.Destroy();
                }
            }
            return 1f + PowerPer * u;
        }

        /// <summary>The kind of ground a spell's first heavy circle draws on, or null if none of it is heavy.</summary>
        public static ComboKind? KindOf(RitualSchema s)
        {
            if (s == null) return null;
            if (s.weave == null) return EarthMagic.Is(Spells.Element(s)) ? ComboKind.None : (ComboKind?)null;
            var pairs = Combos.Pairs(s.weave);
            foreach (SpellCircle c in MapComponent_SpellChain.Order(s.weave))
            {
                ComboKind k = pairs.TryGetValue(c, out ComboKind m) ? m : ComboKind.None;
                if (k == ComboKind.None ? EarthMagic.Is(c.element) : Heavy(k)) return k;
            }
            return null;
        }

        /// <summary>The Arcana a heavy spell asks with stone at its target: 3% less a unit, up to 30%.</summary>
        public static float CostFactor(Map map, IntVec3 at, RitualSchema s)
        {
            ComboKind? k = KindOf(s);
            return k.HasValue ? 1f - CostPer * Units(map, at, k.Value) : 1f;
        }

        public static string Ground(ComboKind k) =>
            k == ComboKind.Magma ? "magma and basalt" : k == ComboKind.Bog ? "mire and pools of water" : k == ComboKind.Dust ? "rubble, stone chunks and shards" : "rubble, stone chunks, rock and stone walls";
    }
}
