using System.Collections.Generic;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Blood filth doesn't naturally disappear (same as any vanilla filth - it only clears via
    /// rain or sweeping), and we deliberately boosted its Flammability so fire actually catches on
    /// it. Combined, that means an ignited blood pool would otherwise sit there as permanent,
    /// endlessly re-ignitable fuel - real fire consumes what it burns, so ours needs to as well.
    /// Called the instant a cell is successfully ignited, both from the initial Combustion blast
    /// and from the spreader's ongoing neighbor-ignition.
    /// </summary>
    public static class CombustionUtility
    {
        public static void ConsumeBloodFilthAt(IntVec3 cell, Map map, HashSet<ThingDef> bloodFilthDefs)
        {
            if (map == null || bloodFilthDefs == null || bloodFilthDefs.Count == 0) return;
            if (!cell.InBounds(map)) return;

            List<Thing> things = cell.GetThingList(map);
            List<Thing> toDestroy = null;

            for (int i = 0; i < things.Count; i++)
            {
                if (bloodFilthDefs.Contains(things[i].def))
                {
                    if (toDestroy == null) toDestroy = new List<Thing>();
                    toDestroy.Add(things[i]);
                }
            }

            if (toDestroy == null) return;

            // Collected first, then destroyed - destroying while iterating the live cell list
            // would be unsafe.
            foreach (Thing t in toDestroy)
            {
                if (!t.Destroyed) t.Destroy(DestroyMode.Vanish);
            }
        }
    }
}
