using System.Collections.Generic;
using Avir;
using Kharassian;
using RimWorld;
using Verse;

namespace AvirKharassianCompat
{
    /// <summary>
    /// Who is bound to whose sphere, and the two things a binding does:
    ///
    ///   THE LASH, from anywhere on the map. Every second, each bound instrument whose holder
    ///   has an active sphere on the same map is "overseen" by that holder - the very call an
    ///   overlord's job makes when standing over his gang. So he gets the overlord's whole
    ///   effect: Under the Lash at the holder's skill, the gang bonus for the size of the
    ///   herd, the overseen stamina drain. Whatever the holder is doing meanwhile. What he does
    ///   NOT get is insults - nobody is there to shout - so no spite either.
    ///
    ///   FEEDING. An Avir holder whose arcane hunger is below the line set on its gizmo drinks
    ///   from one bound instrument every few seconds, through the sphere, without walking
    ///   anywhere. Each drink leaves the ordinary Drained Essence on the slave, and a drained
    ///   slave cannot be drunk from again until it passes.
    ///
    /// A binding breaks when the slave stops being an instrument (collar off, freed, dead) or the
    /// holder takes the sphere off or dies. Merely leaving the map does not break it - the lash
    /// just does not reach until they share a map again.
    /// </summary>
    public class GameComponent_SlaveControl : GameComponent
    {
        private const int LashInterval = 60;
        private const int FeedInterval = 250;
        private const int CleanupInterval = 250;

        private Dictionary<Pawn, Pawn> holderOf = new Dictionary<Pawn, Pawn>();

        private List<Pawn> tmpSlaves;
        private List<Pawn> tmpHolders;

        public GameComponent_SlaveControl(Game game)
        {
        }

        public Pawn HolderOf(Pawn slave)
        {
            return slave != null && holderOf.TryGetValue(slave, out Pawn h) ? h : null;
        }

        public List<Pawn> BoundTo(Pawn holder)
        {
            List<Pawn> result = new List<Pawn>();
            foreach (KeyValuePair<Pawn, Pawn> kv in holderOf)
            {
                if (kv.Value == holder)
                {
                    result.Add(kv.Key);
                }
            }
            return result;
        }

        public bool CanBind(Pawn holder, Pawn slave, out string why)
        {
            why = null;
            if (!InstrumentUtility.CanGripSphere(holder))
            {
                why = "AvirSphereNoGrip".Translate(holder.LabelShortCap);
                return false;
            }
            if (!InstrumentUtility.IsInstrument(slave))
            {
                why = "AvirSphereNotInstrument".Translate(slave.LabelShortCap);
                return false;
            }
            if (HolderOf(slave) != null)
            {
                why = "AvirSphereAlreadyBound".Translate(slave.LabelShortCap, HolderOf(slave).LabelShortCap);
                return false;
            }
            if (BoundTo(holder).Count >= InstrumentUtility.MaxBoundPerSphere)
            {
                why = "AvirSphereFull".Translate(InstrumentUtility.MaxBoundPerSphere);
                return false;
            }
            return true;
        }

        public void Bind(Pawn holder, Pawn slave)
        {
            if (CanBind(holder, slave, out string _))
            {
                holderOf[slave] = holder;
            }
        }

        public void Unbind(Pawn slave)
        {
            if (slave != null)
            {
                holderOf.Remove(slave);
            }
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;

            if (now % CleanupInterval == 3)
            {
                CollarSync();
            }
            if (holderOf.Count == 0)
            {
                return;
            }

            if (now % CleanupInterval == 0)
            {
                Cleanup();
            }
            if (now % LashInterval == 0)
            {
                Lash();
            }
            if (now % FeedInterval == 7)
            {
                Feed();
            }
        }

        /// <summary>
        /// Puts the Instrumented hediff on every colony slave wearing a collar who lacks it. The
        /// hediff removes itself when the collar comes off or the slave is freed, so only this
        /// direction needs doing.
        /// </summary>
        private static void CollarSync()
        {
            HediffDef def = InstrumentDefOf.Avir_Instrumented;
            if (def == null)
            {
                return;
            }
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                List<Pawn> slaves = maps[m].mapPawns.SlavesOfColonySpawned;
                for (int i = 0; i < slaves.Count; i++)
                {
                    Pawn slave = slaves[i];
                    if (!slave.health.hediffSet.HasHediff(def) && InstrumentUtility.WornCollar(slave) != null)
                    {
                        slave.health.AddHediff(def);
                    }
                }
            }
        }

