using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Same role as GameComponent_PridefulApparelSync: the instant Harmony hooks in
    /// Patch_AvirApparelGrantsAbility handle every normal equip/unequip already, so this exists
    /// purely to catch cases those hooks can't see - a pawn who had the amulet added directly via
    /// dev tools/Character Editor (which may not always route through Notify_ApparelAdded), or
    /// one loaded from a save made before this system existed.
    /// </summary>
    public class GameComponent_AvirApparelAbilitySync : GameComponent
    {
        // 60000 ticks = 1 in-game day, same interval the Prideful sync uses. Equip/unequip is
        // always instant regardless of this number - it only affects how quickly a missed grant
        // gets caught.
        private const int ResyncIntervalTicks = 60000;

        private int ticksUntilNextSync;

        public GameComponent_AvirApparelAbilitySync(Game game)
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
                    if (pawn?.apparel == null || pawn.abilities == null)
                    {
                        continue;
                    }

                    List<Apparel> worn = pawn.apparel.WornApparel;
                    for (int w = 0; w < worn.Count; w++)
                    {
                        AvirApparelAbilitySync.SyncOne(pawn, worn[w], granting: true);
                    }
                }
            }
        }
    }
}
