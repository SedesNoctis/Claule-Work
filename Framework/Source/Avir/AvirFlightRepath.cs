using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// AFTER A MID-WALK TAKEOFF, THE ROUTE IS STILL A WALKER'S ROUTE.
    ///
    /// This is the same timing problem AvirAutoFlight opens with, seen from the other end. A pawn
    /// plans its whole route before it takes a step, so a pawn that leaves the ground halfway along
    /// is airborne on a path that was drawn around the river rather than across it. It would arrive
    /// eventually, having flown the long way for no reason - which looks, from outside, exactly
    /// like not having taken off at all.
    ///
    /// So the pather is asked for a fresh route. Patch_AvirDirectFlight then intercepts that
    /// request and draws the straight line, because by now the pawn IS flying.
    ///
    /// WHY needNewPath AND NOT StartPath. Calling StartPath again would mean supplying a
    /// PathEndMode, and the only honest source for it is the pather's own private peMode field.
    /// Guessing OnCell there would silently break every Touch job - the pawn would be told to stand
    /// IN the workbench, the guest, the wall being mined. Raising the pather's own flag instead
    /// leaves destination and end mode exactly as they were and lets the engine re-request them
    /// itself on its next tick.
    ///
    /// If the field cannot be found, this does nothing at all and says so once. The pawn still
    /// flies; it merely flies the old route. Degrading quietly is the point.
    /// </summary>
    public static class AvirFlightRepath
    {
        private static AccessTools.FieldRef<Pawn_PathFollower, bool> needNewPath;
        private static bool bound;
        private static bool warned;

        private static void EnsureBound()
        {
            if (bound) return;
            bound = true;

            try
            {
                needNewPath = AccessTools.FieldRefAccess<Pawn_PathFollower, bool>("needNewPath");
            }
            catch
            {
                needNewPath = null;
            }

            if (needNewPath == null)
            {
                Log.Warning("[Avir flight] Pawn_PathFollower.needNewPath not found. A pawn that "
                    + "takes off in mid-walk will keep its walking route instead of flying "
                    + "straight; everything else is unaffected.");
            }
        }

        public static void Request(Pawn pawn)
        {
            EnsureBound();
            if (needNewPath == null) return;

            Pawn_PathFollower pather = pawn?.pather;
            if (pather == null || !pather.Moving) return;

            try
            {
                needNewPath(pather) = true;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[Avir flight] re-path request failed: " + e);
                }
            }
        }
    }
}