        private void Cleanup()
        {
            List<Pawn> dead = null;
            foreach (KeyValuePair<Pawn, Pawn> kv in holderOf)
            {
                Pawn slave = kv.Key;
                Pawn holder = kv.Value;
                bool slaveGone = slave == null || slave.Destroyed || slave.Dead || !InstrumentUtility.IsInstrument(slave);
                bool holderGone = holder == null || holder.Destroyed || holder.Dead
                                  || InstrumentUtility.WornSphere(holder) == null;
                if (slaveGone || holderGone)
                {
                    (dead ?? (dead = new List<Pawn>())).Add(slave);
                }
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                {
                    holderOf.Remove(dead[i]);
                }
            }
        }

        private void Lash()
        {
            Dictionary<Pawn, int> gangSize = new Dictionary<Pawn, int>();
            foreach (KeyValuePair<Pawn, Pawn> kv in holderOf)
            {
                if (Reaches(kv.Value, kv.Key))
                {
                    gangSize.TryGetValue(kv.Value, out int n);
                    gangSize[kv.Value] = n + 1;
                }
            }

            foreach (KeyValuePair<Pawn, Pawn> kv in holderOf)
            {
                if (!gangSize.TryGetValue(kv.Value, out int n) || !Reaches(kv.Value, kv.Key))
                {
                    continue;
                }
                HediffComp_Exhaustion ex = OverseerWorkUtility.ExhaustionOf(kv.Key);
                if (ex != null && ex.OnTheBlock)
                {
                    ex.Notify_Overseen(kv.Value, n);
                }
            }
        }

        /// <summary>Same map, and the holder actually able to use the sphere.</summary>
        private static bool Reaches(Pawn holder, Pawn slave)
        {
            return slave != null && slave.Spawned && !slave.Dead
                   && InstrumentUtility.SphereActive(holder)
                   && holder.Map == slave.Map;
        }

        private void Feed()
        {
            HashSet<Pawn> fed = null;
            foreach (KeyValuePair<Pawn, Pawn> kv in holderOf)
            {
                Pawn holder = kv.Value;
                if (fed != null && fed.Contains(holder))
                {
                    continue;
                }
                Gene_ArcaneHunger hunger = ArcaneHungerCache.For(holder);
                if (hunger == null || hunger.Value >= hunger.FeedBelow)
                {
                    continue;
                }
                if (!Reaches(holder, kv.Key) || !AvirDrain.CanDrain(kv.Key))
                {
                    continue;
                }
                AvirDrain.Drain(holder, kv.Key);
                (fed ?? (fed = new HashSet<Pawn>())).Add(holder);
            }
        }

        /// <summary>The manual version, from the sphere's gizmo: drink from the first bound
        /// instrument that has anything to give.</summary>
        public bool FeedNow(Pawn holder)
        {
            List<Pawn> bound = BoundTo(holder);
            for (int i = 0; i < bound.Count; i++)
            {
                if (Reaches(holder, bound[i]) && AvirDrain.CanDrain(bound[i]))
                {
                    AvirDrain.Drain(holder, bound[i]);
                    return true;
                }
            }
            return false;
        }

        public bool AnyFeedable(Pawn holder)
        {
            List<Pawn> bound = BoundTo(holder);
            for (int i = 0; i < bound.Count; i++)
            {
                if (Reaches(holder, bound[i]) && AvirDrain.CanDrain(bound[i]))
                {
                    return true;
                }
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                holderOf.RemoveAll(kv => kv.Key == null || kv.Value == null);
            }
            Scribe_Collections.Look(ref holderOf, "avirSphereBindings", LookMode.Reference, LookMode.Reference,
                ref tmpSlaves, ref tmpHolders);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (holderOf == null)
                {
                    holderOf = new Dictionary<Pawn, Pawn>();
                }
                holderOf.RemoveAll(kv => kv.Key == null || kv.Value == null);
            }
        }
    }
}
