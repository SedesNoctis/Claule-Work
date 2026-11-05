using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// DIRECT FLIGHT. A flying Avir does not ask the game for a route; it flies at the target in a
    /// straight line and we hand the engine the resulting path as a finished thing.
    ///
    /// WHY NOTHING ELSE WORKED, IN ONE PARAGRAPH.
    ///
    /// The engine stores a per-cell cost. Ordinary ground is 2; anything impassable is 10000, and
    /// 10000 does not mean "expensive" to the pathfinder, it means "no". So no route was ever built
    /// through rock, and the pawn walked around. VEF makes such a cell ENTERABLE, which is why
    /// flight through walls works at all - but entering and routing are two different systems, and
    /// only the first was ever solved. The one place a mod may adjust costs takes UNSIGNED offsets,
    /// so it can raise a cost and never lower one. There is no lever there. Confirmed in the probe
    /// output: walkCost and flyCost were both 10000 on every obstacle, roofed or not.
    ///
    /// So this stops negotiating with the pathfinder and replaces it for one case. A straight line
    /// needs no search.
    ///
    /// EVERY MEMBER NAMED HERE WAS READ OUT OF Assembly-CSharp.dll, NOT RECALLED. The layout of
    /// PawnPath in particular is not guessable and getting it backwards would fly the pawn from the
    /// destination to itself:
    ///
    ///     Initialize(points, cost)  fills nodes in order, then sets curNodeIndex = nodes.Count - 1
    ///     Peek(n)                => nodes[curNodeIndex - n]
    ///     ConsumeNextNode()      => Peek(1), then curNodeIndex--
    ///     NodesLeftCount         => curNodeIndex + 1
    ///     FirstNode              => nodes[Count - 1]      i.e. THE START
    ///     LastNode               => nodes[0]              i.e. THE DESTINATION
    ///     Found                  => !(totalCostInt < 0)
    ///
    /// So the list runs DESTINATION FIRST and START LAST, and is consumed backwards. We build it in
    /// that order.
    ///
    /// THE HOOK. Pawn_PathFollower.SetNewPathRequest creates the asynchronous request that
    /// eventually becomes curPath, and it is the single funnel: StartPath calls it, PatherTick
    /// calls it twice, and TrySetNewPathRequest is only a null-check wrapper around it. We prefix
    /// it: if a direct
    /// line is legal we build curPath ourselves and skip the original entirely, so no request is
    /// ever made and there is nothing for the pathfinder to disagree with. If the line is not legal
    /// we do nothing and the engine routes normally - which is what carries the pawn AROUND a
    /// sealed room rather than into it.
    /// </summary>
    public static class Patch_AvirDirectFlight
    {
        /// <summary>Longest straight flight we will build by hand. Beyond this the pawn is crossing
        /// most of the map and the engine's own route - which knows about doors, danger and other
        /// pawns - is the better answer even for a flier.</summary>
        private const int MaxDirectCells = 200;

        private static AccessTools.FieldRef<Pawn_PathFollower, Pawn> patherPawn;
        private static AccessTools.FieldRef<Pawn_PathFollower, LocalTargetInfo> patherDestination;
        private static AccessTools.FieldRef<Pawn_PathFollower, PawnPath> patherCurPath;

        /// <summary>
        /// The pather's PathEndMode. Bound SEPARATELY from the three above and allowed to fail on
        /// its own, because everything else here works without it: if this field ever moves, the
        /// patch degrades to its previous behaviour - direct lines for Goto orders, engine routing
        /// for everything else - instead of switching off entirely.
        /// </summary>
        private static AccessTools.FieldRef<Pawn_PathFollower, PathEndMode> patherPeMode;
        private static bool peModeReady;
        private static bool loggedNoPeMode;

        private static FieldInfo nodesField;
        private static FieldInfo curNodeIndexField;
        private static FieldInfo totalCostField;
        private static FieldInfo inUseField;
        private static FieldInfo mapPoolField;
        private static MethodInfo poolGetPath;
        private static MethodInfo disposeCurPath;

        private static bool ready;
        private static bool loggedFirstDirect;
        private static bool loggedRefusal;

        public static void Apply(Harmony harmony)
        {
            try
            {
                patherPawn = AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");
                patherDestination = AccessTools.FieldRefAccess<Pawn_PathFollower, LocalTargetInfo>("destination");
                patherCurPath = AccessTools.FieldRefAccess<Pawn_PathFollower, PawnPath>("curPath");

                // Bound in its own try: a miss here must not take the rest of the patch down.
                try
                {
                    patherPeMode = AccessTools.FieldRefAccess<Pawn_PathFollower, PathEndMode>("peMode");
                    peModeReady = patherPeMode != null;
                }
                catch
                {
                    peModeReady = false;
                }

                if (!peModeReady)
                {
                    Log.Warning("[Avir direct] Pawn_PathFollower.peMode not found. Touch-mode jobs "
                        + "will be routed by the engine as before; direct flight still applies to "
                        + "plain destination orders.");
                }

                nodesField = AccessTools.Field(typeof(PawnPath), "nodes");
                curNodeIndexField = AccessTools.Field(typeof(PawnPath), "curNodeIndex");
                totalCostField = AccessTools.Field(typeof(PawnPath), "totalCostInt");
                inUseField = AccessTools.Field(typeof(PawnPath), "inUse");

                mapPoolField = AccessTools.Field(typeof(Map), "pawnPathPool");
                poolGetPath = mapPoolField == null
                    ? null
                    : AccessTools.Method(mapPoolField.FieldType, "GetPath");

                disposeCurPath = AccessTools.Method(typeof(Pawn_PathFollower), "DisposeAndClearCurPath");

                ready = nodesField != null && curNodeIndexField != null && totalCostField != null
                    && inUseField != null && mapPoolField != null && poolGetPath != null;

                if (!ready)
                {
                    // Named individually rather than as one failure, because if this ever breaks in
                    // a future game version the whole point is to know WHICH member moved.
                    Log.Warning("[Avir direct] not applied - missing: "
                        + (nodesField == null ? "PawnPath.nodes " : "")
                        + (curNodeIndexField == null ? "PawnPath.curNodeIndex " : "")
                        + (totalCostField == null ? "PawnPath.totalCostInt " : "")
                        + (inUseField == null ? "PawnPath.inUse " : "")
                        + (mapPoolField == null ? "Map.pawnPathPool " : "")
                        + (poolGetPath == null ? "PawnPathPool.GetPath " : ""));
                    return;
                }

                // SetNewPathRequest, NOT TrySetNewPathRequest.
                //
                // The first attempt hooked TrySetNewPathRequest and did nothing at all, because
                // that method is only ONE of three callers. Reading the IL of the other two settles
                // it: both StartPath and PatherTick call SetNewPathRequest DIRECTLY, and PatherTick
                // does so from two separate places. Hooking the wrapper caught roughly a quarter of
                // the traffic.
                //
                // SetNewPathRequest is the single funnel - every route request in the game passes
                // through it - so this is the one place where a request can be replaced.
                MethodInfo target = AccessTools.Method(typeof(Pawn_PathFollower), "SetNewPathRequest");
                if (target == null)
                {
                    Log.Warning("[Avir direct] Pawn_PathFollower.SetNewPathRequest not found.");
                    ready = false;
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(Patch_AvirDirectFlight), nameof(SetNewPathRequest_Prefix))
                { priority = Priority.First });

                Log.Message("[Avir direct] Direct flight bound to SetNewPathRequest.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir direct] failed to apply: " + e);
                ready = false;
            }
        }

        /// <summary>
        /// The pather's PathEndMode, or OnCell when the field could not be bound. OnCell is the
        /// safe default: it reproduces exactly what this patch did before peMode was consulted.
        /// </summary>
        private static PathEndMode ReadPeMode(Pawn_PathFollower pather)
        {
            if (!peModeReady) return PathEndMode.OnCell;

            try
            {
                return patherPeMode(pather);
            }
            catch
            {
                if (!loggedNoPeMode)
                {
                    loggedNoPeMode = true;
                    Log.Warning("[Avir direct] reading peMode threw; treating this trip as OnCell.");
                }
                return PathEndMode.OnCell;
            }
        }

        /// <summary>
        /// Where the pawn should actually COME TO REST for this trip, which is not always the cell
        /// it was pointed at.
        ///
        /// OnCell means the target cell itself, and the answer is simply whether it can be stood in.
        ///
        /// Every other mode - Touch, ClosestTouch, InteractionCell, OnAdjacentCell - means the pawn
        /// works on the target from beside it. For those, the target cell is frequently solid by
        /// design: a wall being mined is the clearest case, and a pawn told to stand IN it would be
        /// standing in stone by the mod's own doing.
        ///
        /// So the neighbours are searched and the nearest legal one wins. Nearest to the PAWN, not
        /// to the target: the pawn is flying in along a straight line, and the near side is both the
        /// shorter trip and the side it would naturally arrive on.
        ///
        /// Returns IntVec3.Invalid when nothing adjacent will serve, which sends the trip back to
        /// the engine - the same fallback used everywhere else in this file.
        /// </summary>
        private static IntVec3 ResolveStopCell(Map map, IntVec3 from, IntVec3 to, PathEndMode mode)
        {
            if (mode == PathEndMode.OnCell)
            {
                return AvirFlightRules.CannotStandAt(map, to) ? IntVec3.Invalid : to;
            }

            // Standing on the target's own cell is legitimate for Touch when that cell is open -
            // a downed pawn lying on grass, an item on the floor - and it is what the engine would
            // often choose too. Checked first so the common case costs one test.
            if (!AvirFlightRules.CannotStandAt(map, to)) return to;

            IntVec3 best = IntVec3.Invalid;
            int bestDist = int.MaxValue;

            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 c = to + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map)) continue;
                if (AvirFlightRules.CannotStandAt(map, c)) continue;

                int d = (c - from).LengthHorizontalSquared;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>Returns false to skip the engine's path request entirely.</summary>
        public static bool SetNewPathRequest_Prefix(Pawn_PathFollower __instance)
        {
            if (!ready) return true;

            try
            {
                Pawn pawn = patherPawn(__instance);
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return true;
                if (!AvirFlightUtility.IsFlying(pawn)) return true;

                LocalTargetInfo dest = patherDestination(__instance);
                if (!dest.IsValid) return true;

                Map map = pawn.Map;
                IntVec3 from = pawn.Position;
                IntVec3 to = dest.Cell;
                if (!to.IsValid || !to.InBounds(map) || to == from) return true;

                // A journey may not END somewhere illegal, however it got there. Falling through to
                // the engine here also means an order into a sealed vault behaves exactly as any
                // other unreachable order.
                //
                // THE TARGET CELL IS NOT ALWAYS THE STOPPING CELL. This read dest.Cell directly and
                // ignored peMode entirely, which was wrong in both directions. A Touch job - feeding
                // a downed pawn, mining a wall, operating a bench - names the TARGET, while the pawn
                // is meant to stand NEXT to it. Treating the target as the stopping cell meant every
                // such job failed the CannotStandAt test above and fell through to the engine, which
                // then picked the adjacent cell itself with no rule about stone: that is how an Avir
                // ended up feeding a guest from inside a wall, being pulled out by the rescue, and
                // walking straight back in.
                IntVec3 stopAt = ResolveStopCell(map, from, to, ReadPeMode(__instance));
                if (!stopAt.IsValid) return true;
                if (stopAt == from) return true;
                to = stopAt;

                List<IntVec3> line = BuildLine(map, from, to);
                if (line == null)
                {
                    if (!loggedRefusal)
                    {
                        loggedRefusal = true;
                        Log.Message("[Avir direct] line " + from + " -> " + to
                            + " refused (sealed cell, out of bounds, or too far); "
                            + "handing this trip to the engine.");
                    }
                    return true;
                }

                PawnPath path = MakePath(map, line);
                if (path == null) return true;

                // Release whatever the pather was holding, exactly the way the engine does it.
                disposeCurPath?.Invoke(__instance, null);
                patherCurPath(__instance) = path;

                if (!loggedFirstDirect)
                {
                    loggedFirstDirect = true;
                    Log.Message("[Avir direct] straight path built for " + pawn.LabelShort
                        + ": " + from + " -> " + to + " in " + line.Count + " cells.");
                }

                return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir direct] straight-line path failed: " + e, 0x4A5250);
                return true;
            }
        }

        /// <summary>
        /// Cells from `from` to `to` as an 8-connected chain, or null if the line may not be flown.
        ///
        /// EVERY STEP MOVES ONE CELL, DIAGONALS INCLUDED. The pather walks from node to node and
        /// assumes each is adjacent to the last; a line that jumped two cells would teleport the
        /// pawn or wedge it. Bresenham with the major axis stepped every iteration guarantees
        /// adjacency.
        ///
        /// TWO THINGS VETO A LINE. Rock, mountain, chunks, mech debris and walls standing in the
        /// open are all flown straight through, which is the entire point. What is refused is an
        /// interior nobody has opened, and the SHELL of any roofed room - a building is entered by
        /// its door. Refusing means handing the trip back to the engine so it can route AROUND
        /// rather than failing outright.
        /// </summary>
        private static List<IntVec3> BuildLine(Map map, IntVec3 from, IntVec3 to)
        {
            int dx = Math.Abs(to.x - from.x);
            int dz = Math.Abs(to.z - from.z);

            if (Math.Max(dx, dz) > MaxDirectCells) return null;

            int sx = to.x > from.x ? 1 : (to.x < from.x ? -1 : 0);
            int sz = to.z > from.z ? 1 : (to.z < from.z ? -1 : 0);

            List<IntVec3> cells = new List<IntVec3>();
            int x = from.x, z = from.z;

            cells.Add(from);

            // NO BRESENHAM. Interleaving straight and diagonal steps made the pawn weave from cell centre
            // to cell centre - the zigzag in flight. Like the game's own routes: half the straight run,
            // then every diagonal in one stroke, then the rest of the straight run.
            int diag = Math.Min(dx, dz), straight = Math.Max(dx, dz) - diag;
            bool alongX = dx >= dz;
            int before = straight / 2;
            var steps = new List<(int, int)>();
            for (int i = 0; i < before; i++) steps.Add(alongX ? (sx, 0) : (0, sz));
            for (int i = 0; i < diag; i++) steps.Add((sx, sz));
            for (int i = before; i < straight; i++) steps.Add(alongX ? (sx, 0) : (0, sz));

            int guard = MaxDirectCells * 2 + 4;
            foreach (var (stx, stz) in steps)
            {
                if (guard-- <= 0) break;
                x += stx; z += stz;

                IntVec3 c = new IntVec3(x, 0, z);
                if (!c.InBounds(map)) return null;

                // THE VETO. Sealed interiors, and the shell of any roofed building.
                //
                // This used to be IsSealed alone, which meant the straight line happily crossed the
                // wall of any room that had a door - and a straight line is not routed by the
                // pathfinder, so the block grid never saw it. Both halves of the flight system have
                // to hold the same rule or the pawn simply takes whichever one is looser.
                //
                // Refusing hands the trip back to the engine, which routes around - through the
                // door - rather than failing.
                if (AvirFlightRules.BlocksFlight(map, c)) return null;

                cells.Add(c);
            }

            if (guard <= 0) return null;
            if (cells[cells.Count - 1] != to) return null;

            return cells;
        }

        /// <summary>
        /// Builds a PawnPath the engine will accept.
        ///
        /// The list is written DESTINATION FIRST because that is how the engine stores and consumes
        /// it - see the class comment. Our line comes out start-first, so it is walked backwards.
        /// </summary>
        private static PawnPath MakePath(Map map, List<IntVec3> lineStartFirst)
        {
            object pool = mapPoolField.GetValue(map);
            if (pool == null) return null;

            PawnPath path = poolGetPath.Invoke(pool, null) as PawnPath;
            if (path == null) return null;

            List<IntVec3> nodes = nodesField.GetValue(path) as List<IntVec3>;
            if (nodes == null) return null;

            nodes.Clear();
            for (int i = lineStartFirst.Count - 1; i >= 0; i--) nodes.Add(lineStartFirst[i]);

            inUseField.SetValue(path, true);
            curNodeIndexField.SetValue(path, nodes.Count - 1);

            // Any non-negative number satisfies Found. The pather does not steer by this value -
            // it pays per cell through CostToMoveIntoCell, which Patch_AvirFlightSpeed already
            // clamps to the pawn's walking speed. A rough figure keeps debug overlays sensible.
            totalCostField.SetValue(path, (float)(nodes.Count * 13));

            return path;
        }
    }
}
