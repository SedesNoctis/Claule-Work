using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// EVERY AVIR ON THE MAP GROOMS, WHOEVER OWNS THEM.
    ///
    /// WHY THE THINK TREE IS NOT ENOUGH. Grooming reaches a pawn through three doors, all of them
    /// in the humanlike think tree: scheduled recreation, idle time, and the urgent node above
    /// ordinary work. A guest, a hired servant, a temporary colonist or a visiting trader is driven
    /// by a Lord duty that is consulted BEFORE any of those branches, so none of the three is ever
    /// reached and the pawn sits at the unkempt penalty indefinitely.
    ///
    /// That was invisible while only colonists were being watched. Maximus intends to play with a
    /// standing cast of Avir guests - pawns who live in the colony without belonging to it - and for
    /// them the mood penalty would be permanent and unanswerable, souring their opinion of a colony
    /// that had done nothing wrong.
    ///
    /// SO THE SWEEP ASKS DIRECTLY. Once an in-game hour, any Avir whose coat has reached the point
    /// where it is costing mood gets the job issued to it, bypassing the tree entirely. This is the
    /// same shape the flight rescue and the automatic landing already use in this mod, and for the
    /// same reason: a GameComponent cannot be pre-empted by a duty.
    ///
    /// DELIBERATELY LATE. It waits for the FIRST unkempt stage rather than the grooming threshold,
    /// so a colonist who would have got there on their own through the think tree does so, and this
    /// only picks up whoever the tree could not reach. Interrupting a pawn is a heavier act than
    /// offering them a job, and it should be the second answer rather than the first.
    /// </summary>
    public class GameComponent_AvirGroomingSweep : GameComponent
    {
        /// <summary>
        /// THIRTY-SIX IN-GAME HOURS. Maximus's figure, and he is right that hourly was wrong.
        ///
        /// I had judged this by the cost of a single pass, which is small. The number that matters
        /// is passes per unit of work done: grooming is a THREE-DAY cadence, so an hourly sweep was
        /// asking the same question about the same pawns roughly seventy times for every one time
        /// the answer could possibly have changed to yes.
        ///
        /// THE COST OF THE LONGER INTERVAL, STATED. The unkempt stages sit about 0.9 days apart, so
        /// a pawn can now slip from -3 to -6 before the sweep reaches it. That is acceptable: this
        /// is the safety net for pawns the think tree cannot reach, not the primary route, and a
        /// guest carrying -6 for a few hours is a far smaller problem than one carrying it forever.
        /// </summary>
        private const int CheckInterval = 90000;

        public GameComponent_AvirGroomingSweep(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckInterval != 0) return;

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
                        TryGroom(pawns[i]);
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir grooming] sweep failed: " + e, 0x4A5280);
            }
        }

        private static void TryGroom(Pawn pawn)
        {
            // Cheapest test first: this walks every pawn on every map, animals included, and
            // almost all of them leave on this line.
            if (pawn == null || !AvirFaction.HasFearborn(pawn)) return;

            // TRADERS AND RAIDERS ARE ASKED A TENTH AS OFTEN. Deliberately placed second, after
            // the Fearborn test but before everything else: the checks below this line are the
            // expensive ones, and a passing caravan has no business paying for them on every
            // sweep. Guests, prisoners and slaves are NOT slowed - the whole reason this sweep
            // exists is that the think tree cannot reach them, and that has not changed.
            if (!AvirSimTier.ProcessOnThisPass(pawn, CheckInterval)) return;

            // ORDERED BY COST, NOT BY IMPORTANCE.
            //
            // CoatOf walks the pawn's whole hediff list, which is the most expensive question here
            // by a wide margin - and it used to sit in the middle, behind several field reads that
            // reject far fewer pawns. Every cheap rejection now happens first, so the hediff walk
            // only runs for an Avir who is genuinely free to act.
            if (pawn.Drafted || pawn.InAggroMentalState) return;
            if (!pawn.Awake()) return;
            if (pawn.ParentHolder is Pawn_CarryTracker) return;

            JobDef current = pawn.CurJobDef;
            if (current == AvirGroomingDefOf.Avir_GroomSelfJob
                || current == AvirGroomingDefOf.Avir_GroomOtherJob
                || current == AvirGroomingDefOf.Avir_BeingGroomedJob)
            {
                return;
            }

            // Basics, exactly as the think-tree node asks them. A starving or exhausted Avir has
            // worse problems than loose fur.
            if (pawn.needs != null)
            {
                if (pawn.needs.rest != null && pawn.needs.rest.CurLevel < 0.28f) return;
                if (pawn.needs.food != null && pawn.needs.food.CurLevel < 0.28f) return;
            }

            if (!AvirGrooming.CanGroom(pawn)) return;

            HediffComp_FurShed coat = AvirGrooming.CoatOf(pawn);
            if (coat == null || coat.LooseFraction < AvirGroomingTraits.UnkemptStage0) return;

            Job job = AvirGrooming.TryMakeGroomingJob(pawn);
            if (job == null) return;

            if (pawn.jobs == null) return;

            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            Announce(pawn);
        }

        private static bool announced;

        private static void Announce(Pawn pawn)
        {
            if (announced) return;
            announced = true;

            Log.Message("[Avir grooming] " + pawn.LabelShortCap + " was past the unkempt threshold "
                + "and had not been reached by the think tree - issued directly. Guests, servants "
                + "and temporary colonists are covered by this.");
        }
    }
}
