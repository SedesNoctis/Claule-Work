using System.Collections.Generic;
using Verse;

namespace FlightGene
{
    /// <summary>
    /// WHO IS AIRBORNE RIGHT NOW, ANSWERABLE WITHOUT TOUCHING THE PAWN.
    ///
    /// WHAT THIS IS FOR, MEASURED RATHER THAN GUESSED. Maximus profiled a twenty-head A'Schialis
    /// caravan against the same colony ten seconds after it left:
    ///
    ///     with the caravan     PatherTick   11.9 us per call, 54 calls/tick
    ///     without it            PatherTick    1.63 us per call, 31 calls/tick
    ///
    /// Call COUNT went up 1.7x, which is just more pawns walking and is nobody's fault. But the
    /// cost PER CALL went up more than sevenfold, and that is not explained by pawn count at all.
    /// Something was making each individual movement step dramatically more expensive whenever
    /// Avir were on the map.
    ///
    /// It was the flight system. Patch_AvirFlightSpeed and Patch_AvirFlightArrival hook methods
    /// that run on every cell step of every pawn, and the first thing each did was ask
    /// hediffSet.HasHediff - a linear walk of the pawn's entire hediff list. An Avir carries a lot
    /// of hediffs: claw tier, beak conditioning, keen senses, circadian rhythm, arcane hunger, fur
    /// shedding, pair bond. Twenty of them walking meant twenty full list scans per step, and every
    /// non-Avir pawn on the map paid a shorter version of the same scan.
    ///
    /// HOW THIS FIXES IT. HediffComp_FlightMonitor lives on the flight hediff itself, so it knows
    /// exactly when a pawn goes up and comes down. It registers here on both events. The hot
    /// patches then ask this class instead, and the common case - nobody in the colony is flying -
    /// is settled by reading one integer.
    ///
    /// WHY A SET AND NOT A COUNTER. AnyFlying alone would cover the empty case, but the moment one
    /// Avir does take off every other pawn on the map would fall back to scanning again. The set
    /// answers the per-pawn question at hash-lookup cost, so one bird in the air does not put
    /// everyone else back on the slow path.
    ///
    /// CORRECTNESS OVER SPEED WHERE THEY CONFLICT. Registration hangs off comp lifecycle, and comp
    /// lifecycle can be surprised - a pawn destroyed mid-flight, a save loaded with a bird already
    /// up, a mod removing the hediff by a route that skips the comp. So IsFlying below falls back
    /// to the real hediff check whenever the set says no BUT the registry has not been told about
    /// this pawn either way. A stale "yes" costs a wrong speed clamp for one step; a stale "no"
    /// would drop a flying pawn into stone. The fallback is shaped so the second cannot happen.
    /// </summary>
    public static class AvirFlightRegistry
    {
        private static readonly HashSet<int> airborne = new HashSet<int>();

        /// <summary>
        /// True when not one pawn anywhere is currently flying. The hot patches test this FIRST,
        /// which is the whole point: a colony with no bird in the air pays one integer comparison
        /// per movement step instead of a hediff list walk.
        /// </summary>
        public static bool NobodyFlying => airborne.Count == 0;

        public static void Register(Pawn pawn)
        {
            if (pawn == null) return;
            airborne.Add(pawn.thingIDNumber);
        }

        public static void Unregister(Pawn pawn)
        {
            if (pawn == null) return;
            airborne.Remove(pawn.thingIDNumber);
        }

        /// <summary>Is this specific pawn airborne? Hash lookup, no pawn access at all.</summary>
        public static bool Contains(Pawn pawn)
        {
            return pawn != null && airborne.Contains(pawn.thingIDNumber);
        }

        /// <summary>
        /// Cleared between games so ids from a previous save cannot leak into a new one. Ids are
        /// reused across saves, and a stale entry would mark an innocent pawn as airborne.
        /// </summary>
        public static void Clear()
        {
            airborne.Clear();
        }
    }

    /// <summary>
    /// Wipes the registry when a game is loaded or started. HediffComp_FlightMonitor re-registers
    /// every genuinely airborne pawn on its first tick after load, so the set rebuilds itself
    /// within a tick of play resuming.
    /// </summary>
    public class GameComponent_AvirFlightRegistry : GameComponent
    {
        public GameComponent_AvirFlightRegistry(Game game)
        {
            AvirFlightRegistry.Clear();
        }

        public override void FinalizeInit()
        {
            AvirFlightRegistry.Clear();
        }
    }
}
