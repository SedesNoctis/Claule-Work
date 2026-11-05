using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir.Traits
{
    /// <summary>
    /// Two behavior changes from the first click-to-target version:
    ///   1. No more "not dark enough" rejection loop. If the chosen spot isn't already dark, a
    ///      real Eclipse incident is forced on that map instead of rejecting the click - this was
    ///      the actual source of "can't cancel", since removing the rejection case removes the
    ///      loop it was stuck in. Not-standable and not-yet-explored are still rejected (see #2),
    ///      but those are rare, deliberate edge cases rather than "most of a lit map."
    ///   2. Added a fog-of-war check - can't target a cell the player hasn't actually seen yet,
    ///      which is what "walking through walls into an unexplored Ancient Danger" really was.
    ///      Clicking through a wall visually is just how top-down targeting works and isn't
    ///      something to fix; ending up somewhere never seen is the actual problem, and is now
    ///      checkable and rejected the same way an unstandable cell is.
    /// </summary>
    public static class ShroudgatesUtility
    {
        private static MethodInfo glowMethod;
        private static bool glowMethodResolved;

        public static void BeginDestinationTargeting(List<Pawn> group, Map targetMap)
        {
            if (group == null || group.Count == 0 || targetMap == null) return;

            try
            {
                CameraJumper.TryJump(targetMap.Center, targetMap);
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Shroudgates: CameraJumper.TryJump failed, proceeding with targeting anyway (view may not have switched maps): " + e);
            }

            StartTargeting(group, targetMap);
        }

        private static void StartTargeting(List<Pawn> group, Map targetMap)
        {
            TargetingParameters parms = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetItems = false,
            };

            Find.Targeter.BeginTargeting(parms, (LocalTargetInfo target) => HandleCellChosen(target, group, targetMap));
        }

        private static void HandleCellChosen(LocalTargetInfo target, List<Pawn> group, Map targetMap)
        {
            if (!target.IsValid || !target.Cell.IsValid)
            {
                Messages.Message("Shroudgates cancelled.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            IntVec3 cell = target.Cell;

            if (!cell.InBounds(targetMap) || !cell.Standable(targetMap))
            {
                Messages.Message("That spot won't work - pick a clear, standable location.", MessageTypeDefOf.RejectInput, false);
                StartTargeting(group, targetMap);
                return;
            }

            if (targetMap.fogGrid != null && targetMap.fogGrid.IsFogged(cell))
            {
                Messages.Message("You haven't seen that place yet - pick somewhere already explored.", MessageTypeDefOf.RejectInput, false);
                StartTargeting(group, targetMap);
                return;
            }

            if (!IsCellDark(targetMap, cell))
            {
                ForceEclipse(targetMap);
            }

            Log.Message("[Avir] Shroudgates: teleporting " + group.Count + " pawn(s) to " + cell + " on " + targetMap);

            foreach (Pawn pawn in group)
            {
                if (pawn == null || pawn.Dead) continue;
                TeleportSinglePawn(pawn, cell, targetMap);
            }
        }

        // Real vanilla incident, looked up by name rather than a DefOf field (same defensive
        // pattern as everywhere else uncertain - a wrong guess here just logs and skips instead
        // of failing to compile).
        private static void ForceEclipse(Map map)
        {
            IncidentDef eclipseDef = DefDatabase<IncidentDef>.GetNamedSilentFail("Eclipse");
            if (eclipseDef == null)
            {
                Log.Warning("[Avir] Shroudgates: IncidentDef 'Eclipse' not found - can't force darkness, teleporting to a lit spot as-is.");
                return;
            }

            try
            {
                IncidentParms parms = new IncidentParms { target = map };
                bool success = eclipseDef.Worker.TryExecute(parms);
                Log.Message("[Avir] Shroudgates: forced Eclipse on " + map + " - success: " + success);
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Shroudgates: forcing Eclipse threw: " + e);
            }
        }

        private static bool IsCellDark(Map map, IntVec3 cell)
        {
            float? glow = TryGetGlowAt(map, cell);
            if (glow.HasValue) return glow.Value <= 0.01f;

            return cell.Roofed(map);
        }

        private static float? TryGetGlowAt(Map map, IntVec3 cell)
        {
            EnsureGlowMethodResolved(map);
            if (glowMethod == null) return null;

            try
            {
                object result = glowMethod.Invoke(map.glowGrid, new object[] { cell });
                if (result is float f) return f;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Shroudgates: glow method invocation failed, falling back to roofed-only detection: " + e, 0x4A51C3);
            }

            return null;
        }

        private static void EnsureGlowMethodResolved(Map map)
        {
            if (glowMethodResolved) return;
            glowMethodResolved = true;

            object glowGrid = map.glowGrid;
            if (glowGrid == null)
            {
                Log.Warning("[Avir] Shroudgates: Map.glowGrid is null - falling back to roofed-only darkness detection.");
                return;
            }

            Type glowGridType = glowGrid.GetType();
            string[] candidates = { "GameGlowAt", "GroundGlowAt", "VisualGlowAt", "PsychGlowAt" };

            foreach (string name in candidates)
            {
                MethodInfo m = glowGridType.GetMethod(name, new[] { typeof(IntVec3) });
                if (m != null && m.ReturnType == typeof(float))
                {
                    glowMethod = m;
                    Log.Message("[Avir] Shroudgates: found glow method '" + name + "' on GlowGrid - using real light-level detection.");
                    return;
                }
            }

            Log.Warning("[Avir] Shroudgates: couldn't find a glow-query method on GlowGrid by any candidate name - "
                + "falling back to roofed-only darkness detection.");
        }

        private static void TeleportSinglePawn(Pawn pawn, IntVec3 destCell, Map targetMap)
        {
            try
            {
                IntVec3 spawnCell = CellFinder.RandomClosewalkCellNear(destCell, targetMap, 3);
                if (!spawnCell.IsValid) spawnCell = destCell;

                if (pawn.Spawned)
                {
                    TrySpawnSkipEffect(pawn.Position, pawn.Map);
                    pawn.DeSpawn(DestroyMode.Vanish);
                }

                GenSpawn.Spawn(pawn, spawnCell, targetMap);
                TrySpawnSkipEffect(spawnCell, targetMap);
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Shroudgates teleport failed for " + pawn.LabelShortCap + ": " + e);
            }
        }

        // Same skip-effect approach as Eye of the Crow's resurrection teleport (try real Royalty
        // Skip flecks by name, fall back to a generic spark if none resolve).
        private static void TrySpawnSkipEffect(IntVec3 cell, Map map)
        {
            if (map == null) return;

            try
            {
                string[] candidates = { "PsycastSkipFlashExit", "PsycastSkipOuterRingExit", "PsycastSkipInnerExit", "SkipFlashExit" };
                foreach (string name in candidates)
                {
                    FleckDef def = DefDatabase<FleckDef>.GetNamedSilentFail(name);
                    if (def != null)
                    {
                        FleckMaker.Static(cell, map, def, 1f);
                        return;
                    }
                }

                FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            }
            catch
            {
                // Cosmetic only - silently skip.
            }
        }
    }
}
