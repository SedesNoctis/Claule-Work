using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// THE CONDITION, EXPRESSED IN XML. Every field is optional and every one that IS set must
    /// hold - they are ANDed. A ThoughtDef with this extension and nothing filled in is active
    /// always, which is exactly the old ThoughtWorker_AvirFlavor behaviour and exactly what we are
    /// getting rid of, so leaving one empty is a mistake rather than a shortcut.
    /// </summary>
    public class AvirThoughtCondition : DefModExtension
    {
        // ---- current activity ----
        public WorkTags whileWorkTags;   // flags: <li>Artistic</li><li>Crafting</li> read as one mask
        public bool requireIdle;
        public bool requireDrafted;

        // ---- company ----
        public float nearbyRadius = 12f;
        public int minNearbyColonists = -1;
        public int maxNearbyColonists = -1;
        /// <summary>Counts only colonists this pawn actively dislikes. Turns a crowd check into a
        /// grudge check without a second worker.</summary>
        public bool nearbyOnlyDisliked;
        public int dislikedOpinionBelow = -20;

        // ---- needs ----
        public NeedDef need;
        public float needBelow = -1f;
        public float needAbove = -1f;

        // ---- apparel ----
        /// <summary>Active only when NOTHING covers this group. Featherproud wants bare plumage.</summary>
        public BodyPartGroupDef uncoveredGroup;

        // ---- room ----
        public RoomStatDef roomStat;
        public float roomStatBelow = -1f;
        public float roomStatAbove = -1f;

        // ---- social standing ----
        public float avgOpinionAbove = -999f;
        public float avgOpinionBelow = 999f;

        // ---- memory / permanent marks ----
        public ThoughtDef requiredMemory;
        public HediffDef requiredHediff;
    }

    /// <summary>
    /// Replaces ThoughtWorker_AvirFlavor, which was one line - ActiveAtStage(0), unconditionally -
    /// and therefore handed a flat mood offset to every pawn that happened to roll the trait. A
    /// mood swing nobody can trace to anything is worse than no mood swing: the player cannot act
    /// on it, cannot predict it, and eventually stops reading the needs tab.
    ///
    /// ONE PARAMETERIZED WORKER RATHER THAN EIGHTEEN BESPOKE ONES, same reasoning as AvirChoirDef.
    /// Eighteen classes that each test one thing would mean eighteen places to fix a bug and a
    /// rebuild every time a number moves. Here the condition lives in the ThoughtDef beside the
    /// mood value it gates, and tuning is an XML edit.
    ///
    /// COST. Situational thoughts are recalculated per pawn on an interval, not per tick, and every
    /// check here is ordered cheapest-first: flags and needs before anything that walks a pawn
    /// list or asks a room for a stat. The two genuinely expensive branches - nearby-pawn counting
    /// and average opinion - only run for the handful of ThoughtDefs that ask for them.
    /// </summary>
    public class ThoughtWorker_AvirConditional : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.Dead) return ThoughtState.Inactive;

            AvirThoughtCondition cond = def.GetModExtension<AvirThoughtCondition>();
            if (cond == null) return ThoughtState.Inactive;

            // ---- flags: free ----
            if (cond.requireDrafted && !p.Drafted) return ThoughtState.Inactive;

            if (cond.requireIdle)
            {
                Job job = p.CurJob;
                if (job != null && job.def != JobDefOf.Wait && job.def != JobDefOf.Wait_Wander
                    && job.def != JobDefOf.GotoWander)
                {
                    return ThoughtState.Inactive;
                }
            }

            if (cond.whileWorkTags != WorkTags.None)
            {
                if (!MatchesWorkTag(p, cond.whileWorkTags)) return ThoughtState.Inactive;
            }

            // ---- marks: one dictionary hit each ----
            if (cond.requiredHediff != null
                && (p.health?.hediffSet == null || !p.health.hediffSet.HasHediff(cond.requiredHediff)))
            {
                return ThoughtState.Inactive;
            }

            if (cond.requiredMemory != null)
            {
                MemoryThoughtHandler memories = p.needs?.mood?.thoughts?.memories;
                if (memories == null || memories.NumMemoriesOfDef(cond.requiredMemory) <= 0)
                {
                    return ThoughtState.Inactive;
                }
            }

            // ---- needs: one float read ----
            if (cond.need != null)
            {
                Need need = p.needs?.TryGetNeed(cond.need);
                if (need == null) return ThoughtState.Inactive;
                if (cond.needBelow >= 0f && need.CurLevelPercentage >= cond.needBelow) return ThoughtState.Inactive;
                if (cond.needAbove >= 0f && need.CurLevelPercentage <= cond.needAbove) return ThoughtState.Inactive;
            }

            // ---- apparel: short list walk ----
            if (cond.uncoveredGroup != null && CoversGroup(p, cond.uncoveredGroup)) return ThoughtState.Inactive;

            // ---- room: a stat lookup, cached by the room itself ----
            if (cond.roomStat != null)
            {
                Room room = p.GetRoom();
                if (room == null || room.PsychologicallyOutdoors) return ThoughtState.Inactive;
                float value = room.GetStat(cond.roomStat);
                if (cond.roomStatBelow >= 0f && value >= cond.roomStatBelow) return ThoughtState.Inactive;
                if (cond.roomStatAbove >= 0f && value <= cond.roomStatAbove) return ThoughtState.Inactive;
            }

            // ---- the two expensive ones, last ----
            if (cond.minNearbyColonists >= 0 || cond.maxNearbyColonists >= 0)
            {
                int count = CountNearby(p, cond);
                if (cond.minNearbyColonists >= 0 && count < cond.minNearbyColonists) return ThoughtState.Inactive;
                if (cond.maxNearbyColonists >= 0 && count > cond.maxNearbyColonists) return ThoughtState.Inactive;
            }

            if (cond.avgOpinionAbove > -999f || cond.avgOpinionBelow < 999f)
            {
                float avg = AverageOpinionOf(p);
                if (avg <= cond.avgOpinionAbove) return ThoughtState.Inactive;
                if (avg >= cond.avgOpinionBelow) return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveAtStage(0);
        }

        private static bool MatchesWorkTag(Pawn p, WorkTags tags)
        {
            Job job = p.CurJob;
            WorkTags jobTags = job?.workGiverDef?.workTags ?? WorkTags.None;
            return jobTags != WorkTags.None && (jobTags & tags) != WorkTags.None;
        }

        private static bool CoversGroup(Pawn p, BodyPartGroupDef group)
        {
            List<Apparel> worn = p.apparel?.WornApparel;
            if (worn == null) return false;

            for (int i = 0; i < worn.Count; i++)
            {
                List<BodyPartGroupDef> groups = worn[i]?.def?.apparel?.bodyPartGroups;
                if (groups != null && groups.Contains(group)) return true;
            }

            return false;
        }

        private static int CountNearby(Pawn p, AvirThoughtCondition cond)
        {
            Map map = p.Map;
            if (map == null || !p.Spawned) return 0;

            float radiusSq = cond.nearbyRadius * cond.nearbyRadius;
            int count = 0;

            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn other = colonists[i];
                if (other == null || other == p) continue;
                if ((other.Position - p.Position).LengthHorizontalSquared > radiusSq) continue;

                if (cond.nearbyOnlyDisliked)
                {
                    if (p.relations == null) continue;
                    if (p.relations.OpinionOf(other) >= cond.dislikedOpinionBelow) continue;
                }

                count++;
            }

            return count;
        }

        private static float AverageOpinionOf(Pawn p)
        {
            Map map = p.Map;
            if (map == null) return 0f;

            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            int n = 0;
            float total = 0f;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn other = colonists[i];
                if (other == null || other == p || other.relations == null) continue;
                total += other.relations.OpinionOf(p);
                n++;
            }

            return n > 0 ? total / n : 0f;
        }
    }
}
