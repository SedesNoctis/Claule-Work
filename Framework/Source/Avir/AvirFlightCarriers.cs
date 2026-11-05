using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FlightGene
{
    /// <summary>
    /// WHO COULD FLY AT ALL - the crowd gate for the flight hooks.
    ///
    /// The flight patches sit on pathfinding: Pathing.For, GetPathContext, WalkableBy, StartPath.
    /// Those run for every pawn that looks for a path or checks reachability, and each one used
    /// to ask the full question - airborne? can take off? carries the gene? - for humans,
    /// animals and mechs alike. A global "nobody has wings" switch would not help, because it
    /// switches off the moment two Avir walk onto the map.
    ///
    /// This set answers the cheap question first, PER PAWN: has this pawn ever carried the
    /// flight gene? Everybody who has not leaves on one hash lookup, however many Avir there
    /// are. Only carriers go on to the real checks.
    ///
    /// It is a prefilter, never the final word:
    ///   - a stale entry (gene lost, pawn gone) only sends that pawn on to the exact check, which
    ///     answers no - costs a little, breaks nothing;
    ///   - a MISSING entry would silently ground a real flyer, so the set is filled from every
    ///     direction a carrier can appear: the gene being added, any pawn spawning (which also
    ///     covers map load), and a full sweep once the game has finished loading.
    /// </summary>
    public static class AvirFlightCarriers
    {
        private static readonly HashSet<int> ids = new HashSet<int>();

        public static bool MayCarry(Pawn pawn)
        {
            return pawn != null && ids.Contains(pawn.thingIDNumber);
        }

        public static void Add(Pawn pawn)
        {
            if (pawn != null) ids.Add(pawn.thingIDNumber);
        }

        public static void Remove(Pawn pawn)
        {
            if (pawn != null) ids.Remove(pawn.thingIDNumber);
        }

        public static void Clear()
        {
            ids.Clear();
        }

        /// <summary>Adds the pawn if it carries the gene, active or not.</summary>
        public static void Consider(Pawn pawn)
        {
            if (pawn?.genes == null) return;

            GeneDef def = AvirFlightUtility.FlightGeneDef;
            if (def == null) return;

            if (pawn.genes.GetGene(def) != null) ids.Add(pawn.thingIDNumber);
        }

        /// <summary>Every spawned pawn on every map. Load-time only.</summary>
        public static void Sweep()
        {
            try
            {
                List<Map> maps = Find.Maps;
                if (maps == null) return;

                for (int m = 0; m < maps.Count; m++)
                {
                    IReadOnlyList<Pawn> pawns = maps[m].mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Consider(pawns[i]);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Flight carrier sweep failed: " + e);
            }
        }
    }

    /// <summary>
    /// Starts each game from an empty set - the game constructor runs before any pawn of that
    /// game exists, new or loaded - and sweeps the maps once everything is in place.
    /// </summary>
    public class GameComponent_AvirFlightCarriers : GameComponent
    {
        public GameComponent_AvirFlightCarriers(Game game)
        {
            AvirFlightCarriers.Clear();
        }

        public override void FinalizeInit()
        {
            AvirFlightCarriers.Sweep();
        }
    }

    /// <summary>
    /// Any pawn arriving on a map: raids, caravans, visitors, and every spawned pawn when a map
    /// loads. SpawnSetup is not a hot path, so the gene check here is free in practice.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_AvirFlightCarriers_Spawn
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                AvirFlightCarriers.Consider(__instance);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight carrier check on spawn failed: " + e, 0x4A51F9);
            }
        }
    }
}
