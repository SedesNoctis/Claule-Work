using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// EATING THE DEAD WHERE THEY FELL.
    ///
    /// MAXIMUS'S WORLD, HIS WORDS. In the novel the A'Schialis tear into bodies during the killing
    /// itself, before the body has finished falling - the hunger is strong enough that waiting is
    /// not a thing they do. The Predator Mindset gene has always PROMISED this: "You stalk, you
    /// kill, you devour the dead." Sixteen thoughts about corpses, cannibalism and eating without
    /// a table are already nullified for its carriers. Everything was ready except the eating.
    ///
    /// HIS THREE DECISIONS, RECORDED SO THEY ARE NOT QUIETLY REVERSED LATER:
    ///
    ///   WHO   - carriers of PredatorMindset - since 2026-10-01 part of Fearborn. In this mod's own defs that is only the harpy
    ///           xenotypes, but Maximus's Avir xenotype is his own custom one and carries the gene,
    ///           so the gate is the gene rather than a race or faction check. Anyone who has it
    ///           eats; anyone who does not, does not.
    ///
    ///   WHEN  - when hungry, at any fresh corpse in reach. NOT only after their own kill. He chose
    ///           the broader rule deliberately: the narrow one leaves fallen raiders lying about
    ///           and does nothing for the problem he actually had, which was storing bodies without
    ///           a butcher.
    ///
    ///   AFTER - nothing is left. He asked for no leavings and no cleanup, in his words "no
    ///           technical overhead". So the corpse is consumed outright: no bones, no scraps, no
    ///           filth to haul.
    ///
    /// THE FREEZER PROBLEM SOLVES ITSELF. Bodies are eaten where they lie, so nothing needs
    /// hauling, storing or chilling - which is what he wanted from this in the first place.
    /// </summary>
    public static class AvirDevourDefOf
    {
        public const string GeneName = "Fearborn";   // predator mindset merged into Fearborn
        public const string JobName = "Avir_DevourCorpse";

        private static GeneDef gene;
        private static JobDef job;
        private static bool resolved;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            gene = DefDatabase<GeneDef>.GetNamedSilentFail(GeneName);
            job = DefDatabase<JobDef>.GetNamedSilentFail(JobName);

            if (gene == null)
            {
                Log.Warning("[Avir devour] gene " + GeneName + " not found - devouring is off.");
            }

            if (job == null)
            {
                Log.Warning("[Avir devour] job " + JobName + " not found - devouring is off. "
                    + "Is Jobs_Avir_Devour.xml installed?");
            }
        }

        public static GeneDef Gene { get { Resolve(); return gene; } }
        public static JobDef Job { get { Resolve(); return job; } }

        /// <summary>
        /// Below this the pawn will go and eat a body. Set at the level vanilla calls "hungry"
        /// rather than at starvation: Maximus's Avir devour because the hunger is strong, not
        /// because they are dying. A starving-only rule would make this almost never fire.
        /// </summary>
        public const float HungerThreshold = 0.35f;

        /// <summary>
        /// How far a pawn will walk to a body. Deliberately short. This is meant to catch the
        /// corpse at your feet after a fight, not to send colonists jogging across the map every
        /// time something dies in a corner.
        /// </summary>
        public const float SearchRadius = 24f;

        public static bool CarriesMindset(Pawn pawn)
        {
            GeneDef g = Gene;
            if (g == null || pawn?.genes == null) return false;
            return pawn.genes.HasActiveGene(g);
        }
    }

    /// <summary>
    /// Finds a body worth eating.
    ///
    /// A THINK NODE RATHER THAN A WORK GIVER, on purpose. Devouring is not a chore a colonist gets
    /// assigned; it is something a hungry predator does instead of walking to the dining room. A
    /// work giver would also mean a raider or a guest could never do it, and the whole point is
    /// that any carrier of the gene behaves this way.
    /// </summary>
    public class JobGiver_AvirDevourCorpse : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            try
            {
                if (pawn == null || !pawn.Spawned || pawn.Map == null) return null;
                if (pawn.Drafted) return null;

                // CHEAPEST TEST FIRST. This runs inside the job search, which the profiler showed
                // is the single most expensive thing in Maximus's game - three quarters of the
                // tick. A gene lookup rejects almost every pawn before anything else happens.
                if (!AvirDevourDefOf.CarriesMindset(pawn)) return null;

                Need_Food food = pawn.needs?.food;
                if (food == null) return null;
                if (food.CurLevelPercentage > AvirDevourDefOf.HungerThreshold) return null;

                JobDef def = AvirDevourDefOf.Job;
                if (def == null) return null;

                Corpse target = FindCorpse(pawn);
                if (target == null) return null;

                return JobMaker.MakeJob(def, target);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir devour] job search failed: " + e, 0x4A52D1);
                return null;
            }
        }

        private static Corpse FindCorpse(Pawn pawn)
        {
            Map map = pawn.Map;
            List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            if (corpses == null || corpses.Count == 0) return null;

            float bestDistSq = AvirDevourDefOf.SearchRadius * AvirDevourDefOf.SearchRadius;
            Corpse best = null;

            for (int i = 0; i < corpses.Count; i++)
            {
                Corpse corpse = corpses[i] as Corpse;
                if (corpse == null || !corpse.Spawned) continue;

                float distSq = (corpse.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq > bestDistSq) continue;

                if (!Edible(pawn, corpse)) continue;
                if (!pawn.CanReserveAndReach(corpse, PathEndMode.Touch, Danger.Some)) continue;

                bestDistSq = distSq;
                best = corpse;
            }

            return best;
        }

        /// <summary>
        /// WHAT COUNTS AS FOOD.
        ///
        /// Animals and humanlikes both - Maximus did not restrict it, and a predator that turns up
        /// its beak at a dead muffalo would be a strange predator.
        ///
        /// A BONDED MATE IS NEVER FOOD. This is not in his brief and I am adding it deliberately:
        /// pair-bonding in his world takes a decade and happens once in a lifetime, and an Avir
        /// eating the one person that bond was formed with would read as a bug even to someone who
        /// wanted this feature. If he disagrees it is one line to remove.
        ///
        /// Rotten bodies are skipped. Not squeamishness - vanilla food poisoning would follow, and
        /// nothing about this mechanic is meant to make the eater ill.
        /// </summary>
        private static bool Edible(Pawn pawn, Corpse corpse)
        {
            Pawn dead = corpse.InnerPawn;
            if (dead == null) return false;

            if (corpse.IsForbidden(pawn) || corpse.IsBurning()) return false;

            if (corpse.GetRotStage() != RotStage.Fresh) return false;

            // A corpse waiting on a Phoenix rebirth is a rite in progress, not a meal.
            GameComponent_AvirPhoenixRebirth rebirth =
                Current.Game?.GetComponent<GameComponent_AvirPhoenixRebirth>();
            if (rebirth != null && rebirth.IsPending(dead)) return false;

            // AvirPairBond has no two-pawn test, so this uses the same call AvirBondHediff already
            // relies on - ExistingMostLikedLovePartner - with allowDead: true, because the whole
            // question here is whether the body on the ground is the mate.
            if (pawn.relations != null
                && LovePartnerRelationUtility.ExistingMostLikedLovePartner(pawn, true) == dead)
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Walk to the body, tear into it, leave nothing.
    ///
    /// THE DURATION IS THE WHOLE OF THE STAGING. There is no carrying, no hauling to a table and no
    /// leftovers - Maximus asked for none of that. The pawn arrives, spends a while, and the body
    /// is gone.
    /// </summary>
    public class JobDriver_AvirDevourCorpse : JobDriver
    {
        private const TargetIndex CorpseIndex = TargetIndex.A;

        /// <summary>
        /// About four in-game hours' worth of ticks at normal speed - long enough to see happen on
        /// the map and to read as an act rather than a tick, matching the pacing of the grooming
        /// jobs next door.
        /// </summary>
        private const int DevourTicks = 600;

        private Corpse Target => (Corpse)job.GetTarget(CorpseIndex).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(CorpseIndex), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(CorpseIndex);

            yield return Toils_Goto.GotoThing(CorpseIndex, PathEndMode.Touch);

            Toil devour = Toils_General.Wait(DevourTicks, CorpseIndex);
            devour.FailOnDespawnedNullOrForbidden(CorpseIndex);
            devour.WithProgressBarToilDelay(CorpseIndex);
            devour.handlingFacing = true;
            yield return devour;

            Toil finish = ToilMaker.MakeToil("AvirDevourFinish");
            finish.initAction = delegate
            {
                try
                {
                    Corpse corpse = Target;
                    if (corpse == null || !corpse.Spawned) return;

                    // NUTRITION COMES FROM THE BODY, not from a flat number. A dead muffalo feeds
                    // an Avir properly; a dead chicken barely registers. Using the corpse's own
                    // value keeps that honest without a table of special cases.
                    float nutrition = CorpseNutrition(corpse);

                    Need_Food food = pawn.needs?.food;
                    if (food != null && nutrition > 0f)
                    {
                        food.CurLevel = Mathf.Min(food.MaxLevel, food.CurLevel + nutrition);
                    }

                    // NOTHING IS LEFT. Maximus asked for no remains and no cleanup, so the corpse
                    // is destroyed outright rather than butchered into meat on the ground.
                    corpse.Destroy(DestroyMode.Vanish);
                }
                catch (Exception e)
                {
                    Log.WarningOnce("[Avir devour] finishing the meal failed: " + e, 0x4A52D2);
                }
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }

        private static float CorpseNutrition(Corpse corpse)
        {
            Pawn dead = corpse.InnerPawn;
            if (dead == null) return 0f;

            // Body size is the honest scale here and it is available on every pawn, which a meat
            // count is not once other mods start replacing butchery products.
            float size = dead.RaceProps?.baseBodySize ?? 1f;
            return Mathf.Clamp(size * 1.2f, 0.2f, 4f);
        }
    }

    /// <summary>
    /// Puts the devour node into the think trees.
    ///
    /// PLACED AHEAD OF WORK, NOT AHEAD OF EATING. A hungry Avir with a cooked meal in the freezer
    /// will still go and eat the meal - vanilla's food seeking sits above work and therefore above
    /// this. Devouring is what happens when she is hungry and there is a body closer than dinner,
    /// which is exactly the situation Maximus described: the killing has just happened and she is
    /// hungry now.
    ///
    /// Both trees, same reason as grooming: cyanobot's Toddlers gives one-to-three-year-olds a
    /// separate tree, and a hungry chick standing over a body should behave like its parents.
    /// Absent Toddlers the second call is a silent no-op.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirDevourThinkInjector
    {
        private const string WorkAnchor = "JobGiver_Work";

        static AvirDevourThinkInjector()
        {
            InjectInto("Humanlike", isOptional: false);
            InjectInto("HumanlikeToddler", isOptional: true);
        }

        private static void InjectInto(string treeDefName, bool isOptional)
        {
            try
            {
                ThinkTreeDef tree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(treeDefName);
                if (tree?.thinkRoot == null)
                {
                    if (!isOptional)
                    {
                        Log.Warning("[Avir devour] " + treeDefName + " think tree not found; "
                            + "corpses will not be eaten.");
                    }
                    return;
                }

                if (AlreadyInjected(tree.thinkRoot)) return;

                if (InsertBefore(tree.thinkRoot, WorkAnchor))
                {
                    Log.Message("[Avir devour] inserted into " + treeDefName + " ahead of "
                        + WorkAnchor + ".");
                    return;
                }

                // No work node - a toddler tree, for instance. Anchor near the root instead; the
                // node's own hunger and gene tests still decide whether anything happens.
                if (tree.thinkRoot.subNodes != null)
                {
                    tree.thinkRoot.subNodes.Insert(
                        Mathf.Min(1, tree.thinkRoot.subNodes.Count),
                        new JobGiver_AvirDevourCorpse());
                    Log.Message("[Avir devour] " + treeDefName + " has no " + WorkAnchor
                        + " - anchored near the root instead.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir devour] could not inject into " + treeDefName + ": " + e);
            }
        }

        private static bool AlreadyInjected(ThinkNode node)
        {
            if (node?.subNodes == null) return false;

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                if (node.subNodes[i] is JobGiver_AvirDevourCorpse) return true;
                if (AlreadyInjected(node.subNodes[i])) return true;
            }

            return false;
        }

        private static bool InsertBefore(ThinkNode node, string anchorTypeName)
        {
            if (node?.subNodes == null) return false;

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                if (node.subNodes[i] != null
                    && node.subNodes[i].GetType().Name == anchorTypeName)
                {
                    node.subNodes.Insert(i, new JobGiver_AvirDevourCorpse());
                    return true;
                }
            }

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                if (InsertBefore(node.subNodes[i], anchorTypeName)) return true;
            }

            return false;
        }
    }
}
