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
    /// Decides, once per path, whether a flying Avir may phase - and enforces it by adding or
    /// removing the pawn from VEF's own set rather than by patching anything.
    ///
    /// WHY THIS AND NOT THE EIGHT PREVIOUS ATTEMPTS.
    ///
    /// VEF's phasing consults exactly one thing everywhere:
    ///
    ///     PhasingUtils.IsPhasing(p)  =>  PhasingPawns.Contains(p)
    ///
    /// and PhasingPawns is a public static HashSet. Every gate - CreateRequest, WalkableBy,
    /// CostToMoveIntoCell, BuildingBlockingNextPathCell, TryEnterNextPathCell, Reachability.CanReach,
    /// StartPath, fog - reads that one answer.
    ///
    /// Every earlier attempt patched those gates one at a time, so the router and the mover ended up
    /// holding different opinions: the route was planned through a wall and the movement gate then
    /// refused it, which is a deadlock, or the reverse, which is a pawn walking into an Ancient
    /// Danger. Those two can never be reconciled piecemeal - there are eight of them and they belong
    /// to somebody else's mod.
    ///
    /// Controlling membership closes or opens all eight AT ONCE and keeps them consistent by
    /// construction. There is no gate left to disagree with another.
    ///
    /// DECIDED ONCE PER PATH, NOT PER CELL. The answer is computed when the path is requested and
    /// left alone until the next request. That is what guarantees the route and the walk obey the
    /// same rule: changing the answer mid-path is precisely how the deadlock was produced before.
    /// </summary>
    public static class Patch_AvirPhasingGate
    {
        private static HediffDef flightHediff;
        private static bool lookedUp;

        private static HashSet<Pawn> phasingPawns;
        private static bool resolved;

        public static void Apply(Harmony harmony)
        {
            try
            {
                // EVICTION NEEDS A HOOK THAT FIRES WITHOUT THE PAWN ASKING FOR ANYTHING.
                //
                // I had it on the path request and the reachability check, and your log shows why
                // that was not enough: a pawn standing inside a sealed room asks for nothing. It
                // has no job, no route, no destination - so neither hook ever ran and the eviction
                // never fired. It is not that the rule was wrong; it was never reached.
                //
                // Pawn.TickRare is public and runs on every pawn roughly every four seconds
                // regardless of what it is doing. That is the only property that matters here.
                MethodInfo tickRare = AccessTools.Method(typeof(Pawn), "TickRare");
                if (tickRare != null)
                {
                    harmony.Patch(tickRare, postfix: new HarmonyMethod(
                        typeof(Patch_AvirPhasingGate), nameof(PawnTickRare_Postfix)));
                }
                else
                {
                    Log.Warning("[Avir] Pawn.TickRare not found - a pawn stranded inside a wall "
                        + "will not be rescued automatically.");
                }

                MethodInfo startPath = AccessTools.Method(typeof(Pawn_PathFollower), "StartPath");
                if (startPath == null)
                {
                    Log.Warning("[Avir] Phasing gate: Pawn_PathFollower.StartPath not found.");
                    return;
                }

                // Priority.First so the decision is already made when VEF's own StartPath prefix
                // and the path request that follows it read IsPhasing.
                harmony.Patch(startPath,
                    prefix: new HarmonyMethod(typeof(Patch_AvirPhasingGate),
                        nameof(StartPath_Prefix)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(Patch_AvirPhasingGate),
                        nameof(StartPath_Postfix)) { priority = Priority.Last });

                // REACHABILITY MUST ANSWER HONESTLY TOO, OR THE COLONY LOOPS.
                //
                // Job givers ask Reachability.CanReach constantly to decide what a pawn could do at
                // all. If that says "yes, you can reach the sealed vault" and the path then fails,
                // the giver picks the same job again next tick, forever - a pawn frozen in a retry
                // loop and a permanent drain on the tick.
                //
                // So the same rule is applied here, and only for the duration of the question
                // being asked. Nothing is left in a modified state afterwards.
                int reach = 0;
                foreach (MethodInfo m in typeof(Reachability).GetMethods(
                    BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "CanReach") continue;

                    // ONLY THE TraverseParms OVERLOAD. The other one takes a TraverseMode and
                    // carries no pawn at all, so there is nobody to decide about - and a Harmony
                    // prefix naming a parameter that overload does not have would simply fail to
                    // bind, silently.
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 0) continue;
                    if (ps[ps.Length - 1].ParameterType != typeof(TraverseParms)) continue;

                    harmony.Patch(m,
                        prefix: new HarmonyMethod(typeof(Patch_AvirPhasingGate),
                            nameof(CanReach_Prefix)) { priority = Priority.First },
                        postfix: new HarmonyMethod(typeof(Patch_AvirPhasingGate),
                            nameof(CanReach_Postfix)) { priority = Priority.Last });
                    reach++;
                }

                Log.Message("[Avir] Phasing gate bound to StartPath and " + reach
                    + " CanReach overload(s). VEF set found: " + (Set() != null));
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Phasing gate failed to apply: " + e);
            }
        }

        /// <summary>
        /// VEF's set, by reflection. The mod is a soft dependency, so this must degrade to null
        /// rather than fail to load when VEF is absent.
        /// </summary>
        /// <summary>Phasing by something that is not our flight - a living shadow, an amulet, another mod's power
        /// (Fix259, the author). Such a pawn standing in a wall is that power's business: flight rescue leaves it.</summary>
        internal static bool PhasesOtherwise(Pawn pawn)
        {
            if (pawn == null || AvirFlightRegistry.Contains(pawn)) return false;
            HashSet<Pawn> set = Set();
            return set != null && set.Contains(pawn);
        }

        private static HashSet<Pawn> Set()
        {
            if (resolved) return phasingPawns;
            resolved = true;

            try
            {
                Type utils = AccessTools.TypeByName("VEF.Hediffs.PhasingUtils");
                if (utils == null) return null;

                FieldInfo field = AccessTools.Field(utils, "PhasingPawns");
                phasingPawns = field?.GetValue(null) as HashSet<Pawn>;
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Could not reach VEF's PhasingPawns: " + e);
            }

            return phasingPawns;
        }

        public static void StartPath_Prefix(Pawn_PathFollower __instance)
        {
            try
            {
                HashSet<Pawn> set = Set();
                if (set == null) return;

                // Pawn_PathFollower.pawn is private - reached by FieldRef, the same way
                // Patch_AvirFlightAuto does it.
                Pawn pawn = __instance != null && patherPawn != null ? patherPawn(__instance) : null;
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return;

                // NO RESCUE CALL HERE. It used to sit on this line and it was catastrophic:
                // issuing a Goto from inside a path request means the job giver asks for a path,
                // which triggers this prefix, which issues another job. The log showed the result
                // plainly - "started 10 jobs in one tick" - and colony work stopped.
                //
                // Rescue is a PERIODIC concern and now lives only in periodic hooks. This prefix
                // does one thing: decide phasing.
                if (!HasFlightHediff(pawn)) return;

                // THE STRAIGHT-LINE TEST IS GONE, AND IT WAS THE LAST STRUCTURAL MISTAKE HERE.
                //
                // It decided phasing for a whole trip by sampling the line to the destination - but
                // the pathfinder never had to follow that line. Once phasing was on it could route
                // anywhere, so a destination whose line missed an Ancient Danger still produced a
                // route that cut through one. The pawn was not disobeying the rule; the rule was
                // being asked about a path nobody was going to walk.
                //
                // Flight is simply on for anyone flying now. Where they may not GO is enforced on
                // the route itself, cell by cell, by AvirFlightBlockGrid - which is the only place
                // that can bind the router and the mover to the same answer.
                set.Add(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Phasing gate failed: " + e, 0x4A5200);
            }
        }

        /// <summary>
        /// Answers reachability under the same rule as routing, for the length of one question.
        ///
        /// The pawn is lifted out of the phasing set only while CanReach runs, and put back
        /// immediately afterwards, so nothing anywhere else sees a changed state. Asking about a
        /// sealed room gets an honest no; asking about anything else gets the flying answer.
        ///
        /// This is what stops a job giver from repeatedly choosing work it can never reach.
        /// </summary>
        public static void CanReach_Prefix(LocalTargetInfo dest, TraverseParms traverseParams,
            out int __state)
        {
            __state = 0;
            // Fix254: everyone who has never carried the gene leaves here, on one hash lookup. This prefix sits on
            // every reachability question of every pawn on the map; the test below walks the destination's room
            // region by region, and used to run for humans, animals and mechs alike before the gene was ever asked
            // about. Nothing below applies to a pawn without the gene, so the answer is the same - only sooner.
            if (!AvirFlightCarriers.MayCarry(traverseParams.pawn)) return;

            // A SOLID CELL IS NEVER A PLACE TO END UP.
            //
            // Ordering a pawn into a wall - or into a fogged interior, which looks like one until
            // it is seen - let it stop there, and the walking grid will not path back OUT of an
            // impassable cell. That is how yours ended up stranded and then downed inside an
            // Ancient Danger wall.
            //
            // Refusing reachability here means the order is simply not offered, which is the same
            // answer the game gives for any other unreachable spot. Crossing such a cell is still
            // fine; only stopping in one is not.
            // A FORBIDDEN CELL IS NEVER A DESTINATION EITHER.
            //
            // Solid cells were already refused. Sealed rooms were not, so a player could still
            // order a pawn INTO an Ancient Danger and it would set off, cross the wall and stop
            // wherever the route died. Refusing here means the order is simply never offered - the
            // same answer the game gives for anywhere else it cannot reach.
            //
            // Crossing such a cell was already impossible via the route grid; this closes the last
            // way of asking to end up in one.
            if (DestinationForbidden(dest, traverseParams.pawn)) return;

            try
            {
                HashSet<Pawn> set = Set();
                Pawn pawn = traverseParams.pawn;
                if (set == null || pawn == null || pawn.Map == null) return;
                if (!AvirFlightUtility.CarriesFlightGene(pawn)) return;

                // NO RESCUE CALL HERE EITHER, AND FOR A WORSE REASON THAN ABOVE.
                //
                // Reachability is a QUESTION. Answering it must not change the world. Starting a
                // job from inside it meant every job giver that merely wondered whether a pawn
                // could reach something was ordering that pawn to fly somewhere - hundreds of
                // times per second, recursively, because the new job asks the same question again.

                IntVec3 c = dest.Cell;
                if (!c.IsValid || !c.InBounds(pawn.Map)) return;

                bool allowed = !Forbidden(pawn.Map, c);
                bool inSet = set.Contains(pawn);

                if (!allowed && inSet)
                {
                    // Honest no for a sealed room, so job givers never choose work they cannot
                    // reach and retry it every tick.
                    set.Remove(pawn);
                    __state = 1;
                    return;
                }

                if (allowed && !inSet && AvirFlightUtility.CanTakeOff(pawn))
                {
                    // ANSWER FOR WHAT THE PAWN CAN DO, NOT WHAT IT IS DOING - AND THIS DIRECTION
                    // WAS MISSING ENTIRELY.
                    //
                    // A grounded Avir carries no flight hediff, so it is not in the phasing set, so
                    // reachability said "no path" for anywhere behind a wall - and the order was
                    // refused before anything could take off. Take-off happens when a path is
                    // REQUESTED, and no path was ever requested, because the order never got that
                    // far. That is why an open-roofed building stayed unreachable.
                    //
                    // Reachability is a question about possibility. A pawn that could take off can
                    // get there, so it answers yes.
                    set.Add(pawn);
                    __state = 2;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Phasing gate reachability guard failed: " + e, 0x4A5202);
            }
        }

        /// <summary>
        /// Puts a pawn out of anywhere flight may not leave it, immediately.
        ///
        /// THIS LIVES HERE RATHER THAN IN THE GAME COMPONENT BECAUSE THIS CODE DEMONSTRABLY RUNS.
        /// Your logs are full of "[Avir gate] REFUSED" lines and contain no "Flight control is
        /// running" line at all, so the component that was supposed to do the evicting has never
        /// executed in your game. Rather than work out why a second time, the rule moves to the
        /// path that is already proving itself every tick.
        ///
        /// Called from the two hooks a stuck pawn hits constantly - it asks for a route and for
        /// reachability over and over, which is exactly the "no nodes" spam in your log. Every one
        /// of those attempts is now an opportunity to put it somewhere legal.
        /// </summary>
        public static void PawnTickRare_Postfix(Pawn __instance)
        {
            // CHEAPEST TEST FIRST. This now runs on every pawn on the map, animals included, so
            // the gene lookup - a cached table read - has to come before anything that touches the
            // path grid or the room system. Almost every pawn leaves on this line.
            if (__instance == null) return;

            // HEARTBEAT BEFORE THE GENE TEST, DELIBERATELY.
            //
            // FLIGHT_STATE.md concluded that this hook "does not run in this installation" because
            // no eviction line ever appeared in the log. It ran fine; CarriesFlightGene was
            // returning false for every pawn because of an overflow in its cache, so every call
            // left on the line above. Silence was read as a dead hook and three rounds went into
            // rewriting rules that were correct and unreachable.
            //
            // With the heartbeat placed FIRST, absence of this line is now real evidence.
            AvirFlightRescue.NoteHookAlive("Pawn.TickRare postfix");

            if (!AvirFlightUtility.CarriesFlightGene(__instance)) return;

            EvictIfStranded(__instance);
        }

        /// <summary>
        /// Now a one-line forward. This method used to teleport the pawn itself, with its own
        /// 25-cell search - a second, independent copy of the rescue that also lives in
        /// GameComponent_AvirFlightControl. Maximus never asked for teleporting; it was chosen for
        /// expedience. The shared version flies conscious pawns out under their own power and only
        /// moves the ones that cannot fly themselves.
        /// </summary>
        private static void EvictIfStranded(Pawn pawn)
        {
            AvirFlightRescue.CheckAndRescue(pawn);
        }


        /// <summary>Somewhere no pawn may end a journey: solid ground, or a place flight is not
        /// allowed to reach.</summary>
        private static bool DestinationForbidden(LocalTargetInfo dest, Pawn pawn)
        {
            Map map = pawn?.Map;
            if (map == null) return false;

            IntVec3 c = dest.Cell;
            if (!c.IsValid || !c.InBounds(map)) return false;

            // The whole rule, not half of it. Solid ground and sealed rooms are both places a
            // journey may not end, and CannotStandAt is the single place that decides.
            return AvirFlightRules.CannotStandAt(map, c);
        }

        public static void CanReach_Postfix(TraverseParms traverseParams, int __state)
        {
            if (__state == 0) return;

            try
            {
                HashSet<Pawn> set = Set();
                Pawn pawn = traverseParams.pawn;
                if (set == null || pawn == null) return;

                // Exactly undone, either way. Nothing outside this one question sees a changed set.
                if (__state == 1) set.Add(pawn);
                else set.Remove(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Phasing gate reachability restore failed: " + e, 0x4A5203);
            }
        }

        /// <summary>
        /// Puts the pawn back in the set the moment the route has been built.
        ///
        /// THIS IS WHY ORDERS WERE BEING REFUSED WITH "Cannot go here: No path".
        ///
        /// Reachability is asked long before StartPath ever runs - when you right-click, when a job
        /// giver considers a task, when the game decides whether a destination is even offerable.
        /// All of those go through Reachability.CanReach, which VEF answers from the same set. A
        /// pawn left OUT of the set by some earlier trip was therefore judged unable to reach
        /// anything behind a wall, and the order was rejected before the gate had any chance to
        /// reconsider.
        ///
        /// So membership is only borrowed. It is cleared for exactly as long as it takes the
        /// pathfinder to build this route - which is the one moment it must be honest - and handed
        /// straight back. The route is already committed by then, and the pawn simply walks it.
        /// </summary>
        public static void StartPath_Postfix(Pawn_PathFollower __instance)
        {
            try
            {
                HashSet<Pawn> set = Set();
                if (set == null || patherPawn == null) return;

                Pawn pawn = patherPawn(__instance);
                if (pawn == null || !HasFlightHediff(pawn)) return;

                set.Add(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Phasing gate restore failed: " + e, 0x4A5201);
            }
        }


        /// <summary>
        /// Somewhere flight must not carry a pawn.
        ///
        /// TWO RULES, AND THE SECOND IS THE ONE THAT MATTERS INSIDE A MOUNTAIN.
        ///
        /// A constructed roof means somebody built a ceiling: a base, a sealed vault, an Ancient
        /// Danger. Flight stops at it, always.
        ///
        /// But a structure dug INTO a hillside carries Overhead Mountain, not a constructed roof, so
        /// the roof test alone would wave it through - and mountain is explicitly allowed, because
        /// crossing rock is what flight is for. The second rule catches it: an enclosed proper room
        /// is an interior whoever roofed it. Open cave, open air and bare rock are none of those.
        ///
        /// Natural roofs - thin rock and overhead mountain - are otherwise fine. That is the whole
        /// point: fly over the hill, not into the room inside it.
        /// </summary>
        /// <summary>
        /// Reports, once every few seconds, exactly why a cell was refused.
        ///
        /// Added because the alternative is another round of me theorising about which of four
        /// conditions fired. It prints the cell, what is overhead, and every room flag the rule
        /// consults - so the line that is wrong names itself instead of being deduced.
        /// </summary>
        private static void Explain(Map map, IntVec3 c, string verdict)
        {
            // Fix254: a line for the log only when the log is asked to be wordy. It used to write one every four
            // seconds of flight for ever - and the game stops logging altogether after a thousand messages.
            if (!Prefs.LogVerbose) return;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - lastExplainTick < 240) return;
            lastExplainTick = now;

            RoofDef roof = c.GetRoof(map);
            Room room = c.GetRoom(map);

            Log.Message("[Avir gate] " + c + " -> " + verdict
                + " | roof=" + (roof == null ? "none" : roof.defName + (roof.isNatural ? " natural" : " CONSTRUCTED"))
                + " | room=" + (room == null ? "none"
                    : "outdoors:" + room.PsychologicallyOutdoors
                      + " proper:" + room.ProperRoom
                      + " openRoof:" + room.OpenRoofCount
                      + " cells:" + room.CellCount));
        }

        private static int lastExplainTick = -9999;

        public static bool ForbiddenCell(Map map, IntVec3 c) => Forbidden(map, c);

        /// <summary>
        /// Forwards to the mod-wide rule. The roof/room reasoning that used to live here is now in
        /// AvirFlightRules.IsSealed, unchanged - it was correct, it had simply never been reached
        /// on any path that also required the flight gene.
        ///
        /// Note this is not the full CannotStandAt. This predicate answers "may flight carry a
        /// pawn through here"; where a pawn may STOP is a different question, asked by
        /// CannotStandAt.
        ///
        /// A wall in the open still answers YES here - a boulder, a hillside, a fence, a hundred
        /// cells of unbroken mountain are all crossed on the wing. What answers NO is a wall that
        /// is part of a roofed room: that is a building, and buildings have doors.
        /// </summary>
        private static bool Forbidden(Map map, IntVec3 c)
        {
            if (AvirFlightRules.IsSealed(map, c))
            {
                Explain(map, c, "REFUSED: sealed");
                return true;
            }

            // THE SKIN OF A BUILDING, added because the sealed test could never have caught it.
            // IsSealed says NO the moment a room has a door - correctly, for its own question - so
            // a roofed colony with walls and doors was being flown into through the rock. Crossing
            // a hillside is flight; crossing somebody's wall is walking through walls.
            if (AvirFlightRules.IsEnclosureShell(map, c))
            {
                Explain(map, c, "REFUSED: building shell - use the door");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Pawn_PathFollower.pawn is private, so it is reached by FieldRef - the same way
        /// Patch_AvirFlightAuto does it.
        ///
        /// This declaration was lost when the rule above was rewritten; it had been sitting between
        /// two methods that were replaced together, and its absence produced four errors that all
        /// named the same missing name.
        /// </summary>
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> patherPawn =
            AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");

        public static bool IsFlying(Pawn pawn) => HasFlightHediff(pawn);

        private static bool HasFlightHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;

            if (!lookedUp)
            {
                lookedUp = true;
                flightHediff = DefDatabase<HediffDef>.GetNamedSilentFail("Flight_Hediff");
            }

            return flightHediff != null && pawn.health.hediffSet.HasHediff(flightHediff);
        }
    }
}
