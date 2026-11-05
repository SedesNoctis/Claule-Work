using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// WHO GETS THE FULL SIMULATION, AND WHO GETS IT SLOWED DOWN.
    ///
    /// THE PROBLEM THIS ANSWERS. Every Avir upkeep system in this mod - shedding, arcane hunger,
    /// grooming - walks every Avir in the world, whoever owns them. A thirty-head A'Shaign caravan
    /// that stands on the map for half a day and leaves was costing exactly as much per pawn as a
    /// colonist who will be here for years. Maximus watched TPS fall from 230 to 100 the moment a
    /// Legion caravan arrived, with a mod already slowing pawn ticks twentyfold.
    ///
    /// SLOWED, NOT DISABLED. Maximus's own call, and the right one: a trader who stays a week
    /// should still shed and still get hungry, just not be asked about it four hundred times. Every
    /// caller here keeps the same RATE and changes only the granularity - a transient pawn is
    /// sampled once for every SlowFactor samples a colonist gets, and is credited SlowFactor times
    /// as much when it is. Nothing accumulates differently; it accumulates in bigger steps.
    ///
    /// WHERE THE LINE IS DRAWN, AND WHY NOT ON "GUEST". Maximus asked how guests are told apart
    /// from visitors, since the words mean the same thing. They are separated here by CARE, not by
    /// label: a pawn is fully simulated if the colony is responsible for it - a colonist, a
    /// prisoner, a slave, or a pawn some guest mod has handed to the player as a host. Traders,
    /// raiders and passing visitors belong to somebody else and are slowed.
    ///
    /// That test is built only from checks this mod already uses elsewhere and therefore already
    /// compiles - Faction.IsPlayer, IsPrisonerOfColony, IsSlaveOfColony, guest.HostFaction. No
    /// vanilla name is being recalled from memory here.
    ///
    /// FLIGHT IS DELIBERATELY ABSENT. Maximus was explicit: Avir raiders should keep feeling
    /// unique, and flight is what makes them so. Nothing in the flight system consults this class,
    /// and nothing in the flight system should ever start.
    /// </summary>
    public static class AvirSimTier
    {
        /// <summary>
        /// How much less often a transient pawn is sampled. Ten was chosen as the first honest
        /// number rather than the largest one: at twenty a trader who stays three days would be
        /// asked about its coat roughly once, which starts to look like "disabled" wearing a
        /// different hat. Raise it if the caravans are still felt.
        /// </summary>
        public const int SlowFactor = 10;

        /// <summary>
        /// Is this pawn in the colony's care, rather than passing through it?
        ///
        /// A GUEST MOD'S GUESTS. Hospitality-style mods mark a visiting pawn's host faction as the
        /// player, which is the last clause. If the guest system Maximus ends up using does NOT do
        /// that, its guests will fall to the slow tier - the symptom would be a resident guest
        /// shedding in visible steps rather than smoothly. That is the one clause to widen if it
        /// happens, and it is one line.
        /// </summary>
        public static bool FullSimulation(Pawn pawn)
        {
            if (pawn == null) return false;

            // Colonists, and everything AvirFaction already counts as the player's - which
            // includes the Free Nesting faction, because that faction is only ever handed to the
            // player. Kept as a call rather than a copy so the two can never disagree.
            if (AvirFaction.IsPlayerAvir(pawn)) return true;

            if (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony) return true;

            if (pawn.guest != null && pawn.guest.HostFaction != null
                && pawn.guest.HostFaction.IsPlayer)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// SHOULD THIS MOD BOTHER WORKING OUT WHAT THESE TWO THINK OF EACH OTHER?
        ///
        /// THE SHAPE OF THE PROBLEM. Situational social thoughts are evaluated per ORDERED PAIR of
        /// pawns, so the cost is quadratic. Twenty traders is three hundred and eighty pairs
        /// between themselves alone, and this mod hangs six of its own social thoughts on every
        /// one of them. That is over two thousand extra questions per mood pass about what
        /// merchants who leave by evening think of each other.
        ///
        /// Maximus watched TPS halve the moment a twenty-head Legion caravan arrived. Halving on
        /// arrival is the signature of a quadratic term, and this is the only quadratic term the
        /// mod owns.
        ///
        /// WHAT IS AND IS NOT DROPPED. Only pairs where BOTH sides are passing through. Trader
        /// about trader, raider about raider. The moment either side is the colony's - a colonist,
        /// a guest, a prisoner, a slave - the pair is evaluated in full.
        ///
        /// This is what Maximus asked for and it costs him nothing he will ever see. He said the
        /// relationships mattered and should not be cut; they are not cut. What is dropped is
        /// three hundred and eighty opinions he was never going to read, between pawns who will
        /// not be here tomorrow.
        /// </summary>
        public static bool PairWorthEvaluating(Pawn a, Pawn b)
        {
            return FullSimulation(a) || FullSimulation(b);
        }
        ///
        /// Transients are staggered by thingIDNumber rather than all landing on the same pass, so
        /// a thirty-head caravan spreads its three-pawns-per-pass across the cycle instead of
        /// paying for all thirty at once and nothing for nine passes after. Same total work,
        /// no spike.
        /// </summary>
        public static bool ProcessOnThisPass(Pawn pawn, int intervalTicks)
        {
            if (FullSimulation(pawn)) return true;
            if (intervalTicks < 1) intervalTicks = 1;

            int pass = Find.TickManager.TicksGame / intervalTicks;
            int id = pawn.thingIDNumber & 0x7FFFFFFF;

            return ((pass + id) % SlowFactor) == 0;
        }

        /// <summary>
        /// The interval this pawn is actually being sampled at. Callers that credit an amount of
        /// elapsed time must use THIS rather than their own constant, or a slowed pawn would
        /// accrue at a tenth of the correct rate instead of in tenth-as-frequent steps.
        /// </summary>
        public static int ScaleInterval(Pawn pawn, int baseInterval)
        {
            if (baseInterval < 1) baseInterval = 1;
            return FullSimulation(pawn) ? baseInterval : baseInterval * SlowFactor;
        }
    }
}
