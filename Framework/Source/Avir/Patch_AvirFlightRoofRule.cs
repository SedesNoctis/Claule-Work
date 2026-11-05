using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// PUTS THE ROOF RULE BACK ON TOP OF VEF's PHASING.
    ///
    /// Flight now rides on VEF.Hediffs.HediffComp_Phasing, which works because VEF patches the
    /// whole set of gates rather than the three I had found by hand:
    ///
    ///     PathFinder.CreateRequest          Pawn_PathFollower.CostToMoveIntoCell
    ///     GenGrid.WalkableBy                Pawn_PathFollower.BuildingBlockingNextPathCell
    ///     Pawn_PathFollower.TryEnterNextPathCell   Reachability.CanReach
    ///     Pawn_PathFollower.StartPath       Pawn.SpawnSetup / DeSpawn
    ///
    /// Reachability.CanReach and BuildingBlockingNextPathCell were the two I never touched, and
    /// they are exactly why a route could look correct while the pawn refused to walk it.
    ///
    /// What VEF's phasing does NOT have is any notion of a cell: PhasingUtils.IsPhasing takes a
    /// pawn and nothing else, so a phaser passes everything everywhere. For the amulets that is
    /// correct and intended. For flight it is not - an Avir should cross what is open to the sky
    /// and be stopped by a ceiling.
    ///
    /// So this adds one rule on top, keyed to the flight hediff alone. Amulet phasing is untouched:
    /// Architect's Reach and Living Shadow keep passing through roofed walls exactly as before.
    ///
    /// TWO GATES, BECAUSE VEF OPENS TWO. WalkableBy decides whether a cell may be entered at all;
    /// BuildingBlockingNextPathCell decides whether a solid thing in the next cell stops the step.
    /// Closing only one leaves the pawn either walking through roofed walls anyway or stopping
    /// dead against open-air ones.
    /// </summary>
    public static class Patch_AvirFlightRoofRule
    {

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
                        postfix: new HarmonyMethod(typeof(Patch_AvirFlightRoofRule),
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
            // Runs on every cell step of every moving pawn. It used to walk the hediff list each
            // time; the registry answers the same question - "airborne by OUR hediff" - because
            // HediffComp_FlightMonitor registers on add and every tick, and unregisters on
            // removal. Same check Patch_AvirFlightSpeed already uses.
            if (AvirFlightRegistry.NobodyFlying) return false;
            return pawn != null && AvirFlightRegistry.Contains(pawn);
        }

        /// <summary>
        /// Roofed means solid to a flyer. Thin rock roof does not count - a low outcrop is
        /// something you fly over - while constructed roofs and overhead mountain do.
        /// </summary>
        public static bool Blocked(Map map, IntVec3 c)
        {
            if (map?.roofGrid == null || !c.InBounds(map)) return false;

            RoofDef roof = map.roofGrid.RoofAt(c);
            return roof != null && (!roof.isNatural || roof.isThickRoof) && roof.defName != "Avir_CanopyRoof";
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
