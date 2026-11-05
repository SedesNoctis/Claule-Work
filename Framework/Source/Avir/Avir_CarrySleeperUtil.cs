using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    public static class Avir_CarrySleeperUtil
    {
        // "Close friend" tier for the autonomous (no-supervision) carry - vanilla doesn't
        // have one single canonical opinion threshold for this, so this is a judgment call.
        // Tune here if it fires too often/rarely.
        public const int CloseFriendOpinionThreshold = 40;

        /// <summary>
        /// True if this pawn is a valid target: asleep, off the floor of a proper bed
        /// (collapsed from exhaustion, in a chair, wherever - anywhere that isn't a real bed),
        /// not already downed (Rescue already handles that case), not already being carried
        /// or reserved by somebody else.
        /// </summary>
        public static bool IsEligibleSleeper(Pawn sleeper, Pawn potentialCarrier)
        {
            if (sleeper == null || potentialCarrier == null)
            {
                return false;
            }

            if (sleeper == potentialCarrier)
            {
                return false;
            }

            if (!sleeper.Spawned || sleeper.Dead || sleeper.Downed)
            {
                return false;
            }

            if (!sleeper.RaceProps.Humanlike)
            {
                return false;
            }

            if (sleeper.Awake())
            {
                return false;
            }

            // Already properly in a real bed - nothing to do here.
            if (sleeper.CurrentBed() != null)
            {
                return false;
            }

            // Must actually be resting, not e.g. unconscious from injury/surgery (that's a
            // medical matter, not a "fell asleep tired" one).
            if (sleeper.CurJob == null || sleeper.CurJob.def != JobDefOf.LayDown)
            {
                return false;
            }

            if (sleeper.Faction != potentialCarrier.Faction)
            {
                return false;
            }

            if (sleeper.IsPrisoner || sleeper.IsSlave)
            {
                return false;
            }

            if (!potentialCarrier.CanReserveAndReach(sleeper, PathEndMode.Touch, Danger.Some))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// True if potentialCarrier counts as "close enough" to do this without the player
        /// having to queue it: spouse/lover/fiance, parent/child/sibling, or a close friend by
        /// opinion.
        /// </summary>
        public static bool IsCloseEnoughForAutonomousCarry(Pawn potentialCarrier, Pawn sleeper)
        {
            if (potentialCarrier?.relations == null || sleeper == null)
            {
                return false;
            }

            if (potentialCarrier.relations.DirectRelationExists(PawnRelationDefOf.Spouse, sleeper)
                || potentialCarrier.relations.DirectRelationExists(PawnRelationDefOf.Lover, sleeper)
                || potentialCarrier.relations.DirectRelationExists(PawnRelationDefOf.Fiance, sleeper)
                || potentialCarrier.relations.DirectRelationExists(PawnRelationDefOf.Parent, sleeper)
                || potentialCarrier.relations.DirectRelationExists(PawnRelationDefOf.Child, sleeper)
                || potentialCarrier.relations.DirectRelationExists(PawnRelationDefOf.Sibling, sleeper))
            {
                return true;
            }

            return potentialCarrier.relations.OpinionOf(sleeper) >= CloseFriendOpinionThreshold;
        }

        public static Building_Bed FindBedFor(Pawn sleeper)
        {
            return RestUtility.FindBedFor(sleeper);
        }
    }
}
