using System.Collections.Generic;
using Verse;

namespace FlightGene
{
    /// <summary>
    /// A SECOND PERIODIC HOOK, KEPT ONLY AS INSURANCE AND AS EVIDENCE.
    ///
    /// This class used to hold a full copy of the flight rules: its own solid/sealed tests, its own
    /// teleport, its own take-off and landing decisions - roughly three hundred lines duplicating
    /// what the phasing gate and the flight monitor already did, written before those existed.
    /// It never once ran its own logic, because every path through it began with
    /// AvirFlightUtility.CarriesFlightGene, which was returning false for every pawn in the game.
    ///
    /// All of that is gone. What remains does two things:
    ///
    ///   1. Calls the one shared rescue, so a stranded pawn is caught even if the flight hediff
    ///      is absent - the monitor only ticks while a pawn is airborne, and a pawn that ended up
    ///      inside stone WITHOUT flying has no monitor to notice it.
    ///
    ///   2. Announces itself once, before any condition, so that if it truly does not execute in
    ///      this installation the log finally says so. The old heartbeat sat behind three tests
    ///      including the broken cache, which is why its absence was misread as a dead hook.
    ///
    /// The rescue is idempotent, so this overlapping with Pawn.TickRare and with the monitor costs
    /// nothing but a few wasted calls.
    /// </summary>
    public class GameComponent_AvirFlightControl : GameComponent
    {
        /// <summary>Two seconds. Fast enough that nothing sits in a wall long, slow enough to be
        /// invisible: the sweep only touches pawns carrying the flight gene, and that test is a
        /// cached table read.</summary>
        private const int Interval = 120;

        private bool announced;

        public GameComponent_AvirFlightControl(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (!announced)
            {
                announced = true;
                Log.Message("[Avir flight] GameComponent_AvirFlightControl is executing.");
            }

            if (Find.TickManager.TicksGame % Interval != 0) return;

            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                // IReadOnlyList, not List - MapPawns hands out a read-only view. Indexed rather
                // than foreach because that view allocates an enumerator otherwise.
                IReadOnlyList<Pawn> pawns = maps[m]?.mapPawns?.AllPawnsSpawned;
                if (pawns == null) continue;

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];

                    // Cheapest test first: this runs over every pawn on the map, animals included,
                    // and almost all of them leave on this line.
                    if (pawn == null || !AvirFlightUtility.CarriesFlightGene(pawn)) continue;

                    AvirFlightRescue.NoteHookAlive("GameComponent_AvirFlightControl");
                    AvirFlightRescue.CheckAndRescue(pawn);

                    // AUTOMATIC LANDING. Asked here rather than from a Harmony patch because every
                    // per-step hook tried for this was inlined by the JIT and silently never ran -
                    // three separate rounds were lost to that. A GameComponent cannot be inlined
                    // away.
                    //
                    // Not gated on the player's switch: a pawn hovering at a workbench is wrong
                    // regardless of what the player asked for. ShouldLandNow refuses to land
                    // anywhere the pawn could not stand, so this cannot drop anyone into stone.
                    if (AvirAutoFlight.ShouldLandNow(pawn))
                    {
                        AvirFlightUtility.Land(pawn);
                        continue;
                    }

                    // AUTOMATIC TAKEOFF IN MID-WALK. The counterpart of the line above, and the
                    // reason rivers were never crossed on the wing.
                    //
                    // Takeoff used to be decided in ONE place only: the StartPath prefix, from a
                    // straight line drawn to the destination at the instant the route was
                    // requested. A pawn whose walking route bends through a ford the straight line
                    // never touched was never asked again, and waded across.
                    //
                    // Asked here, once every two seconds, against the route the pawn is actually
                    // walking. The look-ahead in ShouldTakeOffNow is long enough that the pawn
                    // leaves the ground on the bank rather than in the water.
                    if (AvirAutoFlight.ShouldTakeOffNow(pawn))
                    {
                        AvirFlightUtility.TakeOff(pawn, automatic: true);

                        // The route it is walking was planned for legs and goes round. Now that it
                        // is airborne, ask for a new one - Patch_AvirDirectFlight will draw the
                        // straight line.
                        AvirFlightRepath.Request(pawn);
                    }
                }
            }
        }
    }
}
