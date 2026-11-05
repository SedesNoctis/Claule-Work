using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// "Friends/relatives/lovers can carry the pawn to bed without the player supervising them
    /// - automatically." Inserted into the think tree (see Avir_CarryLovedOneToBed_ThinkTree.xml)
    /// so this can fire as a personal, autonomous act for spouses/lovers/fiances, parents/
    /// children/siblings, and close friends by opinion - regardless of whether the pawn even
    /// has Hauling enabled. The Hauling WorkGiver (WorkGiver_Avir_CarrySleeperToBed) is the
    /// separate, unrelated general-purpose path for everyone else.
    /// </summary>
    public class JobGiver_Avir_CarryLovedOneToBed : ThinkNode_JobGiver
    {
        // Vanilla priority-sorted JobGivers commonly sit in roughly a 0-9 range, with survival
        // needs at the very top. This is meant to land as "a caring act worth interrupting
        // ordinary work for" but below anything actually urgent - a guessed value, not one
        // taken from confirmed vanilla source, so it's the first thing to retune if this fires
        // too eagerly or not eagerly enough relative to other autonomous behaviors.
        private const float Priority = 5.8f;

        public override float GetPriority(Pawn pawn)
        {
            return Priority;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn?.Map == null || pawn.Drafted || !pawn.RaceProps.Humanlike)
            {
                return null;
            }

            foreach (Pawn other in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (!Avir_CarrySleeperUtil.IsEligibleSleeper(other, pawn))
                {
                    continue;
                }

                if (!Avir_CarrySleeperUtil.IsCloseEnoughForAutonomousCarry(pawn, other))
                {
                    continue;
                }

                Building_Bed bed = Avir_CarrySleeperUtil.FindBedFor(other);
                if (bed == null)
                {
                    continue;
                }

                return JobMaker.MakeJob(AvirDefOf.Avir_CarrySleeperToBed, other, bed);
            }

            return null;
        }
    }
}
