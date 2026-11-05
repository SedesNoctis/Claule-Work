using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// ARRIVAL. A flying Avir has NOT arrived while it is standing inside solid rock.
    ///
    /// THE LOOP THIS EXISTS TO END, AND WHY EVERY EARLIER FIX MISSED IT.
    ///
    /// A work job does not say "walk to cell X". It says "get within touch of THAT THING", and the
    /// pather decides for itself when that has happened. The test it uses is the plain geometric
    /// one: for PathEndMode.Touch, a pawn has arrived the moment its cell is adjacent to the
    /// target. Nothing in it asks whether the pawn's own cell is a place a pawn may be.
    ///
    /// For a walker that never mattered - it could only ever reach adjacency from walkable ground.
    /// A flying Avir crosses walls, so the FIRST cell from which it is adjacent to a bed set
    /// against a wall is very often the wall itself. It stops there, mid-stone, and does the whole
    /// job from inside the rock.
    ///
    /// Patch_AvirDirectFlight already picks a legal stopping cell - ResolveStopCell - and the line
    /// it draws genuinely ends on open floor. That fix was correct and insufficient: the pawn never
    /// reached the end of that line, because it was told it had arrived several cells early. The
    /// route was right; the arrival test cut it short.
    ///
    /// Then the rescue noticed a stationary pawn inside stone and pulled it out. The work giver,
    /// unchanged and unbothered, handed the same job straight back. Out, in, out, in - the loop
    /// Maximus is watching. Neither half was wrong on its own; they were answering different
    /// questions about the same pawn, which is the failure mode this mod keeps rediscovering.
    ///
    /// So the arrival test is the thing to fix, not the rescue. Say "not yet" and the pawn simply
    /// keeps flying the remaining few cells of a route that already ended somewhere legal.
    ///
    /// THE DEADLOCK GUARD MATTERS AS MUCH AS THE RULE. If the path has run out of nodes there is
    /// nowhere left to go, and refusing arrival there would freeze the pawn in the wall forever -
    /// strictly worse than the loop. In that case arrival is allowed and the rescue handles it, as
    /// it did before. This patch only ever withholds arrival while there is still route left to
    /// walk.
    /// </summary>
    public static class Patch_AvirFlightArrival
    {
        private static AccessTools.FieldRef<Pawn_PathFollower, Pawn> patherPawn;
        private static AccessTools.FieldRef<Pawn_PathFollower, PawnPath> patherCurPath;
        private static bool loggedFirstHold;
        private static AccessTools.FieldRef<Pawn_PathFollower, LocalTargetInfo> patherDest;
        private static AccessTools.FieldRef<Pawn_PathFollower, PathEndMode> patherPeMode;
        private static bool loggedFirstStep;

        /// <summary>How far a pawn that "arrived" inside a wall may be stepped to a legal cell.</summary>
        private const float StepOutRadius = 2.9f;

        public static void PatherArrived_Prefix(Pawn_PathFollower __instance)
        {
            try
            {
                if (patherPawn == null || patherDest == null || patherPeMode == null) return;
                Pawn pawn = patherPawn(__instance);
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return;
                if (!AvirFlightUtility.IsFlying(pawn)) return;
                Map map = pawn.Map;
                if (!AvirFlightRules.CannotStandAt(map, pawn.Position)) return;

                LocalTargetInfo dest = patherDest(__instance);
                if (!dest.IsValid) return;
                PathEndMode mode = patherPeMode(__instance);

                IntVec3 best = IntVec3.Invalid;
                float bestDist = float.MaxValue;
                foreach (IntVec3 c in GenRadial.RadialCellsAround(pawn.Position, StepOutRadius, false))
                {
                    if (!c.InBounds(map) || AvirFlightRules.CannotStandAt(map, c)) continue;
                    if (!ReachabilityImmediate.CanReachImmediate(c, dest, map, mode, pawn)) continue;
                    if (c.GetFirstPawn(map) is Pawn other && other != pawn) continue;
                    float d = (c - pawn.Position).LengthHorizontalSquared;
                    if (d < bestDist) { bestDist = d; best = c; }
                }
                if (!best.IsValid) return;   // nowhere legal touches the target: the rescue takes it, as before

                IntVec3 from = pawn.Position;
                pawn.Position = best;
                pawn.Notify_Teleported(false, false);

                if (!loggedFirstStep)
                {
                    loggedFirstStep = true;
                    Log.Message("[Avir arrival] " + pawn.LabelShort + " arrived inside " + from
                        + "; stepped out to " + best + ", which still touches its target.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir arrival] step-out failed: " + e, 0x4A5265);
            }
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                patherPawn = AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");
                patherCurPath = AccessTools.FieldRefAccess<Pawn_PathFollower, PawnPath>("curPath");

                MethodInfo target = FindArrivalTest();
                if (target == null)
                {
                    // NAMED LOUDLY RATHER THAN FAILING QUIETLY. A silent miss here looks exactly
                    // like the bug still being present, and this mod has already lost several
                    // rounds to hooks that reported nothing and did nothing.
                    Log.Warning("[Avir arrival] Pawn_PathFollower arrival test not found. Avir will "
                        + "still stop inside walls when working on something set against one. The "
                        + "method wanted is the private parameterless bool that PatherTick calls "
                        + "before PatherArrived - read its name out of Assembly-CSharp.dll.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(Patch_AvirFlightArrival), nameof(Arrival_Postfix)));

                // THE SECOND LINE, AT THE ARRIVAL ITSELF. AtDestinationPosition is a one-line
                // method and the JIT folds it into PatherTick, so the postfix above may never run -
                // the pawn then "arrives" in the first wall cell touching its target. PatherArrived
                // is large enough to stay a real call, and there the pawn is stepped out of the
                // stone onto the nearest legal cell that still touches the target before the job
                // carries on. Out of the wall, at work, no rescue and no loop.
                MethodInfo arrived = AccessTools.Method(typeof(Pawn_PathFollower), "PatherArrived");
                patherDest = AccessTools.FieldRefAccess<Pawn_PathFollower, LocalTargetInfo>("destination");
                patherPeMode = AccessTools.FieldRefAccess<Pawn_PathFollower, PathEndMode>("peMode");
                if (arrived != null)
                    harmony.Patch(arrived, prefix: new HarmonyMethod(typeof(Patch_AvirFlightArrival), nameof(PatherArrived_Prefix)));
                else
                    Log.Warning("[Avir arrival] PatherArrived not found - Avir may still stop inside walls.");

                Log.Message("[Avir arrival] bound to Pawn_PathFollower." + target.Name
                    + " - a flying Avir no longer arrives inside stone.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir arrival] failed to apply: " + e);
            }
        }

        /// <summary>
        /// The exact name first, then a shape search.
        ///
        /// The name is the one this game version uses, but it is the sort of member that gets
        /// renamed between versions, and the whole patch is worthless if it binds to nothing. The
        /// fallback looks for what the method IS rather than what it is called: a private instance
        /// method on the pather, no parameters, returning bool, with "destination" in its name.
        /// Whatever it binds to is printed, so a wrong guess is visible in the log rather than
        /// silently active.
        /// </summary>
        private static MethodInfo FindArrivalTest()
        {
            MethodInfo exact = AccessTools.Method(typeof(Pawn_PathFollower), "AtDestinationPosition");
            if (exact != null) return exact;

            MethodInfo[] all = typeof(Pawn_PathFollower).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < all.Length; i++)
            {
                MethodInfo m = all[i];
                if (m.ReturnType != typeof(bool)) continue;
                if (m.GetParameters().Length != 0) continue;
                if (m.Name.IndexOf("Destination", StringComparison.OrdinalIgnoreCase) < 0) continue;

                return m;
            }

            return null;
        }

        /// <summary>
        /// Only ever turns a YES into a NO, and only for a pawn of ours that is airborne and inside
        /// something solid with route still to walk. Every other pawn in the game leaves on the
        /// first or second line.
        /// </summary>
        public static void Arrival_Postfix(Pawn_PathFollower __instance, ref bool __result)
        {
            // Not arrived anyway: nothing to withhold. This is the overwhelmingly common case and
            // costs one branch.
            if (!__result) return;

            try
            {
                if (patherPawn == null) return;

                Pawn pawn = patherPawn(__instance);
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return;

                // Cheap gene-cache read; every non-Avir in the game leaves here.
                if (!AvirFlightUtility.IsFlying(pawn)) return;

                // Somewhere it may legitimately stop. Nothing to do - and this covers ordinary
                // floor, open cave, its own bedroom, everything.
                if (!AvirFlightRules.CannotStandAt(pawn.Map, pawn.Position)) return;

                // THE DEADLOCK GUARD. No route left means no way onward; freezing the pawn in the
                // rock would be worse than the loop this patch exists to end. Let it arrive and
                // leave it to AvirFlightRescue, exactly as before.
                PawnPath path = patherCurPath != null ? patherCurPath(__instance) : null;
                if (path == null || !path.Found || path.NodesLeftCount <= 1) return;

                __result = false;

                if (!loggedFirstHold)
                {
                    loggedFirstHold = true;
                    Log.Message("[Avir arrival] " + pawn.LabelShort + " was about to stop inside "
                        + pawn.Position + " while adjacent to its target; held airborne for the "
                        + "remaining " + path.NodesLeftCount + " cells of its route instead.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir arrival] postfix failed: " + e, 0x4A5264);
            }
        }
    }
}
