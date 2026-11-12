using System.Collections.Generic;

using Avir;
using RimWorld;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// Prints, once, what the pathing system actually thinks of the cells around a flight-capable
    /// Avir standing next to something it should be able to cross.
    ///
    /// NO GIZMO, NO DEV MODE, NO BUTTON. The previous attempt put this behind a gizmo on
    /// Pawn.GetGizmos and the button never appeared even with dev mode on - so the tool meant to end
    /// the guessing became one more thing to guess about. A GameComponent runs unconditionally
    /// every tick the game is loaded; there is nothing left to go wrong between writing it and
    /// seeing the output.
    ///
    /// It fires at most ONCE per game session, on the first flight-capable pawn it finds adjacent
    /// to a cell the walking grid refuses. Then it stops forever. No spam, no performance cost
    /// after the first hit.
    /// </summary>
    public class GameComponent_AvirFlightProbe : GameComponent
    {
        private const int CheckInterval = 120;

        /// <summary>Re-armable rather than once-per-session. The old version set done=true
        /// forever after the first hit, so walking a pawn up to the specific obstacle in question
        /// produced nothing and looked like a broken tool. A long cooldown keeps the log clean
        /// while still letting a deliberate test produce a fresh reading.</summary>
        private const int CooldownTicks = 600;

        private int lastReportTick = -99999;

        public GameComponent_AvirFlightProbe(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            // Fix254: a probe is for looking, not for leaving on. It wrote fourteen lines every ten seconds whenever a
            // colonist with wings stood beside a wall - and the game stops logging altogether after a thousand
            // messages, so a quarter of an hour of play left the log deaf to every real error after it. It runs now
            // only when the log is asked to be wordy, in developer mode.
            if (!Prefs.DevMode || !Prefs.LogVerbose) return;
            if (Find.TickManager.TicksGame % CheckInterval != 0) return;
            if (Find.TickManager.TicksGame - lastReportTick < CooldownTicks) return;

            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                List<Pawn> pawns = map?.mapPawns?.FreeColonistsSpawned;
                if (pawns == null) continue;

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null || !pawn.Spawned) continue;
                    if (!AvirFlightUtility.CarriesFlightGene(pawn)) continue;

                    // Only worth reporting where the question actually arises: standing beside a
                    // cell the walking grid will not accept. Anywhere else every number would be
                    // ordinary and tell us nothing.
                    if (!HasBlockedNeighbour(map, pawn)) continue;

                    Report(map, pawn);
                    lastReportTick = Find.TickManager.TicksGame;
                    return;
                }
            }
        }

        private static bool HasBlockedNeighbour(Map map, Pawn pawn)
        {
            PathGrid grid = map.pathing?.Normal?.pathGrid;
            if (grid == null) return false;

            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 c = pawn.Position + GenAdj.AdjacentCells[i];
                if (c.InBounds(map) && !grid.Walkable(c)) return true;
            }

            return false;
        }

        /// <summary>
        /// ONE Log.Message PER LINE, NOT ONE MULTI-LINE MESSAGE.
        ///
        /// The original built a ten-line StringBuilder and logged it once. RimWorld's debug log
        /// shows only the first couple of lines of a multi-line entry, so the table - the entire
        /// point of the tool - was invisible. Maximus reported "I don't see the probe" twice while
        /// it had in fact fired twice and printed its header both times.
        ///
        /// A diagnostic that cannot be read is worse than no diagnostic, because its silence gets
        /// mistaken for evidence. Separate calls cost nothing and cannot be truncated.
        /// </summary>
        private static void Report(Map map, Pawn pawn)
        {
            PathingContext normal = map.pathing.Normal;
            PathingContext flying = map.pathing.Flying;

            bool usesFlying = flying != null && map.pathing.For(pawn) == flying;

            Log.Message("[Avir probe] ---- " + pawn.LabelShort + " at " + pawn.Position + " ----");
            Log.Message("[Avir probe] Flying context exists : " + (flying != null));
            Log.Message("[Avir probe] Pathing.For(pawn)     : " + (usesFlying ? "FLYING" : "normal/fence")
                + "   <-- THIS IS THE ANSWER WE NEED");
            Log.Message("[Avir probe] gene " + AvirFlightUtility.CarriesFlightGene(pawn)
                + " | flying " + AvirFlightUtility.IsFlying(pawn)
                + " | canTakeOff " + AvirFlightUtility.CanTakeOff(pawn)
                + " | rest " + (pawn.needs?.rest?.CurLevel.ToString("0.00") ?? "n/a"));
            Log.Message("[Avir probe] cell | walkCost | flyCost | roof | walkableBy");

            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 c = pawn.Position + GenAdj.AdjacentCells[i];
                if (!c.InBounds(map)) continue;

                int nCost = normal?.pathGrid?.CalculatedCostAt(c, false, IntVec3.Invalid, null) ?? -1;
                int fCost = flying?.pathGrid?.CalculatedCostAt(c, false, IntVec3.Invalid, null) ?? -1;

                RoofDef roof = map.roofGrid.RoofAt(c);
                string roofName = roof == null
                    ? "none"
                    : roof.defName + (roof.isNatural ? " nat" : " built");

                Log.Message("[Avir probe] " + c + " | " + nCost + " | " + fCost + " | "
                    + roofName + " | " + c.WalkableBy(map, pawn));
            }

            Log.Message("[Avir probe] ---- end ----");
        }
    }
}
