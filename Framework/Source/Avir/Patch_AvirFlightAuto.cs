using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// AUTOMATIC TAKEOFF. The pawn lifts off exactly when the next cell of its route is one it
    /// could not walk into, and HediffComp_FlightMonitor puts it down again once the route is
    /// ordinary ground.
    ///
    /// WHY THE HOOK IS HERE AND NOT ON A TIMER. A sweep every N ticks would have to guess how far
    /// ahead to look and would take off early or late depending on move speed. SetupMoveIntoNextCell
    /// fires exactly once per cell entered, and nextCell is precisely the cell about to be stepped
    /// into - so takeoff lands on the correct step every time, at any speed, with no look-ahead
    /// constant to tune.
    ///
    /// THIS IS THE HOTTEST PATCH IN THE MOD AND IT IS BUILT AROUND THAT. It runs for every moving
    /// pawn, colonist and raider alike, once per cell. Two gates, cheapest first:
    ///
    ///   1. Does this pawn carry the flight gene? A ConditionalWeakTable read and an int compare,
    ///      re-derived from the gene tracker only every 2500 ticks. Every non-Avir in the game -
    ///      raiders, animals, mechs, visitors, and the other 399 mods' pawns - exits here, and
    ///      nothing further ever runs for them.
    ///   2. Does the next cell actually need wings? One array read against the walking path grid.
    ///
    /// Only past both does it look at rest level and hediffs. The uncached version of gate 1 was a
    /// HasActiveGene call, which is a gene-tracker walk; doing that per step per pawn WOULD have
    /// been measurable, which is what the cache is for.
    ///
    /// GATING ON FEARBORN INSTEAD WOULD BE STRICTLY WORSE. Flight_Gene lists Fearborn as its
    /// prerequisite, so every flight carrier is Fearborn by construction - a Fearborn test would
    /// exclude nobody the flight test does not already exclude, and would let through every
    /// Fearborn Avir who cannot fly. The flight gene is the tighter filter.
    ///
    /// WHERE flight may carry them is decided by Patch_AvirPhasingGate. This file only decides WHEN
    /// to leave the ground; the two are deliberately separate questions.
    /// </summary>
    public static class Patch_AvirFlightAuto
    {
        private static bool announcedTakeoff;
        private static readonly System.Collections.Generic.HashSet<int> announcedLanding =
            new System.Collections.Generic.HashSet<int>();

        private static AccessTools.FieldRef<Pawn_PathFollower, IntVec3> nextCellRef;
        private static AccessTools.FieldRef<Pawn_PathFollower, Pawn> patherPawnRef;

        public static void Apply(Harmony harmony)
        {
            try
            {
                nextCellRef = AccessTools.FieldRefAccess<Pawn_PathFollower, IntVec3>("nextCell");
                patherPawnRef = AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");

                // NO PER-STEP PATCH AT ALL ANY MORE.
                //
                // Every per-step hook I tried was inlined by the JIT and silently never ran:
                // GenGrid.WalkableBy, then Pawn_PathFollower.SetupMoveIntoNextCell, then the same
                // again via PatherTick. Each reported binding and then did nothing, which is
                // indistinguishable from a logic error and cost a round to rule out.
                //
                // Takeoff and landing now live in GameComponent_AvirFlightControl, which is not a
                // patch and therefore cannot be inlined away. StartPath below is kept only as an
                // early lift-off so the route is planned as a flyer where possible; the component
                // is what actually guarantees the behaviour.

                MethodInfo startPath = AccessTools.Method(typeof(Pawn_PathFollower), "StartPath");
                if (startPath != null)
                {
                    harmony.Patch(startPath,
                        prefix: new HarmonyMethod(typeof(Patch_AvirFlightAuto), nameof(StartPath_Prefix)));
                }

                Log.Message("[Avir] Early lift-off patch applied to StartPath. Landing and takeoff "
                    + "themselves are handled by GameComponent_AvirFlightControl.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Auto-flight patch failed to apply: " + e);
            }
        }

        /// <summary>
        /// Takes off before the route is planned, when the destination cannot be walked to
        /// directly.
        ///
        /// The test is deliberately cheap and blunt: if the straight line from here to there
        /// crosses anything the walking grid refuses, fly. It does not try to work out whether
        /// flying would be shorter - that would mean running the pathfinder twice per request, and
        /// the answer is nearly always yes anyway for an Avir.
        /// </summary>
        public static void StartPath_Prefix(Pawn_PathFollower __instance, LocalTargetInfo dest)
        {
            try
            {
                if (patherPawnRef == null) return;

                Pawn pawn = patherPawnRef(__instance);
                if (pawn == null) return;

                // THE RULE SET NOW LIVES IN AvirAutoFlight, NOT HERE.
                //
                // What used to be inline was a single question - "is anything impassable on the
                // straight line" - which let a pawn wade a river or trudge through marsh because
                // neither is technically blocked. The rules also have to cover the player's switch,
                // drafting, and a rest floor, and having three files each carry part of that is
                // precisely how the sealed-cell rule ended up contradicting itself this morning.
                //
                // The hook stays exactly where it was: StartPath, before the route is planned. A
                // pawn that decides to fly after planning is already walking the long way round.
                if (!AvirAutoFlight.ShouldTakeOffFor(pawn, dest.Cell)) return;

                if (!announcedTakeoff)
                {
                    announcedTakeoff = true;
                    Log.Message("[Avir] Auto-takeoff on path request: " + pawn.LabelShort
                        + " heading for " + dest.Cell + ".");
                }

                AvirFlightUtility.TakeOff(pawn, automatic: true);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Auto-takeoff on StartPath failed: " + e, 0x4A51F7);
            }
        }

        /// <summary>
        /// Runs every tick a pawn is moving. Lands an airborne Avir the moment its next step would
        /// carry it under a ceiling, and lifts one off when the next step needs wings and does not.
        ///
        /// Landing rather than refusing the cell, because refusing deadlocks: VEF plans the route
        /// as if the pawn phases through everything, so a refusal leaves it asking for the same
        /// route forever. On landing, the step fails, the pather repaths, and the pawn is an
        /// ordinary walker by then - so the new route goes around, and nothing is stuck.
        /// </summary>

    }
}
