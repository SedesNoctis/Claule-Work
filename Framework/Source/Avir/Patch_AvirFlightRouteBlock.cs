using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// Marks the cells a flying Avir must never route through, so the PATHFINDER refuses them
    /// rather than the pawn discovering it one step at a time.
    ///
    /// WHY THIS REPLACES THE STRAIGHT-LINE CHECK, AND WHY THAT CHECK COULD NEVER HAVE WORKED.
    ///
    /// The gate decided phasing per trip by sampling the straight line to the destination. But the
    /// pathfinder is not obliged to follow that line - once phasing is on for a trip it may route
    /// anywhere at all. So a destination whose straight line missed an Ancient Danger could still
    /// produce a route that cut straight through it, which is exactly what you saw: the pawn
    /// "shortcut" through a wall and came out inside the vault. No amount of refining the line
    /// would have fixed that, because the line was never what the pawn walked.
    ///
    /// PathRequest.IPathGridCustomizer is the only per-pawn lever on the route itself. Its offsets
    /// are NativeArray&lt;ushort&gt; - UNSIGNED, so they can only ever ADD cost. I dismissed it
    /// earlier for exactly that reason, because I was trying to make rock cheaper. Blocking is the
    /// other direction, and it is precisely what unsigned offsets are good for:
    /// PathGridJob.CellIsPassable treats custom[i] >= 10000 as impassable.
    ///
    /// So flight stays permitted everywhere, and the small number of places it must not reach are
    /// closed off in the route itself. Router and mover cannot disagree, because there is only one
    /// answer and the router is the one holding it.
    /// </summary>
    public class AvirFlightBlockGrid : PathRequest.IPathGridCustomizer, IDisposable
    {
        /// <summary>The threshold PathGridJob.CellIsPassable tests against.</summary>
        private const ushort Impassable = 10000;

        private const int RebuildIntervalTicks = 600;

        private static readonly Dictionary<Map, AvirFlightBlockGrid> byMap =
            new Dictionary<Map, AvirFlightBlockGrid>();

        private readonly Map map;
        private NativeArray<ushort> grid;
        private bool allocated;
        private int builtAtTick = -99999;

        private AvirFlightBlockGrid(Map map)
        {
            this.map = map;
        }

        public static AvirFlightBlockGrid For(Map map)
        {
            if (map == null) return null;

            if (!byMap.TryGetValue(map, out AvirFlightBlockGrid g))
            {
                g = new AvirFlightBlockGrid(map);
                byMap[map] = g;
            }

            return g;
        }

        public NativeArray<ushort> GetOffsetGrid()
        {
            Rebuild();
            return grid;
        }

        /// <summary>
        /// Rebuilt on a timer rather than hooked to roof, room and fog events.
        ///
        /// A stale cell costs one slightly wrong route for a few seconds. A missed event hook costs
        /// a permanently wrong grid that nobody can see - and this feature has produced enough of
        /// those already.
        /// </summary>
        private void Rebuild()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (allocated && now - builtAtTick < RebuildIntervalTicks) return;
            builtAtTick = now;

            int count = map.cellIndices.NumGridCells;

            if (!allocated || grid.Length != count)
            {
                if (allocated) grid.Dispose();
                grid = new NativeArray<ushort>(count, Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
                allocated = true;
            }

            for (int i = 0; i < count; i++)
            {
                IntVec3 c = map.cellIndices.IndexToCell(i);
                grid[i] = Patch_AvirPhasingGate.ForbiddenCell(map, c) ? Impassable : (ushort)0;
            }
        }

        public void Dispose()
        {
            if (!allocated) return;
            grid.Dispose();
            allocated = false;
        }

        /// <summary>A Persistent NativeArray is not garbage collected. Forgetting this leaks for
        /// the life of the process and Unity says so loudly on quit.</summary>
        public static void Forget(Map map)
        {
            if (map == null) return;
            if (byMap.TryGetValue(map, out AvirFlightBlockGrid g))
            {
                g.Dispose();
                byMap.Remove(map);
            }
        }
    }

    /// <summary>Hands the blocking grid to path requests made for a flying Avir.</summary>
    public static class Patch_AvirFlightRouteBlock
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                int patched = 0;
                foreach (MethodInfo m in typeof(PathFinder).GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (m.Name != "CreateRequest") continue;

                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 0) continue;
                    if (ps[ps.Length - 1].ParameterType != typeof(PathRequest.IPathGridCustomizer)) continue;

                    harmony.Patch(m, prefix: new HarmonyMethod(
                        typeof(Patch_AvirFlightRouteBlock), nameof(CreateRequest_Prefix))
                    { priority = Priority.First });
                    patched++;
                }

                MethodInfo dispose = AccessTools.Method(typeof(Map), "Dispose");
                if (dispose != null)
                {
                    harmony.Patch(dispose, postfix: new HarmonyMethod(
                        typeof(Patch_AvirFlightRouteBlock), nameof(MapGone_Postfix)));
                }

                Log.Message("[Avir] Flight route blocking bound to " + patched
                    + " CreateRequest overload(s).");

                // WHO ELSE IS ON THIS METHOD. The whole theory rests on VEF also patching
                // CreateRequest and losing the customizer slot to us. If VEF is not listed here,
                // the theory is dead and the cause is elsewhere - which is worth knowing before
                // rewriting anything.
                //
                // Wrapped tightly: this is Harmony's own reflection surface and it is not worth a
                // startup failure if the shape differs in this version.
                try
                {
                    foreach (MethodInfo m in typeof(PathFinder).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Static | BindingFlags.Instance))
                    {
                        if (m.Name != "CreateRequest") continue;

                        HarmonyLib.Patches info = Harmony.GetPatchInfo(m);
                        if (info == null)
                        {
                            Log.Message("[Avir route] CreateRequest: no patch info.");
                            continue;
                        }

                        foreach (Patch pr in info.Prefixes)
                        {
                            Log.Message("[Avir route] prefix owner=" + pr.owner
                                + " priority=" + pr.priority + " method=" + pr.PatchMethod.Name);
                        }
                    }
                }
                catch (Exception probe)
                {
                    Log.Message("[Avir route] could not enumerate patches: " + probe.Message);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Flight route blocking failed to apply: " + e);
            }
        }

        public static void CreateRequest_Prefix(Pawn pawn, ref PathRequest.IPathGridCustomizer customizer)
        {
            try
            {
                if (pawn?.Map == null) return;
                if (!Patch_AvirPhasingGate.IsFlying(pawn)) return;

                // DIAGNOSTIC, TEMPORARY. Answers one question and nothing else: when a flying Avir
                // asks for a route, is the customizer slot already filled by somebody - VEF, most
                // likely - or is it empty when we arrive?
                //
                // The suspicion is that our Priority.First patch reaches CreateRequest BEFORE VEF
                // and claims the single customizer slot with a grid that can only ADD cost. VEF's
                // grid is what makes walls cheap enough to route through at all; if we take the
                // slot first and VEF then declines to overwrite, flight stays possible but every
                // obstacle still costs 10000 to the pathfinder, so it routes around. That is
                // exactly the behaviour observed.
                //
                // If this prints "empty" and routing through walls still works elsewhere, the
                // suspicion is wrong and the cause is somewhere else entirely.
                if (!loggedCustomizerState)
                {
                    loggedCustomizerState = true;
                    Log.Message("[Avir route] CreateRequest for " + pawn.LabelShort
                        + " - customizer on entry: "
                        + (customizer == null ? "EMPTY" : customizer.GetType().FullName)
                        + " | our patch runs at Priority.First");
                }

                // NO ESCAPE CLAUSE HERE ANY MORE, AND REMOVING IT CLOSES AN EXPLOIT I OPENED.
                //
                // I had let a pawn standing somewhere forbidden path freely, so it would not be
                // trapped. That also let a player do this deliberately: switch flight OFF while
                // crossing a wall to land inside a vault, then switch it ON to leave through the
                // roofed wall. The safeguard became a door.
                //
                // Being inside is now handled by putting the pawn OUT - see
                // GameComponent_AvirFlightControl - rather than by relaxing the rule for it. A pawn
                // is never left in there long enough to need a route from it.

                // Only fills an empty slot. Overwriting another mod's customizer would silently
                // break whatever it was doing.
                if (customizer != null) return;

                customizer = AvirFlightBlockGrid.For(pawn.Map);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight route blocking failed: " + e, 0x4A5220);
            }
        }

        private static bool loggedCustomizerState;

        public static void MapGone_Postfix(Map __instance)
        {
            AvirFlightBlockGrid.Forget(__instance);
        }
    }
}
