using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Avir_HA_CaptiveAudience's actual mechanic: while a pawn holding that trait is doing Art
    /// or Crafting work, everyone else nearby on the same map periodically picks up a small,
    /// short memory just for being close enough to notice. Stop working (or leave), and the
    /// memory simply stops renewing and decays out on its own - there's no separate "leaving"
    /// event to handle, durationDays does that for free.
    ///
    /// Runs on a short interval (300 ticks, ~5 seconds) rather than the Prideful sync's daily
    /// one, since this is meant to feel live - but it only ever does any real work at all in
    /// maps that currently contain a pawn with the trait who is also actually performing, which
    /// in practice is rare enough that the interval cost is negligible.
    /// </summary>
    public class GameComponent_AvirCaptiveAudience : GameComponent
    {
        private const int CheckIntervalTicks = 300;
        private const int RadiusTiles = 6;
        private const int RadiusSquared = RadiusTiles * RadiusTiles;

        private int ticksUntilNextCheck;
        private static TraitDef captiveAudienceTrait;
        private static WorkTypeDef artWork;
        private static WorkTypeDef craftingWork;
        private static ThoughtDef audienceThought;

        public GameComponent_AvirCaptiveAudience(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureCached();
            ticksUntilNextCheck = CheckIntervalTicks;
        }

        public override void GameComponentTick()
        {
            ticksUntilNextCheck--;
            if (ticksUntilNextCheck > 0) return;
            ticksUntilNextCheck = CheckIntervalTicks;

            EnsureCached();
            if (captiveAudienceTrait == null || audienceThought == null) return;

            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                RunForMap(maps[m]);
            }
        }

        private static void EnsureCached()
        {
            if (captiveAudienceTrait == null)
                captiveAudienceTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_HA_CaptiveAudience");

            if (artWork == null)
                artWork = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Art");

            if (craftingWork == null)
                craftingWork = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");

            if (audienceThought == null)
                audienceThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_HA_CaptiveAudienceMood");
        }

        private static void RunForMap(Map map)
        {
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;

            // Find every performer on this map first, THEN scan for an audience - two small
            // passes over the same list is still far cheaper than doing distance math against
            // every pawn on the off chance one of them happens to be performing.
            List<Pawn> performers = null;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (IsPerforming(p))
                {
                    if (performers == null) performers = new List<Pawn>();
                    performers.Add(p);
                }
            }

            if (performers == null) return;

            for (int i = 0; i < performers.Count; i++)
            {
                GrantAudienceMemory(performers[i], pawns);
            }
        }

        private static bool IsPerforming(Pawn pawn)
        {
            if (pawn?.story?.traits == null || pawn.Dead || pawn.Downed) return false;
            if (!pawn.story.traits.HasTrait(captiveAudienceTrait)) return false;

            WorkTypeDef workType = pawn.CurJob?.workGiverDef?.workType;
            return workType != null && (workType == artWork || workType == craftingWork);
        }

        private static void GrantAudienceMemory(Pawn performer, IReadOnlyList<Pawn> mapPawns)
        {
            for (int i = 0; i < mapPawns.Count; i++)
            {
                Pawn other = mapPawns[i];
                if (other == performer || other.Dead || other.Downed) continue;
                if (other.RaceProps?.Humanlike != true) continue;
                if (other.needs?.mood?.thoughts?.memories == null) continue;

                IntVec3 delta = other.Position - performer.Position;
                int distSquared = (delta.x * delta.x) + (delta.z * delta.z);
                if (distSquared > RadiusSquared) continue;

                other.needs.mood.thoughts.memories.TryGainMemory(audienceThought, performer);
            }
        }
    }
}
