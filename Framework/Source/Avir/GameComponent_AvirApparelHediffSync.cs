using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Same role as GameComponent_AvirApparelAbilitySync, for hediff-granting apparel instead of
    /// ability-granting apparel. The instant Harmony hooks in Patch_AvirApparelGrantsHediff
    /// handle every normal equip/unequip; this only exists to catch dev-tool grants and old saves.
    /// </summary>
    public class GameComponent_AvirApparelHediffSync : GameComponent
    {
        private const int ResyncIntervalTicks = 60000; // 1 in-game day

        private int ticksUntilNextSync;

        public GameComponent_AvirApparelHediffSync(Game game)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            RescanAndSync();
            ticksUntilNextSync = ResyncIntervalTicks;
        }

        public override void GameComponentTick()
        {
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
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                IReadOnlyList<Pawn> mapPawns = maps[m].mapPawns.AllPawnsSpawned;
                for (int p = 0; p < mapPawns.Count; p++)
                {
                    Pawn pawn = mapPawns[p];
                    if (pawn?.apparel == null || pawn.health?.hediffSet == null)
                    {
                        continue;
                    }

                    List<Apparel> worn = pawn.apparel.WornApparel;
                    for (int w = 0; w < worn.Count; w++)
                    {
                        AvirApparelHediffSync.SyncOne(pawn, worn[w], granting: true);
                    }
                }
            }
        }
    }
}
