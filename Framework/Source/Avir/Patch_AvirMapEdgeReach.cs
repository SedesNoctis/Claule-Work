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
    /// "AVIR FROM A'SHAIGN HAVE BEEN TRAPPED AND ARE DIGGING OUT TO ESCAPE."
    ///
    /// WHAT THE GAME WAS ACTUALLY ASKING. Visitors and trade caravans are watched by a trigger that
    /// asks, periodically, whether each of them can still reach the edge of the map. If the answer
    /// is no, the game concludes the colony has walled them in, and they turn hostile and start
    /// mining their way out. That is correct behaviour and Maximus's colony did nothing wrong.
    ///
    /// The question is answered by walking REGIONS - the same connected-floor graph the pathfinder
    /// uses for pawns with legs. An Avir caravan that flew in over a ridge is standing somewhere no
    /// walker could have got to, so the honest walking answer is "no route to the edge". Trapped.
    ///
    /// This is not the phasing gate failing. The gate patches Reachability.CanReach, and the map
    /// edge check is a DIFFERENT method that runs its own region flood and never consults it. The
    /// flight system has been answering one question correctly while the game asked another.
    ///
    /// THE RULE. Outdoors, a pawn with wings can always get to the edge: open ground, hillsides and
    /// walls standing in the open are all crossed on the wing, and the only thing closed to flight
    /// is the shell of a roofed building. Indoors is left exactly as vanilla found it - a roofed
    /// room is entered and left by its doors, so if an Avir really has been shut inside one, the
    /// walking answer is already the right answer and they really are trapped.
    ///
    /// That split is what keeps the fix from being a blank cheque. Wall a trader into a room and
    /// they still dig out; let a trader land on a mountainside and they no longer panic about it.
    /// </summary>
    public static class Patch_AvirMapEdgeReach
    {

        public static void Apply(Harmony harmony)
        {
            try
            {
                List<MethodInfo> targets = FindMapEdgeChecks();
                if (targets.Count == 0)
                {
                    Log.Warning("[Avir mapedge] no map-edge reachability method found on "
                        + "Reachability. Flying Avir visitors may still be reported as trapped when "
                        + "they land somewhere no walker could reach. The method wanted returns "
                        + "bool and has \"MapEdge\" in its name.");
                    return;
                }

                // EVERY overload, not only the first found: the trigger may call another one, and a postfix on the wrong
                // one changes nothing.
                foreach (MethodInfo target in targets)
                {
                    try
                    {
                        harmony.Patch(target, postfix: new HarmonyMethod(
                            typeof(Patch_AvirMapEdgeReach), nameof(MapEdge_Postfix)));
                        Log.Message("[Avir mapedge] bound to Reachability." + target.Name + ".");
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[Avir mapedge] could not bind " + target.Name + ": " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir mapedge] failed to apply: " + e);
            }
        }

        /// <summary>
        /// Bound by shape rather than by a remembered signature. There may be more than one
        /// overload; the one that matters is whichever the trapped-visitor trigger calls, and both
        /// take a TraverseParms, which is the only thing this patch needs.
        /// </summary>
        private static List<MethodInfo> FindMapEdgeChecks()
        {
            var found = new List<MethodInfo>();
            MethodInfo[] all = typeof(Reachability).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            for (int i = 0; i < all.Length; i++)
            {
                MethodInfo m = all[i];
                if (m.ReturnType != typeof(bool)) continue;
                if (m.Name.IndexOf("MapEdge", StringComparison.OrdinalIgnoreCase) < 0) continue;

                // the postfix is bound by the parameter's NAME: only a method that has exactly "traverseParms" can take it
                ParameterInfo[] ps = m.GetParameters();
                for (int p = 0; p < ps.Length; p++)
                {
                    if (ps[p].ParameterType == typeof(TraverseParms) && ps[p].Name == "traverseParms") { found.Add(m); break; }
                }
            }

            return found;
        }

        /// <summary>
        /// One line per pawn per session, because "the fix did not work" and "the fix never ran"
        /// look identical from the map and cost a whole round trip to tell apart.
        /// </summary>
        private static readonly HashSet<int> explained = new HashSet<int>();

        private static void Explain(Pawn pawn, IntVec3 c, string why)
        {
            if (pawn == null || !explained.Add(pawn.thingIDNumber)) return;

            Log.Message("[Avir mapedge] " + pawn.LabelShort + " at " + c
                + " could not walk to the map edge and was about to be counted as trapped; "
                + why + ", so counted as free.");
        }

        /// <summary>
        /// Only ever turns a NO into a YES, and only for a winged pawn standing out of doors.
        /// Everything else in the game leaves on the first line.
        ///
        /// THE PARAMETER IS traverseParms, NOT traverseParams. Harmony binds postfix arguments by
        /// NAME; the misspelling meant it could not inject the argument, threw at patch time, and
        /// Apply's catch turned that into a log warning nobody was reading. The patch never ran
        /// once - which is why the trapped-caravan message came back unchanged and looked like a
        /// failed fix rather than an absent one. Read out of the assembly's Param table now, not
        /// typed from habit: RimWorld spells it "Parms" throughout.
        /// </summary>
        public static void MapEdge_Postfix(TraverseParms traverseParms, ref bool __result)
        {
            // Already reachable on foot. Nothing to add, and this is the common case.
            if (__result) return;

            try
            {
                Pawn pawn = traverseParms.pawn;
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return;

                // Carries wings - not "is currently airborne". The trigger fires while they are
                // standing about trading, and a landed Avir is still an Avir that can leave.
                if (!AvirFlightUtility.CarriesFlightGene(pawn)) return;

                Map map = pawn.Map;
                IntVec3 c = pawn.Position;
                if (!c.InBounds(map)) return;

                // INSIDE SOLID ROCK IS NOT "SHUT IN A ROOM", AND THIS IS THE HOLE THAT LET THE BUG
                // THROUGH THE FIRST FIX.
                //
                // A trader that flew in and came to rest inside a wall is standing on a cell with
                // no region at all, which is exactly why vanilla answered "no route to the edge".
                // But a wall cell's GetRoom does not return null - it resolves to the room the wall
                // borders - so the check below saw a proper roofed room, concluded "shut inside a
                // building, vanilla is right", and left them trapped. The message came back and I
                // had patched the correct method with the wrong condition.
                //
                // Somewhere a pawn cannot even stand is never a room they were shut into. Wings
                // out, and let the rescue put them down somewhere real.
                if (AvirFlightRules.CannotStandAt(map, c))
                {
                    __result = true;
                    Explain(pawn, c, "inside solid rock, not shut in");
                    return;
                }

                // SHUT INSIDE A BUILDING IS STILL SHUT IN. The shell of a roofed room is closed to
                // flight, so an Avir in a locked storeroom is as stuck as anyone else and vanilla's
                // answer stands. Only the outdoor case is overridden.
                //
                // Asked of the cell the pawn is ACTUALLY IN, which is why the test above has to run
                // first: for a pawn in a wall this question has no honest answer.
                Room room = c.GetRoom(map);
                if (room != null && room.ProperRoom && !room.PsychologicallyOutdoors) return;

                __result = true;
                Explain(pawn, c, "can fly to the edge");

            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir mapedge] postfix failed: " + e, 0x4A5268);
            }
        }
    }
}
