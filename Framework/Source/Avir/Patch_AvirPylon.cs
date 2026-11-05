using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// EXTENDS ROOF SUPPORT FOR PYLONS ONLY, WITHOUT TOUCHING THE GENERAL RULE.
    ///
    /// The general radius cannot be raised safely. Read out of the IL: 6.9 is not read from
    /// RoofCollapseUtility.RoofMaxSupportDistance in the hot path at all - the compiler inlined it
    /// as a literal into the closure methods that back WithinRangeOfRoofHolder:
    ///
    ///     &lt;&gt;c__DisplayClass2_0::&lt;WithinRangeOfRoofHolder&gt;b__1   ldc.r4 6.9
    ///
    /// So writing the static field changes nothing, and reaching the literal means transpiling
    /// compiler-generated closures whose names change between builds. Raising it globally would
    /// also double the support radius of every wall in vanilla and in all 400 other mods, which
    /// would not show up as an error - it would show up a hundred hours later as mountains that no
    /// longer collapse.
    ///
    /// None of that is necessary, because we only ever need to ADD support. WithinRangeOfRoofHolder
    /// is a plain public static method, so a postfix that turns false into true where a pylon is in
    /// range gives exactly the asked-for behaviour: vanilla rules everywhere, one exception. It can
    /// never take support away, so it cannot break another mod's building.
    /// </summary>
    public static class Patch_AvirPylonRoof
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(RoofCollapseUtility), nameof(RoofCollapseUtility.WithinRangeOfRoofHolder)),
                    postfix: new HarmonyMethod(typeof(Patch_AvirPylonRoof), nameof(WithinRange_Postfix)));

                harmony.Patch(
                    AccessTools.Method(typeof(RoofCollapseUtility), nameof(RoofCollapseUtility.ConnectedToRoofHolder)),
                    postfix: new HarmonyMethod(typeof(Patch_AvirPylonRoof), nameof(Connected_Postfix)));
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Pylon roof patches failed to apply: " + e);
            }
        }

        public static void WithinRange_Postfix(IntVec3 c, Map map, ref bool __result)
        {
            if (__result) return;
            if (Building_AvirPylon.CellIsCovered(c, map)) __result = true;
        }

        public static void Connected_Postfix(IntVec3 c, Map map, ref bool __result)
        {
            if (__result) return;
            if (Building_AvirPylon.CellIsCovered(c, map)) __result = true;
        }
    }

    /// <summary>
    /// HALVES THE COLONY WEALTH OF EVERYTHING STANDING UNDER A PYLON.
    ///
    /// THIS IS AN EXTENSION OF A VANILLA RULE, NOT A NEW ONE. WealthWatcher.CalculateWealthItems
    /// already skips anything whose PositionHeld is fogged - what the map has not revealed does not
    /// count toward raid scaling. "Hidden wealth is not wealth" is the game's own principle; the
    /// pylon just lets the Avir produce that condition deliberately.
    ///
    /// WHY ForceRecount AND NOT MarketValue. Patching Thing.MarketValue would be the obvious hook
    /// and would be a serious mistake: market value is read by trade, by stockpile sorting and by
    /// half the UI, so hiding wealth would also halve what traders pay. ForceRecount is the one
    /// place where the number means "how hard should raids be", and it is throttled by
    /// MinCountInterval rather than run per tick, so the cost of the second pass is negligible.
    ///
    /// Items and buildings are held in separate fields and are summed by different rules -
    /// MarketValue * stackCount for items, the MarketValueIgnoreHp stat for buildings - so both
    /// are discounted separately here to match.
    ///
    /// BALANCE NOTE FOR ANYONE READING THIS LATER: full coverage of a base is roughly six pylons,
    /// so this is a standing -50% to raid points. The counterweight is deliberate and is NOT in
    /// this file - it is the fuel. Each pylon burns three essence extracts per week, and every
    /// extract is a lethal siphon. Six pylons is eighteen kills a week, forever. Tune the fuel
    /// rate, not the discount.
    /// </summary>
    public static class Patch_AvirPylonWealth
    {
        private static AccessTools.FieldRef<WealthWatcher, float> itemsRef;
        private static AccessTools.FieldRef<WealthWatcher, float> buildingsRef;
        private static AccessTools.FieldRef<WealthWatcher, Map> mapRef;

        public static void Apply(Harmony harmony)
        {
            try
            {
                itemsRef = AccessTools.FieldRefAccess<WealthWatcher, float>("wealthItems");
                buildingsRef = AccessTools.FieldRefAccess<WealthWatcher, float>("wealthBuildings");
                mapRef = AccessTools.FieldRefAccess<WealthWatcher, Map>("map");

                harmony.Patch(
                    AccessTools.Method(typeof(WealthWatcher), "ForceRecount"),
                    postfix: new HarmonyMethod(typeof(Patch_AvirPylonWealth), nameof(ForceRecount_Postfix)));
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Pylon wealth patch failed to apply: " + e);
            }
        }

        public static void ForceRecount_Postfix(WealthWatcher __instance)
        {
            try
            {
                if (itemsRef == null || buildingsRef == null || mapRef == null) return;

                Map map = mapRef(__instance);
                List<Building_AvirPylon> pylons = Building_AvirPylon.PylonsOn(map);
                if (pylons == null || pylons.Count == 0) return;

                Discount(map, pylons, out float itemDiscount, out float buildingDiscount);

                if (itemDiscount > 0f) itemsRef(__instance) = Math.Max(0f, itemsRef(__instance) - itemDiscount);
                if (buildingDiscount > 0f) buildingsRef(__instance) = Math.Max(0f, buildingsRef(__instance) - buildingDiscount);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Pylon wealth discount failed: " + e, 0x4A5201);
            }
        }

        /// <summary>
        /// Walks the covered cells rather than the whole map, so the cost is bounded by how many
        /// pylons exist and not by how large the colony is.
        ///
        /// The HashSet is not optional: a wall or a workbench occupies several cells and would
        /// otherwise be discounted once per cell it stands on, which for a 5-cell building would
        /// subtract more than its whole value and drive colony wealth negative.
        /// </summary>
        private static void Discount(Map map, List<Building_AvirPylon> pylons,
            out float itemDiscount, out float buildingDiscount)
        {
            itemDiscount = 0f;
            buildingDiscount = 0f;

            HashSet<Thing> counted = new HashSet<Thing>();
            HashSet<IntVec3> visited = new HashSet<IntVec3>();
            int cellCount = GenRadial.NumCellsInRadius(Building_AvirPylon.PylonRadius);

            for (int p = 0; p < pylons.Count; p++)
            {
                Building_AvirPylon pylon = pylons[p];
                if (pylon == null || !pylon.Spawned) continue;

                for (int i = 0; i < cellCount; i++)
                {
                    IntVec3 cell = pylon.Position + GenRadial.RadialPattern[i];
                    if (!cell.InBounds(map)) continue;

                    // Overlapping pylons must not discount the same cell twice.
                    if (!visited.Add(cell)) continue;

                    // Fogged cells were never counted in the first place - discounting them would
                    // subtract wealth the colony was never charged for.
                    if (cell.Fogged(map)) continue;

                    List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                    for (int t = 0; t < things.Count; t++)
                    {
                        Thing thing = things[t];
                        if (thing == null || !counted.Add(thing)) continue;

                        if (thing.def.category == ThingCategory.Item)
                        {
                            itemDiscount += thing.MarketValue * thing.stackCount * 0.5f;
                        }
                        else if (thing.def.category == ThingCategory.Building
                                 && thing.Faction == Faction.OfPlayer)
                        {
                            buildingDiscount += thing.GetStatValue(StatDefOf.MarketValueIgnoreHp) * 0.5f;
                        }
                    }
                }
            }
        }
    }
}
