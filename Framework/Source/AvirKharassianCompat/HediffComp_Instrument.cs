using Kharassian;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AvirKharassianCompat
{
    public class HediffCompProperties_Instrument : HediffCompProperties
    {
        /// <summary>How often the collar looks the slave over.</summary>
        public int checkIntervalTicks = 250;

        /// <summary>Once the collar has put a spent slave to sleep, it lets him lie until his
        /// stamina is this far above the player's marker - otherwise he would be jolted awake the
        /// instant he crossed it and sent back down a moment later.</summary>
        public float wakeMarginAboveMarker = 0.20f;

        public HediffCompProperties_Instrument()
        {
            compClass = typeof(HediffComp_Instrument);
        }
    }

    /// <summary>
    /// The collar's own work, done every few seconds on the slave who wears it:
    ///
    ///   - SUPPRESSION held full. There is nothing left in him to suppress.
    ///   - The collar LOCKED on him the first time it is worn, so neither he nor his outfit can
    ///     take it off. Only the player can: unlocking it in the gear tab is the direct order.
    ///   - Any part in a conspiracy dropped. An instrument does not conspire.
    ///   - SLEEP, by the stamina marker the player already sets on every slave on the block:
    ///       above the marker  the collar keeps him working - the same jolt an overlord's goad
    ///                         gives (rest clawed back, stamina paid, suppression up), on the
    ///                         same conditions and the same cooldown;
    ///       below the marker  the collar puts him to sleep itself, and lets him lie until his
    ///                         stamina is back above the marker by wakeMarginAboveMarker.
    ///
    /// The rest - no pain, no speech, the low break threshold - is the hediff's own stage.
    /// </summary>
    public class HediffComp_Instrument : HediffComp
    {
        private bool collarSleep;
        private int lockedCollarId = -1;

        public HediffCompProperties_Instrument Props => (HediffCompProperties_Instrument)props;

        public override bool CompShouldRemove
        {
            get
            {
                Pawn pawn = Pawn;
                return pawn == null || !pawn.IsSlave || InstrumentUtility.WornCollar(pawn) == null;
            }
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || !pawn.IsHashIntervalTick(Props.checkIntervalTicks, delta))
            {
                return;
            }

            HoldSuppression(pawn);
            LockCollarOnce(pawn);
            DropConspiracy(pawn);

            if (pawn.Spawned)
            {
                TickSleep(pawn);
            }
        }

        private static void HoldSuppression(Pawn pawn)
        {
            Need_Suppression suppression = pawn.needs?.TryGetNeed<Need_Suppression>();
            if (suppression != null)
            {
                suppression.CurLevel = suppression.MaxLevel;
            }
        }

        private void LockCollarOnce(Pawn pawn)
        {
            Apparel collar = InstrumentUtility.WornCollar(pawn);
            if (collar == null || collar.thingIDNumber == lockedCollarId || pawn.apparel == null)
            {
                return;
            }
            lockedCollarId = collar.thingIDNumber;
            if (!pawn.apparel.IsLocked(collar))
            {
                pawn.apparel.Lock(collar);
            }
        }

        private static void DropConspiracy(Pawn pawn)
        {
            RemoveIfPresent(pawn, KharassianSlaveryDefOf.Kharassian_ConspiracyLeader);
            RemoveIfPresent(pawn, KharassianSlaveryDefOf.Kharassian_Conspirator);
        }

        private static void RemoveIfPresent(Pawn pawn, HediffDef def)
        {
            if (def == null)
            {
                return;
            }
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (h != null)
            {
                pawn.health.RemoveHediff(h);
            }
        }

        private void TickSleep(Pawn pawn)
        {
            HediffComp_Exhaustion ex = OverseerWorkUtility.ExhaustionOf(pawn);
            if (ex == null || !ex.OnTheBlock)
            {
                collarSleep = false;
                return;
            }
            if (pawn.Downed || pawn.Drafted || pawn.InMentalState)
            {
                return;
            }

            float marker = ex.Threshold;

            // Spent: the collar lets him go, and sends him down if he has not gone already.
            if (ex.Stamina < marker)
            {
                collarSleep = true;
                if (pawn.Awake() && pawn.CurJobDef != JobDefOf.LayDown)
                {
                    SendToSleep(pawn);
                }
                return;
            }

            // Put down by the collar: left to lie until properly back above the marker.
            if (collarSleep && !pawn.Awake() && ex.Stamina < marker + Props.wakeMarginAboveMarker)
            {
                return;
            }
            collarSleep = false;

            // Above the marker: kept at it, exactly as an overlord would keep him.
            if (ex.ShouldPunishForSleep)
            {
                Jolt(pawn, ex);
            }
        }

        private static void SendToSleep(Pawn pawn)
        {
            Building_Bed bed = RestUtility.FindBedFor(pawn);
            Job job = bed != null
                ? JobMaker.MakeJob(JobDefOf.LayDown, bed)
                : JobMaker.MakeJob(JobDefOf.LayDown, pawn.Position);
            pawn.jobs?.StartJob(job, JobCondition.InterruptForced);
        }

        private static void Jolt(Pawn pawn, HediffComp_Exhaustion ex)
        {
            if (!pawn.Awake())
            {
                RestUtility.WakeUp(pawn);
            }
            ex.Notify_Punished();
            if (pawn.Map != null)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "AvirInstrumentJolt".Translate(), new Color(0.75f, 0.8f, 1f), 2f);
            }
        }

        public override string CompTipStringExtra
        {
            get
            {
                Pawn holder = Current.Game?.GetComponent<GameComponent_SlaveControl>()?.HolderOf(Pawn);
                if (holder == null)
                {
                    return "AvirInstrumentUnbound".Translate();
                }
                return "AvirInstrumentBoundTo".Translate(holder.LabelShortCap);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref collarSleep, "avirCollarSleep", false);
            Scribe_Values.Look(ref lockedCollarId, "avirLockedCollarId", -1);
        }
    }
}
