using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// MAKES A TORN PSYCHIC BOND PERMANENT FOR FEATHERBROKEN.
    ///
    /// Avir bond for life. So the trait should not fade when the memory does - vanilla's
    /// PsychicBondTorn is a MEMORY thought with a duration, and once it expires an Avir who lost
    /// their mate would quietly go back to normal, which is the opposite of the point.
    ///
    /// So: watch for the memory once, stamp a permanent hediff, and let the ThoughtDef key off the
    /// hediff from then on. The memory is the event; the hediff is the scar - which is the right
    /// word even though Avir do not scar, because this one is not on the body.
    ///
    /// WHY A SWEEP AND NOT A CHECK INSIDE THE THOUGHT WORKER. Thought workers are called from the
    /// needs tab and from tooltips as well as from the simulation - adding a hediff from inside one
    /// would mean mutating a pawn because the player happened to hover over something. A sweep runs
    /// where mutation is expected.
    ///
    /// Every 2500 ticks over player Avir only. The inner test is a memory count, so the cost is a
    /// short list walk per Avir per in-game hour.
    /// </summary>
    public class GameComponent_AvirFeatherbroken : GameComponent
    {
        private const int SweepIntervalTicks = 2500;

        private static TraitDef featherbrokenTrait;
        private static HediffDef markHediff;
        private static bool lookedUp;

        public GameComponent_AvirFeatherbroken(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (Find.TickManager.TicksGame % SweepIntervalTicks != 0) return;

            EnsureLookedUp();
            if (featherbrokenTrait == null || markHediff == null) return;

            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                List<Pawn> pawns = maps[m]?.mapPawns?.FreeColonistsSpawned;
                if (pawns == null) continue;

                for (int i = 0; i < pawns.Count; i++)
                {
                    TryStamp(pawns[i]);
                }
            }
        }

        private static void TryStamp(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return;
            if (pawn.story?.traits == null || !pawn.story.traits.HasTrait(featherbrokenTrait)) return;
            if (pawn.health?.hediffSet == null || pawn.health.hediffSet.HasHediff(markHediff)) return;

            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
            if (memories == null) return;
            if (memories.NumMemoriesOfDef(ThoughtDefOf.PsychicBondTorn) <= 0) return;

            pawn.health.AddHediff(HediffMaker.MakeHediff(markHediff, pawn));
        }

        private static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            featherbrokenTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Featherbroken");
            markHediff = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_FeatherbrokenMark");
        }
    }
}
