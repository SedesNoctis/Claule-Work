using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// TEACHES THE PATHFINDER ABOUT FLIGHT. One fix, three symptoms, because all three had the
    /// same cause.
    ///
    /// WHAT WAS ACTUALLY WRONG. VEF's HediffComp_Floating patches Pawn_PathFollower
    /// .CostToMoveIntoCell - the MOVER. Nothing ever told the PATHFINDER anything. So a flying
    /// pawn still routed around the river (the route was planned as if walking), and a pawn
    /// standing in deep water could not be given a path OUT of that cell, because PathGrid called
    /// it impassable. That last one is why they stayed stuck EVEN WHILE AIRBORNE - the flight was
    /// never the broken part, the route was.
    ///
    /// THE FIND THAT REPLACED THREE DAYS OF WORK WITH THIRTY LINES. RimWorld 1.6 already has a
    /// flying pathfinder. Verse.AI.Pathing holds three contexts - normal, fenceBlocked, flying -
    /// and Pawn.GetPathContext(Pathing) hands back pathing.Flying for anything the engine already
    /// considers airborne. The flying grid's rules, read out of PathGrid.CalculatedCostAt:
    ///
    ///     if (terrain.passability == Impassable
    ///         && !(def.flying && terrain.forcePassableByFlyingPawns)) return 10000;
    ///     if (def.flying) { skip terrain pathCost entirely }
    ///
    /// So on the flying grid terrain is free, and impassable terrain opens where the def carries
    /// BuildableDef.forcePassableByFlyingPawns - which is exactly what that flag exists for and
    /// what deep water carries. Reachability, regions and cost all stay internally consistent
    /// because it is the engine's own grid rather than something bolted to the side of it.
    ///
    /// WHY GetPathContext AND NOT Pawn.Flying. Pawn.Flying reads the Pawn_FlightTracker, and it
    /// is consulted by far more than pathing - drawing altitude, targeting, cover. Forcing it
    /// true for an Avir mid-hop would make the pawn airborne to systems that have no idea what to
    /// do with that. Patching the path-context choice alone gets the routing and touches nothing
    /// else.
    ///
    /// WHY NOT IPathGridCustomizer, THE OTHER 1.6 HOOK. Tried first. Its offset grid gets its own
    /// separate >= 10000 test in PathGridJob.CellIsPassable, so a customizer can forbid a cell and
    /// can never permit one. It only tightens.
    ///
    /// THE ROOF RULE IS A PROPERTY OF THE CELL, NOT OF THE PAWN, which is why it can live in the
    /// shared flying grid rather than in the per-path job. That also keeps every line of this file
    /// on the main thread: CalculatedCostAt runs during grid construction and dirtying, never on
    /// the batched worker threads that evaluate paths. No locking, no thread-safety question.
    /// </summary>
    public static class Patch_AvirFlightPathing
    {
        /// <summary>What an unroofed wall costs to fly over. Well above open ground, so a flying
        /// Avir still prefers a real floor and does not cut through the wall of a compound to save
        /// two cells - but far below the 10000 that means "never".</summary>
        private const int FlightCellCost = 40;

        /// <summary>The engine's own "this is a wall" threshold, from PathGrid and PathGridJob
        /// alike. Named rather than inlined: if a future version moves it, this is the line.</summary>
        public const int ImpassableThreshold = 10000;

        private static bool warnedNoFlyingGrid;
        private static bool announcedActive;
        private static bool announcedRoofRule;
        private static bool announcedWalkable;

        private static AccessTools.FieldRef<PathGrid, Map> pathGridMap;
        private static AccessTools.FieldRef<PathGrid, PathGridDef> pathGridDef;

        public static void Apply(Harmony harmony)
        {
            try
            {
                pathGridMap = AccessTools.FieldRefAccess<PathGrid, Map>("map");
                pathGridDef = AccessTools.FieldRefAccess<PathGrid, PathGridDef>("def");

                MethodInfo getContext = AccessTools.Method(typeof(Pawn), "GetPathContext");
                MethodInfo costAt = AccessTools.Method(typeof(PathGrid), "CalculatedCostAt");

                if (getContext == null || costAt == null)
                {
                    Log.Warning("[Avir] Flight pathing: GetPathContext or CalculatedCostAt not found. "
                        + "Flight still works, it just will not affect routing.");
                    return;
                }

                harmony.Patch(getContext,
                    postfix: new HarmonyMethod(typeof(Patch_AvirFlightPathing), nameof(GetPathContext_Postfix)));

                // THE ONE THAT ACTUALLY MATTERS, AND THE REASON THE FEATURE DID NOTHING.
                //
                // Pathing has two For overloads. For(Pawn) consults Pawn.GetPathContext, which is
                // what the first patch hooks - but the pathfinder does not use it. Read out of
                // PathFinder.ParameterizePathJob:
                //
                //     map.pathing.For(request.TraverseParms)
                //
                // the TraverseParms overload, which only ever looks at fenceBlocked and returns
                // fenceBlocked or normal. It can never return the flying context. So every path was
                // built on the walking grid no matter what GetPathContext said, routes went round
                // the river as before, and the auto-takeoff patch never saw a cell that needed
                // wings because no such cell was ever on the route.
                //
                // TraverseParms carries the pawn, so the same decision can be made here.
                // THE GATE ON ACTUALLY ENTERING A CELL, WHICH IS SEPARATE FROM ROUTING.
                //
                // A route through the rock is not enough. SetupMoveIntoNextCell then asks
                // GenGrid.WalkableBy(nextCell, map, pawn) and, if that says no, abandons the step
                // and requests a new path - which produces a pawn standing still in front of the
                // exact cell its own route told it to enter.
                //
                // WalkableBy resolves to map.pathing.For(pawn).pathGrid.Walkable(c), so in
                // principle the routing patch already covers it. In practice that leaves the
                // movement gate depending on the flying grid having been recalculated with our
                // cost rule applied, which is one more thing to be true than it needs to be.
                // Answering here directly makes entering a cell depend on the same rule that
                // opened it, and nothing else.
                MethodInfo walkableBy = AccessTools.Method(typeof(GenGrid), "WalkableBy");
                if (walkableBy != null)
                {
                    harmony.Patch(walkableBy,
                        postfix: new HarmonyMethod(typeof(Patch_AvirFlightPathing), nameof(WalkableBy_Postfix)));
                }

                // PATCH For(Pawn) ITSELF, NOT JUST Pawn.GetPathContext WHICH IT CALLS.
                //
                // GenGrid.WalkableBy is three lines - map.pathing.For(pawn).pathGrid.Walkable(c) -
                // and the JIT inlines it into Pawn_PathFollower. Once inlined, a Harmony patch on
                // WalkableBy never runs, which is exactly what the log showed: the "WalkableBy hook
                // is live" line never appeared even though the patch reported binding.
                //
                // But the inlined body still executes its own calls, so patching the method it
                // calls does take effect. For(Pawn) is the last hook on that path that cannot be
                // skipped.
                MethodInfo forPawn = AccessTools.Method(typeof(Pathing), "For", new[] { typeof(Pawn) });
                if (forPawn != null)
                {
                    harmony.Patch(forPawn,
                        postfix: new HarmonyMethod(typeof(Patch_AvirFlightPathing), nameof(ForPawn_Postfix)));
                }

                MethodInfo forParms = AccessTools.Method(typeof(Pathing), "For", new[] { typeof(TraverseParms) });
                if (forParms != null)
                {
                    harmony.Patch(forParms,
                        postfix: new HarmonyMethod(typeof(Patch_AvirFlightPathing), nameof(ForParms_Postfix)));
                }
                else
                {
                    Log.Warning("[Avir] Pathing.For(TraverseParms) not found - flight will not affect "
                        + "routing. This is the hook that matters.");
                }
                harmony.Patch(costAt,
                    postfix: new HarmonyMethod(typeof(Patch_AvirFlightPathing), nameof(CalculatedCostAt_Postfix)));

                Log.Message("[Avir] Flight patches bound: GetPathContext=" + (getContext != null)
                    + " CalculatedCostAt=" + (costAt != null)
                    + " WalkableBy=" + (walkableBy != null)
                    + " ForPawn=" + (forPawn != null)
                    + " ForTraverseParms=" + (forParms != null));
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Flight pathing patches failed to apply: " + e);
            }
        }

        /// <summary>
        /// The hook GenGrid.WalkableBy goes through, and therefore the one that decides whether a
        /// pawn may actually step into a cell. See Apply for why patching Pawn.GetPathContext was
        /// not enough.
        /// </summary>
        public static void ForPawn_Postfix(Pathing __instance, Pawn __0, ref PathingContext __result)
        {
            try
            {
                if (__instance == null) return;

                if (!announcedForPawn)
                {
                    announcedForPawn = true;
                    Log.Message("[Avir] Pathing.For(Pawn) hook is live. First caller: "
                        + (__0?.LabelShort ?? "null"));
                }

                if (!WantsFlyingGrid(__0)) return;

                PathingContext flying = __instance.Flying;
                if (flying == null) { WarnNoFlyingGrid(); return; }

                AnnounceActive(__0);
                __result = flying;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight For(Pawn) postfix failed: " + e, 0x4A51F6);
            }
        }

        /// <summary>
        /// The hook the pathfinder actually goes through. See Apply for why this exists and why the
        /// GetPathContext patch alone was not enough.
        /// </summary>
        public static void ForParms_Postfix(Pathing __instance, TraverseParms __0, ref PathingContext __result)
        {
            try
            {
                if (__instance == null) return;

                if (!announcedForParms)
                {
                    announcedForParms = true;
                    Log.Message("[Avir] Pathing.For(TraverseParms) hook is live. First caller: "
                        + (__0.pawn?.LabelShort ?? "no pawn"));
                }

                if (!WantsFlyingGrid(__0.pawn)) return;

                PathingContext flying = __instance.Flying;
                if (flying == null)
                {
                    WarnNoFlyingGrid();
                    return;
                }

                AnnounceActive(__0.pawn);
                __result = flying;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight For(TraverseParms) postfix failed: " + e, 0x4A51F4);
            }
        }

        /// <summary>
        /// Lets a flight-capable Avir step into a cell the walking grid refuses, under exactly the
        /// same roof rule that opened it for routing.
        ///
        /// Only ever turns false into true. A cell the engine already allows is untouched, so no
        /// other pawn's movement changes in any way.
        /// </summary>
        public static void WalkableBy_Postfix(IntVec3 __0, Map __1, Pawn __2, ref bool __result)
        {
            if (__result) return;

            try
            {
                if (!announcedWalkableHit)
                {
                    announcedWalkableHit = true;
                    Log.Message("[Avir] GenGrid.WalkableBy hook is live. First refused cell " + __0
                        + " for " + (__2?.LabelShort ?? "no pawn"));
                }

                if (__1 == null || !WantsFlyingGrid(__2)) return;
                if (!FlightPassable(__1, __0)) return;

                if (!announcedWalkable)
                {
                    announcedWalkable = true;
                    Log.Message("[Avir] Flight movement gate active: " + __2.LabelShort
                        + " may now enter " + __0 + ".");
                }

                __result = true;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight WalkableBy postfix failed: " + e, 0x4A51F5);
            }
        }

        /// <summary>
        /// The roof rule as a plain question about a cell, so routing and movement cannot disagree.
        /// Nothing overhead means flight passes; thin rock roof is not a ceiling, constructed roof
        /// and overhead mountain are.
        /// </summary>
        public static bool FlightPassable(Map map, IntVec3 cell)
        {
            if (map?.roofGrid == null || !cell.InBounds(map)) return false;

            RoofDef roof = map.roofGrid.RoofAt(cell);
            // the shelter canopy is leaves: an Avir flies through it
            return roof == null || (roof.isNatural && !roof.isThickRoof) || roof.defName == "Avir_CanopyRoof";
        }

        /// <summary>Single decision, shared by every hook, so they can never disagree about who
        /// flies.</summary>
        private static readonly HashSet<int> explained = new HashSet<int>();
        private static bool announcedForParms;
        private static bool announcedWalkableHit;
        private static bool announcedForPawn;

        private static bool WantsFlyingGrid(Pawn pawn)
        {
            if (pawn == null) return false;

            // pawn.Flying is the engine's own airborne tracker. If it is already true the engine
            // has handed out the flying grid itself and we must not interfere.
            if (pawn.Flying) return false;

            // CROWD GATE. Pathing.For runs for every pawn that looks for a path or checks
            // reachability. A pawn that is neither registered airborne nor a flight-gene carrier
            // cannot want the flying grid, so it leaves on two hash lookups - no matter how many
            // Avir are on the map. Also keeps non-carriers out of the "explained" set below.
            if (!AvirFlightCarriers.MayCarry(pawn) && !AvirFlightRegistry.Contains(pawn)) return false;

            bool flying = AvirFlightUtility.IsFlying(pawn);
            bool canTakeOff = AvirFlightUtility.CanTakeOff(pawn);

            // SAYS WHICH CONDITION FAILED, once per pawn.
            //
            // Three rounds went to inferring this from silence. Silence is a bad diagnostic: it
            // looks identical whether the hook never ran, the pawn was rejected, or the answer was
            // right and something downstream ignored it.
            if (!flying && !canTakeOff && explained.Add(pawn.thingIDNumber))
            {
                string reason;
                if (pawn.genes == null) reason = "no gene tracker";
                else if (!AvirFlightUtility.CarriesFlightGene(pawn)) reason = "no active Flight_Gene";
                else if (pawn.Dead || pawn.Downed) reason = "dead or downed";
                else
                {
                    Need_Rest rest = pawn.needs?.rest;
                    reason = rest != null && rest.CurLevel <= AvirFlightUtility.AutoTakeoffRestThreshold
                        ? "rest " + rest.CurLevel.ToString("0.00") + " at or below the "
                          + AvirFlightUtility.AutoTakeoffRestThreshold.ToString("0.00") + " floor"
                        : "unknown - every listed check passed, which should be impossible";
                }

                Log.Message("[Avir] " + pawn.LabelShort + " refused the flying grid: " + reason + ".");
            }

            return flying || canTakeOff;
        }

        private static void WarnNoFlyingGrid()
        {
            if (warnedNoFlyingGrid) return;
            warnedNoFlyingGrid = true;
            Log.Warning("[Avir] This game has no flying path grid - Pathing.Flying is null. "
                + "Flight cannot affect routing here.");
        }

        private static void AnnounceActive(Pawn pawn)
        {
            if (announcedActive) return;
            announcedActive = true;
            Log.Message("[Avir] Flight routing active: " + pawn.LabelShort
                + " is now pathing on the flying grid.");
        }

        /// <summary>
        /// Puts an airborne Avir on the engine's flying grid. Kept because other systems - notably
        /// Reachability - do go through For(Pawn), so both doors need opening.
        /// </summary>
        public static void GetPathContext_Postfix(Pawn __instance, Pathing __0, ref PathingContext __result)
        {
            try
            {
                if (__instance == null || __0 == null) return;
                if (!WantsFlyingGrid(__instance)) return;

                PathingContext flying = __0.Flying;
                if (flying == null) { WarnNoFlyingGrid(); return; }

                AnnounceActive(__instance);
                __result = flying;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight path context postfix failed: " + e, 0x4A51F1);
            }
        }

        /// <summary>
        /// The roof rule, applied only to the flying grid.
        ///
        /// Opens a cell the engine had priced as a wall, but ONLY where nothing is overhead. Thin
        /// rock roof does not count as overhead: RoofDef.isNatural separates rock from constructed
        /// roofs and isThickRoof separates overhead mountain from the thin skin over a small rocky
        /// outcrop. A hill you fly over; a mountain and a built ceiling you do not.
        ///
        /// Only ever lowers a cost that was already at or above the impassable threshold. A cell
        /// the engine was happy with is left completely alone, so nothing about ordinary flying
        /// pawns - vanilla's or anyone else's - changes.
        /// </summary>
        public static void CalculatedCostAt_Postfix(PathGrid __instance, IntVec3 __0, ref int __result)
        {
            try
            {
                if (__result < ImpassableThreshold) return;
                if (__instance == null || pathGridDef == null || pathGridMap == null) return;

                PathGridDef def = pathGridDef(__instance);
                if (def == null || !def.flying) return;

                Map map = pathGridMap(__instance);
                if (map?.roofGrid == null || !__0.InBounds(map)) return;

                RoofDef roof = map.roofGrid.RoofAt(__0);
                if (roof != null && (!roof.isNatural || roof.isThickRoof) && roof.defName != "Avir_CanopyRoof") return;

                if (!announcedRoofRule)
                {
                    announcedRoofRule = true;
                    Log.Message("[Avir] Flight roof rule active: opening unroofed cells on the "
                        + "flying grid, first at " + __0 + ".");
                }

                __result = FlightCellCost;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight roof rule failed: " + e, 0x4A51F2);
            }
        }

        /// <summary>
        /// Would this cell hold the pawn if they were NOT airborne? Asked by the landing guard, on
        /// the main thread, against the NORMAL grid - which our roof rule deliberately never
        /// touches, so the answer is the plain walking answer.
        /// </summary>
        public static bool CellNeedsFlight(Map map, IntVec3 cell)
        {
            try
            {
                if (map?.pathing == null || !cell.InBounds(map)) return false;
                PathGrid grid = map.pathing.Normal?.pathGrid;
                if (grid == null) return false;
                return !grid.Walkable(cell);
            }
            catch
            {
                return false;
            }
        }

    }
}
