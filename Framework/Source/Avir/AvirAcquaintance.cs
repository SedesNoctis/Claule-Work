using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// HOW LONG TWO PAWNS HAVE KNOWN EACH OTHER.
    ///
    /// THE GAME DOES NOT TRACK THIS. RimWorld records opinions, relations and interaction history,
    /// but nothing that answers "since when". So the mod has to remember it, and that memory has to
    /// survive saving, because a rule that resets on every load is not a rule.
    ///
    /// WHAT COUNTS AS MEETING: being alive on the same map at the same time. Not conversation, not
    /// proximity, not a shared bed. That is deliberately generous, because the threshold is about
    /// TIME rather than intimacy - an Avir who has spent a month in the same colony as someone has
    /// had a month to decide about them, whether or not the game noticed them speaking.
    ///
    /// COST. The sweep runs every 2500 ticks and only over pawns carrying the bond gene, pairing
    /// each against the humanlikes on their map. In a colony with three Avir and twenty pawns that
    /// is sixty comparisons an hour, and the dictionary only ever grows by first meetings.
    ///
    /// FORGETTING. Entries for pawns that no longer exist are dropped during the sweep rather than
    /// on a timer, so a world that has churned through hundreds of visitors does not carry all of
    /// them forever in the save.
    /// </summary>
    public class AvirAcquaintance : GameComponent
    {
        private const int SweepInterval = 2500;

        private static AvirAcquaintance instance;

        /// <summary>
        /// First tick each pair was seen together, keyed by the two thingIDNumbers combined in a
        /// stable order so that (a,b) and (b,a) are the same entry.
        /// </summary>
        private Dictionary<long, int> firstSeen = new Dictionary<long, int>();

        private List<long> keysWorking = new List<long>();
        private List<int> valuesWorking = new List<int>();

        public AvirAcquaintance(Game game)
        {
            instance = this;
        }

        public override void FinalizeInit()
        {
            instance = this;
        }

        private static long KeyFor(Pawn a, Pawn b)
        {
            int lo = Math.Min(a.thingIDNumber, b.thingIDNumber);
            int hi = Math.Max(a.thingIDNumber, b.thingIDNumber);
            return ((long)lo << 32) | (uint)hi;
        }

        /// <summary>
        /// Ticks these two have known each other, or -1 if they have never been recorded together.
        ///
        /// -1 rather than 0 on purpose: "never met" and "met this instant" must not be the same
        /// answer, or a courtship could fire on the tick two pawns first laid eyes on one another.
        /// </summary>
        public static int TicksKnown(Pawn a, Pawn b)
        {
            if (instance == null || a == null || b == null) return -1;

            int first;
            if (!instance.firstSeen.TryGetValue(KeyFor(a, b), out first)) return -1;

            return Find.TickManager.TicksGame - first;
        }

        /// <summary>
        /// Records a meeting if this is the first. Public so that the romance patch can seed a pair
        /// that somehow reached it unrecorded, rather than treating an unknown pair as strangers
        /// forever.
        /// </summary>
        public static void NoteMet(Pawn a, Pawn b)
        {
            if (instance == null || a == null || b == null || a == b) return;

            long key = KeyFor(a, b);
            if (instance.firstSeen.ContainsKey(key)) return;

            instance.firstSeen[key] = Find.TickManager.TicksGame;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % SweepInterval != 0) return;

            try
            {
                List<Map> maps = Find.Maps;
                if (maps == null) return;

                for (int m = 0; m < maps.Count; m++)
                {
                    IReadOnlyList<Pawn> pawns = maps[m]?.mapPawns?.AllPawnsSpawned;
                    if (pawns == null) continue;

                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn avir = pawns[i];

                        // Only pawns the rule applies to. Everyone else on the map is only ever a
                        // partner in a pairing, never the reason to build one.
                        if (!AvirPairBond.HasBondGene(avir)) continue;

                        for (int j = 0; j < pawns.Count; j++)
                        {
                            Pawn other = pawns[j];
                            if (other == avir || other.Dead) continue;
                            if (other.RaceProps == null || !other.RaceProps.Humanlike) continue;

                            NoteMet(avir, other);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] acquaintance sweep failed: " + e, 0x4A5281);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                keysWorking = new List<long>(firstSeen.Keys);
                valuesWorking = new List<int>(firstSeen.Values);
            }

            Scribe_Collections.Look(ref keysWorking, "avirAcquaintanceKeys", LookMode.Value);
            Scribe_Collections.Look(ref valuesWorking, "avirAcquaintanceTicks", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                firstSeen = new Dictionary<long, int>();

                if (keysWorking != null && valuesWorking != null
                    && keysWorking.Count == valuesWorking.Count)
                {
                    for (int i = 0; i < keysWorking.Count; i++)
                    {
                        firstSeen[keysWorking[i]] = valuesWorking[i];
                    }
                }
            }
        }
    }
}
