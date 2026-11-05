using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Shared tests for the bond's social and sleeping effects.
    ///
    /// FAMILY IS EXEMPT FROM ALL OF IT, and that exemption is the reason this class exists rather
    /// than the checks being written four times. A mate's opinion should not be able to turn an
    /// Avir against her own mother, her chick, or her sibling - those relationships are older than
    /// the bond and are not the bond's business. Getting that wrong in one of four places would
    /// produce a colony where nobody could work out why a family had turned on itself.
    /// </summary>
    public static class AvirBondSocial
    {
        /// <summary>
        /// Blood, in any direction and at any remove the game recognises.
        ///
        /// Uses vanilla's own family relation set rather than a hand-written list of defNames, so
        /// cousins, grandparents and half-siblings are covered without this having to enumerate
        /// them - and so a relation added by another mod that declares itself familial is too.
        /// </summary>
        public static bool IsFamily(Pawn a, Pawn b)
        {
            if (a?.relations == null || b == null) return false;

            List<DirectPawnRelation> direct = a.relations.DirectRelations;
            if (direct != null)
            {
                for (int i = 0; i < direct.Count; i++)
                {
                    DirectPawnRelation rel = direct[i];
                    if (rel?.def == null || rel.otherPawn != b) continue;
                    if (rel.def.familyByBloodRelation) return true;
                }
            }

            // Indirect blood - cousins, grandparents, aunts - are not in DirectRelations at all.
            PawnRelationDef closest = a.GetMostImportantRelation(b);
            return closest != null && closest.familyByBloodRelation;
        }

        /// <summary>The bonded mate, or null. Splinter-hearted pawns have none by definition.</summary>
        public static Pawn MateOf(Pawn pawn)
        {
            if (!AvirPairBond.HasBondGene(pawn)) return null;
            return LovePartnerRelationUtility.ExistingMostLikedLovePartner(pawn, false);
        }
    }

    /// <summary>
    /// WHAT AN AVIR THINKS OF THE ONE SHE CHOSE.
    ///
    /// +50, permanently, on top of the +30 the Spouse relation already carries in its own def -
    /// so eighty in total, each way. Worth stating plainly because it is a very large number: it
    /// puts the mate near the ceiling of what one pawn can think of another, which is the point.
    /// Nothing short of that describes a creature that chose once.
    ///
    /// SITUATIONAL, NOT A MEMORY. A memory would decay, be capped by the memory system, and be
    /// cleared by the same things that clear everything else. This is not something she felt once;
    /// it is the state of being bonded, and it should last exactly as long as that does.
    ///
    /// ONE-SIDED WHEN THE MATE IS NOT AN AVIR. She holds him at eighty; he holds her at whatever a
    /// human holds a spouse at. That asymmetry is the whole shape of a cross-species bond in this
    /// mod and it should be visible in the social tab.
    /// </summary>
    public class ThoughtWorker_Social_AvirBondMate : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            // Both sides passing through: nobody will ever read this opinion.
            // See AvirSimTier.PairWorthEvaluating - this is the quadratic term.
            if (!AvirSimTier.PairWorthEvaluating(p, other)) return ThoughtState.Inactive;

            if (p == null || other == null || p == other) return ThoughtState.Inactive;

            Pawn mate = AvirBondSocial.MateOf(p);
            if (mate != other) return ThoughtState.Inactive;

            return ThoughtState.ActiveAtStage(0);
        }
    }

    /// <summary>
    /// WHAT MY MATE THINKS OF YOU, I THINK A LITTLE OF YOU TOO.
    ///
    /// Small on purpose - five points either way. This is not the Avir adopting their mate's
    /// judgement, it is the unavoidable seepage of living inside someone else's head: you hear
    /// about the person who annoys them often enough that you start to find them annoying.
    ///
    /// Family excluded. A mate's dislike of your mother does not become your dislike of your
    /// mother.
    /// </summary>
    public class ThoughtWorker_Social_AvirBondEcho : ThoughtWorker
    {
        /// <summary>
        /// THE THRESHOLD TO SWITCH ON. Deliberately far from vanilla's own +/-20.
        ///
        /// THE OSCILLATION MAXIMUS CAUGHT. Arlinarie and Sha'Alleign kept flipping between "my mate
        /// cannot stand them" and "my mate likes them" about the same illithid, while both of them
        /// plainly liked him. Neither pawn was changing its mind. The pair was resonating.
        ///
        /// This thought is PART OF the opinion it reads. Sha'Alleign's echo asks what Arlinarie
        /// thinks of Omeluum - and Arlinarie's opinion of Omeluum already contains HER echo, which
        /// asks what Sha'Alleign thinks of Omeluum, which contains his echo. Two mirrors facing
        /// each other.
        ///
        /// With the old bare +/-20 test that loop had no damping at all. An underlying opinion
        /// sitting anywhere near 20 would cross it, hand its mate +5, push the mate across, take
        /// +5 back, and then unwind the same way on the next recalculation - forever, visibly, in
        /// the inspect window.
        ///
        /// THE FIX IS ARITHMETIC, NOT A GUESS. The echo contributes at most 5 points. So the band
        /// between switching on (40) and switching off (20) is 20 points wide - four times the
        /// largest push the feedback can ever produce. The loop cannot walk a pair across a gap
        /// four times bigger than its own step, so it settles instead of ringing.
        ///
        /// Raising the entry threshold alone would not have been enough; without the wider exit
        /// the pair would simply have resonated around 40 instead of around 20.
        /// </summary>
        private const int EnterMagnitude = 40;

        /// <summary>
        /// THE THRESHOLD TO SWITCH OFF, once it is already on. See EnterMagnitude for why these
        /// two numbers are different - a single threshold is what allowed the ringing.
        /// </summary>
        private const int ExitMagnitude = 20;

        /// <summary>
        /// Which pairs currently have this thought, so the exit threshold can be applied to them
        /// rather than the entry one.
        ///
        /// Keyed by both pawns because the thought is directional: what Sha'Alleign echoes about
        /// Omeluum is a different question from what Arlinarie echoes about him, and they must be
        /// allowed to settle independently. Only ACTIVE pairs are stored, so this stays as small
        /// as the number of opinions actually in effect.
        /// </summary>
        private static readonly Dictionary<long, int> activeStageByPair = new Dictionary<long, int>();

        private static long PairKey(Pawn a, Pawn b)
        {
            return ((long)a.thingIDNumber << 32) ^ (uint)b.thingIDNumber;
        }

        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            // Both sides passing through: nobody will ever read this opinion.
            // See AvirSimTier.PairWorthEvaluating - this is the quadratic term.
            if (!AvirSimTier.PairWorthEvaluating(p, other)) return ThoughtState.Inactive;

            if (p == null || other == null || p == other) return ThoughtState.Inactive;

            Pawn mate = AvirBondSocial.MateOf(p);
            if (mate == null || mate == other) return ThoughtState.Inactive;

            // Never let the bond reach into blood. Checked against BOTH of them: the target being
            // the Avir's own family is the case that matters, and the mate's family is the case
            // where the mate has no business having an opinion transmitted either.
            if (AvirBondSocial.IsFamily(p, other)) return ThoughtState.Inactive;

            int mateOpinion = mate.relations?.OpinionOf(other) ?? 0;

            long key = PairKey(p, other);
            bool wasActive = activeStageByPair.TryGetValue(key, out int previousStage);

            // Already on: hold it until the opinion falls well back past the lower mark. This is
            // the half that actually stops the ringing - see EnterMagnitude.
            int threshold = wasActive ? ExitMagnitude : EnterMagnitude;

            if (mateOpinion <= -threshold)
            {
                activeStageByPair[key] = 0;
                return ThoughtState.ActiveAtStage(0);
            }

            if (mateOpinion >= threshold)
            {
                activeStageByPair[key] = 1;
                return ThoughtState.ActiveAtStage(1);
            }

            // FLIPPING SIDES REQUIRES CLEARING THE MIDDLE FIRST. A pair that was "likes" cannot
            // become "cannot stand" without passing through nothing at all, which is both what a
            // real change of heart looks like and one more thing the feedback cannot fake.
            if (wasActive) activeStageByPair.Remove(key);

            return ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// SHARING A BED WITH THE ONE THEY CHOSE.
    ///
    /// In the book the whole clutch sleeps folded into one another - the chicks held by the mother,
    /// the mother held by the father, all of them hanging from a branch under his wings. RimWorld
    /// cannot stack bodies, so the arrangement is expressed in what it is worth to them instead:
    /// the innermost layer is the bed, the next is the room, and the absence of the chicks is felt
    /// as a small wrongness rather than a punishment.
    /// </summary>
    public class ThoughtWorker_AvirBondSharedBed : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Pawn mate = AvirBondSocial.MateOf(p);
            if (mate == null) return ThoughtState.Inactive;

            Building_Bed bed = p.ownership?.OwnedBed;
            if (bed == null) return ThoughtState.Inactive;

            return mate.ownership?.OwnedBed == bed
                ? ThoughtState.ActiveAtStage(0)
                : ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// THE CHICKS IN THE SAME ROOM, OR THE QUIET WHERE THEY SHOULD BE.
    ///
    /// Only their own chicks, and only while the chicks are still growing. Once a youngling has
    /// finished growing up it is not part of the pile any more - it is an adult who happens to
    /// sleep nearby, and there is nothing left to feel about it either way.
    ///
    /// The absence is deliberately a much smaller number than the presence. An Avir with no chicks
    /// yet is not being punished for that; it is a faint sense of a room being too large, and it
    /// has to stay faint or every unpartnered breeding pair in the colony would carry a standing
    /// penalty for not having reproduced.
    /// </summary>
    public class ThoughtWorker_AvirBondNest : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Pawn mate = AvirBondSocial.MateOf(p);
            if (mate == null) return ThoughtState.Inactive;

            Building_Bed bed = p.ownership?.OwnedBed;
            Room room = bed?.GetRoom();
            if (room == null || room.PsychologicallyOutdoors) return ThoughtState.Inactive;

            if (AnyOwnChickSleepingIn(p, room)) return ThoughtState.ActiveAtStage(0);

            return ThoughtState.ActiveAtStage(1);
        }

        private static bool AnyOwnChickSleepingIn(Pawn parent, Room room)
        {
            List<DirectPawnRelation> relations = parent.relations?.DirectRelations;
            if (relations == null) return false;

            // Searched from the parent's side through Child relations. Where the tailing and
            // grooming systems walk the map because the Parent relation lives on the chick, here
            // the parent's own list is enough and far cheaper.
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation rel = relations[i];
                if (rel?.def == null || rel.otherPawn == null) continue;
                if (rel.def != PawnRelationDefOf.Child) continue;

                Pawn chick = rel.otherPawn;
                if (chick.Dead || !chick.Spawned) continue;

                // Grown children are not part of the pile any more.
                if (!chick.DevelopmentalStage.Juvenile()) continue;

                Building_Bed chickBed = chick.ownership?.OwnedBed;
                if (chickBed == null) continue;

                if (chickBed.GetRoom() == room) return true;
            }

            return false;
        }
    }
}
