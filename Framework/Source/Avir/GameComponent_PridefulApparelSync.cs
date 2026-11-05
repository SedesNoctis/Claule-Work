using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Replaces the old Pawn.TickRare-based resync, which ran a cheap check against EVERY pawn
    /// on the map every 250 ticks. This instead keeps its own short list of Prideful pawns and
    /// only ever touches those, on a long interval - equip/unequip already updates instantly via
    /// the Harmony patches below, so this periodic pass exists purely to catch slow-developing
    /// state (a garment wearing down into "tattered") and to pick up pawns newly granted the
    /// gene/trait mid-save. With clothes managed by hand, there's nothing time-sensitive here.
    /// </summary>
    public class GameComponent_PridefulApparelSync : GameComponent
    {
        // 60000 ticks = 1 in-game day. Lower this if you want new gene/trait grants (dev mode,
        // xenogerm, Character Editor) or slow HP decay into "tattered" caught sooner - it has
        // zero effect on responsiveness to actually changing clothes, which is always instant.
        private const int ResyncIntervalTicks = 60000;

        private readonly List<Pawn> pridefulPawns = new List<Pawn>();
        private int ticksUntilNextSync;

        public GameComponent_PridefulApparelSync(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RescanAndSync();
            ticksUntilNextSync = ResyncIntervalTicks;
        }

        public override void GameComponentTick()
        {
            // Just an int decrement/compare on every tick - the actual scan only runs once
            // every ResyncIntervalTicks, and only ever walks pridefulPawns (6 pawns for you).
            ticksUntilNextSync--;
            if (ticksUntilNextSync > 0)
            {
                return;
            }
            ticksUntilNextSync = ResyncIntervalTicks;
            RescanAndSync();
        }

        private void RescanAndSync()
        {
            pridefulPawns.Clear();
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                IReadOnlyList<Pawn> mapPawns = maps[m].mapPawns.AllPawnsSpawned;
                for (int p = 0; p < mapPawns.Count; p++)
                {
                    Pawn pawn = mapPawns[p];
                    if (PridefulApparelUtility.IsPrideful(pawn))
                    {
                        pridefulPawns.Add(pawn);
                    }
                }
            }

            for (int i = 0; i < pridefulPawns.Count; i++)
            {
                Pawn pawn = pridefulPawns[i];
                if (pawn?.apparel == null)
                {
                    continue;
                }
                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int w = 0; w < worn.Count; w++)
                {
                    PridefulApparelSync.SyncOne(pawn, worn[w]);
                }
                PridefulApparelSync.SyncNudity(pawn);
            }
        }
    }
}
