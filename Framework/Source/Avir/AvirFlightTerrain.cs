using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// WHICH GROUND IS WORTH LEAVING. One answer, shared by takeoff, landing and route look-ahead.
    ///
    /// WHY THIS FILE EXISTS AT ALL. AvirAutoFlight used to decide by path cost alone: anything
    /// costing 20 or more counted as terrain worth flying over. That test was written against
    /// walls, boulders and marsh, and it quietly failed on the one case Maximus actually cares
    /// about - rivers.
    ///
    /// The reason is that 1.6 made water SWIMMABLE. A swimmable cell is not impassable and its
    /// walking cost is no longer the whole story: much of the slowness a pawn suffers in a river
    /// now comes from the swim, not from the cell's number in the path grid. So the cost test could
    /// read a perfectly ordinary figure for a river cell, decide the crossing did not need wings,
    /// and let the pawn wade in. Which is exactly what was observed.
    ///
    /// Water is therefore named DIRECTLY here, not inferred from a cost. Cost is kept as a second,
    /// independent reason - marsh, mud and rubble still count - but water no longer depends on it.
    ///
    /// HOW WATER IS RECOGNISED, AND WHY IT IS NOT HARDCODED. TerrainDef exposes an IsWater
    /// property, but the exact member has moved across versions and this mod has already lost
    /// several compile round trips to vanilla names recalled rather than read. So the property is
    /// looked up by reflection ONCE, at first use, and every TerrainDef in the game is classified
    /// there and then into a set that is never written to again. If the property is missing, two
    /// fallbacks run instead - driesTo, then the def name. Lookups afterwards are a HashSet read
    /// with no reflection and no allocation, and the finished set is immutable, so the pathfinder
    /// threads can read it safely.
    /// </summary>
    public static class AvirFlightTerrain
    {
        /// <summary>
        /// A walking cost this high or worse counts as terrain worth flying over on its own.
        /// Ordinary ground reads 2 and light rough ground 14 in this game's grid - both measured in
        /// the probe output, not assumed - while marsh and rubble climb past that, and impassable
        /// cells report 10000.
        ///
        /// Set above 14 so that merely rough ground does not launch a pawn into the air for a
        /// two-cell trip.
        /// </summary>
        public const int HeavyTerrainCost = 20;

        private static readonly object buildLock = new object();
        private static HashSet<TerrainDef> waterTerrains;

        /// <summary>
        /// Classifies every terrain in the game exactly once. Called lazily rather than at startup
        /// because this file must not care where in the loading order it sits: the first pawn to
        /// ask a question is guaranteed to be later than def loading.
        /// </summary>
        private static void EnsureBuilt()
        {
            if (waterTerrains != null) return;

            lock (buildLock)
            {
                if (waterTerrains != null) return;

                HashSet<TerrainDef> set = new HashSet<TerrainDef>();

                PropertyInfo isWaterProp = null;
                FieldInfo driesToField = null;

                try
                {
                    isWaterProp = typeof(TerrainDef).GetProperty("IsWater",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (isWaterProp != null && isWaterProp.PropertyType != typeof(bool))
                        isWaterProp = null;

                    driesToField = typeof(TerrainDef).GetField("driesTo",
                        BindingFlags.Public | BindingFlags.Instance);
                }
                catch (Exception e)
                {
                    Log.Warning("[Avir flight] terrain reflection failed, falling back to names: " + e);
                }

                List<TerrainDef> all = DefDatabase<TerrainDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    TerrainDef t = all[i];
                    if (t == null) continue;
                    if (Classify(t, isWaterProp, driesToField)) set.Add(t);
                }

                Log.Message("[Avir flight] water terrains recognised: " + set.Count
                    + " (IsWater property " + (isWaterProp != null ? "found" : "MISSING, using fallback")
                    + ").");

                // Published last, and only once fully populated: readers never see a half-built set.
                waterTerrains = set;
            }
        }

        private static bool Classify(TerrainDef t, PropertyInfo isWaterProp, FieldInfo driesToField)
        {
            // 1. The engine's own answer, when it exists.
            if (isWaterProp != null)
            {
                try
                {
                    if ((bool)isWaterProp.GetValue(t, null)) return true;
                }
                catch
                {
                    // Fall through to the cheaper tests rather than losing the whole classification.
                }
            }

            // 2. Anything that can dry out is standing water of some kind - shallow water, mud,
            //    a marsh tile - even if the property above is unavailable.
            if (driesToField != null)
            {
                try
                {
                    if (driesToField.GetValue(t) != null) return true;
                }
                catch
                {
                }
            }

            // 3. Last resort. Names, not costs, because a cost test is precisely what failed.
            string n = t.defName;
            if (string.IsNullOrEmpty(n)) return false;

            return n.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Marsh", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Ocean", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Bog", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Swamp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsWater(TerrainDef t)
        {
            if (t == null) return false;
            EnsureBuilt();
            return waterTerrains.Contains(t);
        }

        /// <summary>Water underfoot, of any depth: river, lake, sea, marsh, flooded ground.</summary>
        public static bool IsWaterAt(Map map, IntVec3 c)
        {
            if (map?.terrainGrid == null || !c.InBounds(map)) return false;

            // TerrainAt is the surface the pawn walks on; a bridge over water reports the bridge,
            // which is correct - a bridged crossing needs no wings.
            return IsWater(map.terrainGrid.TerrainAt(c));
        }

        /// <summary>
        /// The full test. THREE independent reasons, any one of which is enough:
        ///
        ///   - the cell cannot be walked into at all (wall, rock, deep water in older terms);
        ///   - the cell is water, whatever the pathfinder charges for it;
        ///   - the cell simply costs a lot to cross on foot.
        ///
        /// The middle clause is the fix. The other two are the old behaviour, kept intact.
        /// </summary>
        public static bool WorthFlyingOver(Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map)) return false;

            PathGrid grid = map.pathing?.Normal?.pathGrid;
            if (grid == null) return false;

            // AN OBSTACLE IS ONLY WORTH WINGS IF WINGS ACTUALLY GET PAST IT.
            //
            // This used to be a flat "unwalkable means fly". Now that the shell of a roofed
            // building is closed to flight, that answer would launch an Avir into the air to cross
            // an interior wall it is not permitted to cross - it would then be routed around
            // through the door anyway and land again a few seconds later, having achieved a short
            // hover. Asking AvirFlightRules keeps the takeoff decision and the routing decision
            // holding the same rule.
            if (!grid.Walkable(c)) return !AvirFlightRules.BlocksFlight(map, c);
            if (IsWaterAt(map, c)) return true;

            // CalculatedCostAt rather than the baked array, because the same call is what the probe
            // printed and its numbers are therefore known-good in this version.
            return grid.CalculatedCostAt(c, false, IntVec3.Invalid, null) >= HeavyTerrainCost;
        }
    }
}
