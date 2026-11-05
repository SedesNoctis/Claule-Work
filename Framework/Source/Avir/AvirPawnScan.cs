using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir
{
    /// <summary>
    /// Enumerates every living pawn the colony can reasonably be said to have - on any map, and in
    /// any caravan.
    ///
    /// Deliberately does NOT use PawnsFinder: its members keep getting renamed between RimWorld
    /// versions (AllMapsCaravansAndTravelingTransportPods_Alive, ...Transporters_Alive, and so on),
    /// and each rename is a hard compile error. Find.Maps and Find.WorldObjects.Caravans have been
    /// stable for years.
    /// </summary>
    public static class AvirPawnScan
    {
        // REUSED BUFFER. This used to allocate a brand new List of every pawn on every map, twice
        // an in-game hour, forever - garbage the collector then had to chase. The two callers are
        // now offset from each other (see AvirArcaneHunger) and neither holds the list past its own
        // loop, so a single shared buffer is safe and allocates nothing.
        private static readonly List<Pawn> buffer = new List<Pawn>(256);

        public static List<Pawn> AllLivePawns()
        {
            List<Pawn> result = buffer;
            result.Clear();

            List<Map> maps = Find.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    Map map = maps[i];
                    if (map == null || map.mapPawns == null) continue;

                    // 1.6 returns IReadOnlyList here, 1.5 returned List - IReadOnlyList accepts both.
                    IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
                    for (int j = 0; j < spawned.Count; j++)
                    {
                        Pawn p = spawned[j];
                        if (p != null && !p.Dead) result.Add(p);
                    }
                }
            }

            if (Find.WorldObjects != null)
            {
                List<Caravan> caravans = Find.WorldObjects.Caravans;
                if (caravans != null)
                {
                    for (int i = 0; i < caravans.Count; i++)
                    {
                        IReadOnlyList<Pawn> members = caravans[i].PawnsListForReading;
                        for (int j = 0; j < members.Count; j++)
                        {
                            Pawn p = members[j];
                            if (p != null && !p.Dead) result.Add(p);
                        }
                    }
                }
            }

            return result;
        }
    }
}
