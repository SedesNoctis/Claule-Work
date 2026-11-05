using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// THE MATE GETS HIS TURN, BECAUSE A SOCIAL FIGHT ONLY HOLDS TWO.
    ///
    /// THE PROBLEM, WHICH IS THE GAME'S AND NOT A BUG. MentalState_SocialFighting is strictly
    /// pairwise: each side stores one opponent and stops the moment that opponent is not fighting
    /// back. There is no way to put a third pawn into an existing fight. So when the Avir starts
    /// swinging, her husband cannot join - he is refused on the spot because the suitor is already
    /// in a mental state, and he walks past while his wife beats the man who propositioned her.
    /// Which is exactly what Maximus watched happen.
    ///
    /// THE ANSWER IS A QUEUE, NOT A SECOND FIGHT. The mate's retaliation is remembered and started
    /// the moment the suitor is free again. He gets his turn rather than being written out of it,
    /// and the game's own rule about pairwise fights is left intact.
    ///
    /// BOUNDED, because a grudge that waits forever is a trap. If the suitor has not become
    /// available within a day, or either of them leaves, dies or is downed, the entry is dropped.
    /// A husband hunting a man across three seasons for a pass made at a party is a different story
    /// than the one being told here.
    /// </summary>
    public class GameComponent_AvirBondRetaliation : GameComponent
    {
        /// <summary>Checked often enough to feel like a response rather than a coincidence.</summary>
        private const int CheckInterval = 120;

        /// <summary>One in-game day. After that the moment has passed.</summary>
        private const int ExpiryTicks = 60000;

        private class Grudge : IExposable
        {
            public Pawn avenger;
            public Pawn suitor;
            public int expiresAt;

            public void ExposeData()
            {
                Scribe_References.Look(ref avenger, "avenger");
                Scribe_References.Look(ref suitor, "suitor");
                Scribe_Values.Look(ref expiresAt, "expiresAt", 0);
            }
        }

        private static GameComponent_AvirBondRetaliation instance;
        private List<Grudge> pending = new List<Grudge>();

        public GameComponent_AvirBondRetaliation(Game game)
        {
            instance = this;
        }

        public override void FinalizeInit()
        {
            instance = this;
        }

        /// <summary>
        /// Remember that this pawn owes that one a hiding, to be collected when possible.
        /// </summary>
        public static void Queue(Pawn avenger, Pawn suitor)
        {
            if (instance == null || avenger == null || suitor == null || avenger == suitor) return;

            for (int i = 0; i < instance.pending.Count; i++)
            {
                Grudge g = instance.pending[i];
                if (g.avenger == avenger && g.suitor == suitor) return;
            }

            instance.pending.Add(new Grudge
            {
                avenger = avenger,
                suitor = suitor,
                expiresAt = Find.TickManager.TicksGame + ExpiryTicks,
            });
        }

        public override void GameComponentTick()
        {
            if (pending.Count == 0) return;
            if (Find.TickManager.TicksGame % CheckInterval != 0) return;

            try
            {
                int now = Find.TickManager.TicksGame;

                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    Grudge g = pending[i];

                    if (g.avenger == null || g.suitor == null || now > g.expiresAt)
                    {
                        pending.RemoveAt(i);
                        continue;
                    }

                    if (g.avenger.Dead || g.suitor.Dead
                        || !g.avenger.Spawned || !g.suitor.Spawned
                        || g.avenger.Map != g.suitor.Map
                        || g.avenger.Downed || g.suitor.Downed)
                    {
                        pending.RemoveAt(i);
                        continue;
                    }

                    // Still busy with the first one. Wait.
                    if (g.suitor.InMentalState || g.avenger.InMentalState) continue;

                    if (Patch_AvirPairBondRomance.StartMutualFight(g.avenger, g.suitor))
                    {
                        pending.RemoveAt(i);
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] retaliation queue failed: " + e, 0x4A5298);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "avirBondGrudges", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && pending == null)
            {
                pending = new List<Grudge>();
            }
        }
    }
}
