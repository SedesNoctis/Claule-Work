using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// Keeps an airborne Avir moving at its own walking speed instead of paying a wall's path cost.
    ///
    /// WHAT THIS FILE USED TO BE. It began as the roof rule, blocking roofed cells at
    /// GenGrid.WalkableBy and BuildingBlockingNextPathCell. That approach deadlocked - the route was
    /// planned as if the pawn phased through everything and then the movement gate refused the very
    /// cells it had chosen - and the roof rule now lives in Patch_AvirPhasingGate, which decides
    /// once per path instead. Those two patches are disabled below and only the speed clamp remains.
    ///
    /// WHY THE CLAMP IS NEEDED AT ALL. Pawn_PathFollower.CostToMoveIntoCell adds the target cell's
    /// own path cost to the pawn's base move time, and a wall's path cost is 10000. So a phasing
    /// pawn pays ten thousand ticks to enter a cell it is supposedly passing through freely. VEF
    /// zeroes the wall costs the PATHFINDER uses, but never this one - and this one is what the pawn
    /// actually moves at.
    ///
    /// Clamped to the pawn's own walking speed rather than zeroed: flight already ignores terrain
    /// through the Floating comp, and making it faster than walking would turn it into a movement
    /// upgrade rather than a way past obstacles.
    /// </summary>
    public static class Patch_AvirFlightSpeed
    {

        /// <summary>Set the first time an airborne Avir enters a water cell. See the comment in
        /// CostToMoveIntoCell_Postfix - this exists to answer one question once.</summary>
        private static bool loggedWaterCost;

        public static void Apply(Harmony harmony)
        {
            try
            {
                // THE ROOF BLOCK IS OFF, AND IT HAD TO COME OFF.
                //
                // Blocking entry to roofed cells produced a deadlock: VEF plans the route as if the
                // pawn phases through everything, then my rule refused the very cells that route
                // chose, so the pawn stopped in front of them and asked for the same route again.
                // Standing still forever is worse than passing through a roof.
                //
                // The rule cannot live at the movement gate at all. Refusing a cell AFTER the route
                // committed to it can only ever deadlock; the route itself has to know. That means
                // reaching into how VEF builds the path request, which is the next piece of work
                // rather than a line I can flip here.
                //
                // The methods below are kept, unpatched, so turning this back on later is one
                // uncommented block rather than a rewrite.

                // SPEED. This is what made crossing a wall take an age.
                //
                // Pawn_PathFollower.CostToMoveIntoCell adds the cell's own path cost to the pawn's
                // base move time, and a wall's path cost is 10000 - so an airborne Avir paid ten
                // thousand ticks to enter a cell it was supposedly passing through freely. VEF
                // zeroes the wall costs the PATHFINDER uses, but not this one, which is what the
                // pawn actually walks at.
                MethodInfo cost = AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell",
                    new[] { typeof(Pawn), typeof(IntVec3) });
                if (cost != null)
                {
                    harmony.Patch(cost,
                        postfix: new HarmonyMethod(typeof(Patch_AvirFlightSpeed),
                            nameof(CostToMoveIntoCell_Postfix)));
                }

                Log.Message("[Avir] Flight speed clamp bound: " + (cost != null)
                    + ". Roof restriction is currently OFF - see the comment in Apply().");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Flight roof rule failed to apply: " + e);
            }
        }

        /// <summary>
        /// Only pawns airborne by OUR hediff. A pawn phasing because of an amulet is somebody
        /// else's rule and must not be narrowed by ours.
        /// </summary>
        private static bool IsAvirFlying(Pawn pawn)
        {
            // ONE INTEGER COMPARISON FOR THE COMMON CASE.
            //
            // This method runs on EVERY cell step of EVERY pawn on the map. It used to open with a
            // hediff-list walk, which is what took PatherTick from 1.63 to 11.9 microseconds per
            // call whenever an Avir caravan was present - see AvirFlightRegistry for the numbers.
            //
            // Almost always nobody is flying, and that case now costs a count check.
            if (AvirFlightRegistry.NobodyFlying) return false;
            if (pawn == null) return false;

            // Somebody IS up. A hash lookup answers whether it is this pawn, so the rest of the
            // colony still avoids the scan.
            return AvirFlightRegistry.Contains(pawn);
        }

        /// <summary>
        /// Roofed means solid to a flyer. Thin rock roof does not count - a low outcrop is
        /// something you fly over - while constructed roofs and overhead mountain do.
        /// </summary>
        public static bool Blocked(Map map, IntVec3 c)
        {
            if (map?.roofGrid == null || !c.InBounds(map)) return false;

            // ANY roof is a ceiling. The thin-natural-rock exemption that used to be here was the
            // door into an Ancient Danger - those sit under "Rock roof (thin)".
            RoofDef roof = map.roofGrid.RoofAt(c);
            return roof != null && roof.defName != "Avir_CanopyRoof";      // the shelter canopy is leaves
        }

        public static void WalkableBy_RoofRule(IntVec3 c, Map map, Pawn pawn, ref bool __result)
        {
            // Only ever turns true back into false, and only for a cell the walking grid would
            // have refused anyway. A cell an ordinary pawn could stand in stays walkable no matter
            // what is overhead - otherwise a flying Avir could not enter its own bedroom.
            if (!__result) return;

            try
            {
                if (!IsAvirFlying(pawn) || map == null) return;
                if (!Blocked(map, c)) return;
                if (map.pathing?.Normal?.pathGrid?.Walkable(c) ?? true) return;

                __result = false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight roof rule (WalkableBy) failed: " + e, 0x4A51F8);
            }
        }

        /// <summary>
        /// VEF's NoBuildingBlocking clears whatever is in the next cell for a phaser. Under a roof,
        /// an Avir should be stopped by it again - so the building is handed back.
        /// </summary>
        public static void BuildingBlocking_RoofRule(Pawn_PathFollower __instance, ref Building __result)
        {
            if (__result != null) return;

            try
            {
                Pawn pawn = patherPawn != null ? patherPawn(__instance) : null;
                if (pawn == null || !IsAvirFlying(pawn) || pawn.Map == null) return;

                IntVec3 next = nextCell != null ? nextCell(__instance) : IntVec3.Invalid;
                if (!next.IsValid || !Blocked(pawn.Map, next)) return;

                Building edifice = next.GetEdifice(pawn.Map);
                if (edifice != null) __result = edifice;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight roof rule (BuildingBlocking) failed: " + e, 0x4A51F9);
            }
        }

        /// <summary>
        /// Flat move cost for an airborne Avir, whatever it is crossing.
        ///
        /// Clamped to the pawn's own walking speed rather than zeroed: flight already ignores
        /// terrain through the Floating comp, and making it FASTER than walking would turn it into
        /// a movement upgrade rather than a way past obstacles.
        ///
        /// Diagonals cost their usual bit more, because the vanilla formula does that and a flyer
        /// that crossed corners at cardinal speed would drift out of step with everything else.
        /// </summary>
        public static void CostToMoveIntoCell_Postfix(Pawn pawn, IntVec3 c, ref float __result)
        {
            try
            {
                if (pawn == null || !IsAvirFlying(pawn)) return;

                // EVERYTHING HERE IS A FLOAT, and both halves of that had to be read out of the IL
                // rather than assumed. Pawn.TicksPerMove clamps and returns a float, and the static
                // CostToMoveIntoCell ends with Mathf.Max(x, 1f) - so it returns a float too, and a
                // postfix declaring ref int would not bind. No rounding anywhere: the vanilla
                // pipeline carries fractional ticks and rounding here would drift against it.
                bool diagonal = c.x != pawn.Position.x && c.z != pawn.Position.z;
                float baseCost = diagonal ? pawn.TicksPerMoveDiagonal : pawn.TicksPerMoveCardinal;

                // ONE-SHOT EVIDENCE ABOUT WATER, because "I cannot tell whether they are flying"
                // cannot be answered by staring at the map.
                //
                // If the printed raw cost is far above baseCost, the river's penalty was arriving
                // through this method and the clamp below removes it: an airborne Avir crosses
                // water at its own walking speed, and any remaining slowness is somewhere else.
                // If raw and base are already equal, this method is NOT where the swim penalty
                // lives in 1.6, and the next place to look is the move-speed stat rather than the
                // path cost. Either way the log settles it in one crossing instead of a session.
                //
                // The bool is tested FIRST so that after the first water cell this whole block is a
                // single field read - this is a per-cell, per-pawn method.
                if (!loggedWaterCost && pawn.Map != null && AvirFlightTerrain.IsWaterAt(pawn.Map, c))
                {
                    loggedWaterCost = true;
                    Log.Message("[Avir flight] airborne over water at " + c
                        + ": raw move cost " + __result.ToString("0.0")
                        + ", clamped to " + baseCost.ToString("0.0")
                        + (__result > baseCost
                            ? " - the water penalty came through CostToMoveIntoCell and is now gone."
                            : " - NO penalty in this method; the slowness is elsewhere."));
                }

                if (__result > baseCost) __result = baseCost;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight move cost postfix failed: " + e, 0x4A51FA);
            }
        }

        private static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> patherPawn =
            AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");

        private static readonly AccessTools.FieldRef<Pawn_PathFollower, IntVec3> nextCell =
            AccessTools.FieldRefAccess<Pawn_PathFollower, IntVec3>("nextCell");
    }
}
