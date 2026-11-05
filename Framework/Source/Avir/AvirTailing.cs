using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    [DefOf]
    public static class AvirTailingDefOf
    {
        public static JobDef Avir_TailingJob;
        public static HediffDef Avir_TailingCooldown;
        public static ThoughtDef Avir_Cuddled;
        public static ThoughtDef Avir_Tussled;
        public static InteractionDef Avir_Cuddle;

        static AvirTailingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirTailingDefOf));
        }
    }

    /// <summary>
    /// Shared validity rules for tailing, used by both the joy giver (before starting) and the
    /// job driver (every tick, as a fail condition).
    /// </summary>
    public static class AvirTailing
    {
        /// Close enough to count as caught up.
        public const float CuddleRangeSquared = 4f;

        /// Re-path no more often than this. The lover is walking, not teleporting.
        public const int RepathInterval = 30;

        /// How often a chick that has caught up with unresponsive kin may try its luck, and how
        /// likely each try is. Over one tailing session these add up to "probably, eventually",
        /// which combined with the day-long cooldown lands on roughly once a day per chick.
        public const int NudgeInterval = 120;
        public const float NudgeChance = 0.12f;

        /// Chance a chick picks a parent to shadow even though a clutchmate was available.
        public const float ParentOverSiblingChance = 0.25f;

        /// Biological age band in which a chick shadows its kin instead of a partner.
        public const int MinYounglingAge = 3;
        public const int MaxYounglingAge = 13;

        /// <summary>
        /// Matched by defName rather than through PawnRelationDefOf, so a missing or renamed
        /// entry in the DefOf class cannot stop the whole assembly from loading.
        /// </summary>
        private static readonly HashSet<string> SiblingRelationNames =
            new HashSet<string> { "Sibling", "HalfSibling" };

        private static readonly HashSet<string> ParentRelationNames =
            new HashSet<string> { "Parent" };

        /// <summary>Is this pawn at a loose end right now, rather than mid-task?</summary>
        public static bool IsAtLeisure(Pawn pawn)
        {
            if (pawn == null) return false;

            Job job = pawn.CurJob;
            if (job == null) return true;

            JobDef def = job.def;
            if (def == AvirTailingDefOf.Avir_TailingJob) return true;

            // Joy jobs and idle wandering both count as free time.
            if (def.joyKind != null) return true;

            return def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander
                || def == JobDefOf.Wait_MaintainPosture;
        }

        public static bool IsYoungling(Pawn p)
        {
            if (p == null || p.ageTracker == null) return false;
            int age = p.ageTracker.AgeBiologicalYears;
            return age >= MinYounglingAge && age <= MaxYounglingAge;
        }

        /// <summary>
        /// Who this pawn would shadow, or null. Chicks follow family; everyone else follows the
        /// partner they like most. One entry point for both the joy giver and the idle node, so
        /// the two can never disagree about who is being tailed.
        /// </summary>
        public static Pawn FindTailTarget(Pawn pawn)
        {
            if (pawn == null || pawn.relations == null) return null;

            if (IsYoungling(pawn)) return FindKin(pawn);

            Pawn lover = LovePartnerRelationUtility.ExistingMostLikedLovePartner(pawn, false);
            return IsValidTarget(pawn, lover) ? lover : null;
        }

        /// <summary>
        /// Siblings first, parents second. Avir hatch in pairs, so a clutchmate is the natural
        /// shadow; a parent is the fallback for a chick without one to hand.
        /// </summary>
        private static Pawn FindKin(Pawn pawn)
        {
            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            if (relations == null) return null;

            Pawn sibling = null;
            Pawn parent = null;

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation rel = relations[i];
                if (rel == null || rel.def == null || rel.otherPawn == null) continue;
                if (!IsValidTarget(pawn, rel.otherPawn)) continue;

                if (sibling == null && SiblingRelationNames.Contains(rel.def.defName))
                    sibling = rel.otherPawn;
                else if (parent == null && ParentRelationNames.Contains(rel.def.defName))
                    parent = rel.otherPawn;
            }

            // Clutchmate by default, but not exclusively - otherwise a chick with a living
            // sibling would never once go to a parent, and the parent side of this would be
            // dead code in most colonies.
            if (sibling != null && parent != null)
                return Rand.Chance(ParentOverSiblingChance) ? parent : sibling;

            return sibling ?? parent;
        }

        public static bool CanTail(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Map == null) return false;
            if (pawn.genes == null || !pawn.genes.HasActiveGene(AvirNameDefOf.Fearborn)) return false;
            if (pawn.Drafted || pawn.Downed || pawn.InMentalState) return false;
            if (pawn.IsPrisoner) return false;
            if (pawn.health != null && pawn.health.hediffSet.HasHediff(AvirTailingDefOf.Avir_TailingCooldown)) return false;
            return true;
        }

        public static bool IsValidTarget(Pawn pawn, Pawn lover)
        {
            if (lover == null || lover.Dead || !lover.Spawned) return false;
            if (lover == pawn) return false;
            if (lover.Map != pawn.Map) return false;
            if (lover.Downed || lover.InBed()) return false;
            if (lover.InMentalState || lover.IsPrisoner) return false;
            if (lover.HostileTo(pawn)) return false;
            return true;
        }

        /// <summary>True when the other pawn is tailing us back - the trigger for a cuddle.</summary>
        public static bool IsTailingBack(Pawn pawn, Pawn lover)
        {
            if (lover?.CurJob == null) return false;
            if (lover.CurJob.def != AvirTailingDefOf.Avir_TailingJob) return false;
            return lover.CurJob.targetA.Thing == pawn;
        }

        /// <summary>
        /// Both of them gave up on pretending and just tucked into each other. Small mood bump,
        /// then a hidden day-long cooldown on BOTH so this cannot loop.
        /// </summary>
        /// <param name="mutual">
        /// True when both parties were shadowing each other, so both are settled and both go on
        /// cooldown. False for a one-sided nudge: only the initiator calms down, and the other
        /// pawn is not interrupted or locked out of their own tailing for the rest of the day.
        /// </param>
        public static void DoCuddle(Pawn a, Pawn b, bool mutual)
        {
            if (a == null || b == null) return;

            // InteractionDef.Symbol hands its raw path string to ContentFinder.Get, so a def with
            // no <symbol> throws ArgumentNullException from a null Dictionary key before the
            // worker is ever reached - which is exactly how this crashed the first time. The XML
            // now supplies one. It cannot be re-checked from here, because the backing field is
            // not public; reading the Symbol property to test it would trigger the very lookup
            // being guarded against. The tick's try/catch is the net if it ever regresses.
            InteractionDef cuddle = AvirTailingDefOf.Avir_Cuddle;
            if (a.interactions != null && cuddle != null && cuddle.Worker != null)
            {
                a.interactions.TryInteractWith(b, cuddle);
            }

            GiveCuddleMemory(a, b);
            GiveCuddleMemory(b, a);

            ApplyCooldown(a);

            if (mutual)
            {
                ApplyCooldown(b);
                if (b.jobs != null) b.jobs.EndCurrentJob(JobCondition.Succeeded, false);
            }
        }

        private static void GiveCuddleMemory(Pawn p, Pawn other)
        {
            if (p?.needs?.mood?.thoughts?.memories == null) return;

            // A pair of chicks piling into each other is not the same event as two adults
            // settling together, and it should not read as though it were. If either party is
            // still a youngling, both of them get the sibling version.
            bool youngling = IsYoungling(p) || IsYoungling(other);
            ThoughtDef memory = youngling ? AvirTailingDefOf.Avir_Tussled : AvirTailingDefOf.Avir_Cuddled;
            if (memory == null) memory = AvirTailingDefOf.Avir_Cuddled;
            if (memory == null) return;

            p.needs.mood.thoughts.memories.TryGainMemory(memory, other);
        }

        /// <summary>
        /// The joy kind this activity pays into. Read from the JobDef first, then looked up by
        /// name, and null if neither works - callers must treat null as "pay no recreation"
        /// rather than passing it on, because the joy tolerance set is keyed by JoyKindDef and a
        /// null key throws.
        /// </summary>
        public static JoyKindDef ResolveJoyKind(JobDef jobDef)
        {
            if (jobDef != null && jobDef.joyKind != null) return jobDef.joyKind;

            if (!resolvedFallbackKind)
            {
                resolvedFallbackKind = true;
                fallbackKind = DefDatabase<JoyKindDef>.GetNamedSilentFail("Social");
                if (fallbackKind == null)
                    Log.Warning("[Avir] No JoyKindDef named 'Social'; tailing will not fill recreation.");
            }

            return fallbackKind;
        }

        private static bool resolvedFallbackKind;
        private static JoyKindDef fallbackKind;

        /// <summary>Cooldown entry point for the job driver's error path.</summary>
        public static void ApplyCooldownPublic(Pawn p)
        {
            ApplyCooldown(p);
        }

        private static void ApplyCooldown(Pawn p)
        {
            if (p?.health == null || AvirTailingDefOf.Avir_TailingCooldown == null) return;
            if (p.health.hediffSet.HasHediff(AvirTailingDefOf.Avir_TailingCooldown)) return;
            p.health.AddHediff(AvirTailingDefOf.Avir_TailingCooldown);
        }
    }

    /// <summary>
    /// Fearborn-only recreation: shadow your lover around the base.
    ///
    /// Deliberately an ordinary JoyGiver rather than a hook into wandering. A joy giver's
    /// TryGiveJob only runs when a pawn is actually picking something to do with its free time -
    /// not every tick, and not for anyone else - which is what keeps this cheap. It also means
    /// the activity slots into the recreation schedule, fills the recreation need through the
    /// normal machinery, and competes fairly with chess and horseshoes.
    ///
    /// Nothing about this is advertised anywhere. It is not mentioned in the Fearborn gene
    /// description and JoyGiverDefs are not surfaced in any gene or pawn UI, so a player only
    /// ever finds it by watching two Avir and noticing.
    /// </summary>
    public class JoyGiver_AvirTailing : JoyGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            if (!AvirTailing.CanTail(pawn)) return null;

            Pawn target = AvirTailing.FindTailTarget(pawn);
            if (target == null) return null;

            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.None)) return null;

            return JobMaker.MakeJob(AvirTailingDefOf.Avir_TailingJob, target);
        }
    }

    /// <summary>
    /// The second entry point into tailing, for idle time rather than scheduled recreation.
    ///
    /// The joy giver only ever fires while a pawn is deliberately choosing a recreation activity.
    /// This node covers the other case: a pawn with nothing left to do, who would otherwise be
    /// handed a short wander job, finish it, and send the whole think tree - work scan included -
    /// round again a few hundred ticks later. A tailing job is long, so while it runs that churn
    /// stops.
    ///
    /// Worth being clear about the size of that win: it only applies to a Fearborn pawn whose
    /// lover is also free and off cooldown, which is a small slice of a colony at any one moment.
    /// This is not a general TPS fix. What it does reliably deliver is the other half of the
    /// idea - that in their own time these two actually go and find each other.
    /// </summary>
    public class JobGiver_AvirTailingIdle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!AvirTailing.CanTail(pawn)) return null;

            // Never pull a pawn away from something that still matters.
            if (pawn.needs != null)
            {
                if (pawn.needs.rest != null && pawn.needs.rest.CurLevel < 0.28f) return null;
                if (pawn.needs.food != null && pawn.needs.food.CurLevel < 0.28f) return null;
            }

            Pawn target = AvirTailing.FindTailTarget(pawn);
            if (target == null) return null;

            // Only tail someone who is themselves at a loose end. Chasing a partner who is busy
            // hauling steel across the base is not the picture, and it would keep the tailer
            // walking indefinitely.
            if (!AvirTailing.IsAtLeisure(target)) return null;

            if (!pawn.CanReach(target, PathEndMode.Touch, Danger.None)) return null;

            return JobMaker.MakeJob(AvirTailingDefOf.Avir_TailingJob, target);
        }

    }
    public class JobDriver_AvirTailing : JobDriver
    {
        private Pawn Lover { get { return job.targetA.Thing as Pawn; } }

        // Vanilla's per-tick joy figure. Scaled by the JobDef's joyGainRate.
        private const float BaseJoyPerTick = 0.36f / 400f;

        // One detailed trace per session is plenty; after that the cooldown keeps it quiet.
        private static bool loggedTickFailure;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Deliberately reserves nothing. The lover is a person going about their day, not a
            // workpiece - reserving them would block their own jobs.
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !AvirTailing.CanTail(pawn) || !AvirTailing.IsValidTarget(pawn, Lover));

            Toil tail = new Toil();
            tail.defaultCompleteMode = ToilCompleteMode.Delay;
            tail.defaultDuration = job.def.joyDuration > 0 ? job.def.joyDuration : 2000;
            tail.handlingFacing = true;

            tail.tickAction = delegate
            {
                try
                {
                    TailTick();
                }
                catch (System.Exception e)
                {
                    // The truncated one-liner RimWorld prints for a driver tick exception is not
                    // enough to fix anything, so print the whole trace exactly once per session.
                    if (!loggedTickFailure)
                    {
                        loggedTickFailure = true;
                        Log.Error("[Avir] Tailing tick threw. Full trace follows; this is printed only once per session.\n" + e);
                    }

                    // Put the pair on the normal cooldown rather than just ending the job -
                    // otherwise the joy giver hands the same broken job straight back and the
                    // log fills with the same exception several times a second.
                    AvirTailing.ApplyCooldownPublic(pawn);
                    EndJobWith(JobCondition.Errored);
                }
            };

            yield return tail;
        }

        private void TailTick()
        {
            Pawn lover = Lover;
            if (lover == null || !lover.Spawned || pawn.Map == null) return;

            float distSq = pawn.Position.DistanceToSquared(lover.Position);

            if (distSq <= AvirTailing.CuddleRangeSquared)
            {
                // Caught up, and they were coming for us too. Resolve it.
                if (AvirTailing.IsTailingBack(pawn, lover))
                {
                    // Only one side runs the resolution, or both would fire it in the same tick
                    // and double the thought. Lower ID wins; the choice is arbitrary but stable.
                    if (pawn.thingIDNumber < lover.thingIDNumber)
                    {
                        AvirTailing.DoCuddle(pawn, lover, true);
                        EndJobWith(JobCondition.Succeeded);
                    }
                    return;
                }

                // One-sided nudge. A chick that has run down a relative who was never chasing
                // back gets an occasional chance to be picked up anyway. Adults do not do this:
                // a parent never goes looking for a chick, it only ever accepts one that has
                // already arrived, and only while it has nothing better in hand.
                if (AvirTailing.IsYoungling(pawn)
                    && pawn.IsHashIntervalTick(AvirTailing.NudgeInterval)
                    && AvirTailing.IsAtLeisure(lover)
                    && Rand.Chance(AvirTailing.NudgeChance))
                {
                    AvirTailing.DoCuddle(pawn, lover, false);
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
            }

            if (distSq > AvirTailing.CuddleRangeSquared)
            {
                // Previously this tested "pather == null || not moving || wrong destination" and
                // then called pather.StartPath - so a null pather satisfied the condition and
                // went straight into a null dereference. Guard first, then decide.
                if (pawn.pather != null && pawn.IsHashIntervalTick(AvirTailing.RepathInterval))
                {
                    bool alreadyHeaded = pawn.pather.Moving
                        && pawn.pather.Destination.HasThing
                        && pawn.pather.Destination.Thing == lover;

                    if (!alreadyHeaded) pawn.pather.StartPath(lover, PathEndMode.Touch);
                }
            }
            else if (pawn.rotationTracker != null)
            {
                pawn.rotationTracker.FaceTarget(lover);
            }

            Need_Joy joy = pawn.needs != null ? pawn.needs.joy : null;
            if (joy == null) return;

            if (joy.CurLevel >= 1f)
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }

            // Resolved defensively rather than trusting the DefOf. Feeding a null JoyKindDef into
            // the joy tolerance set is one of the few things in this whole tick that can raise an
            // ArgumentNullException, so if it cannot be resolved the activity simply stops paying
            // recreation instead of throwing every tick.
            JoyKindDef kind = AvirTailing.ResolveJoyKind(job.def);
            if (kind == null) return;

            float rate = job.def.joyGainRate > 0f ? job.def.joyGainRate : 1f;
            joy.GainJoy(BaseJoyPerTick * rate, kind);
        }
    }

    /// <summary>
    /// Puts JobGiver_AvirTailingIdle into the humanlike think tree, immediately ahead of the node
    /// that handles idling.
    ///
    /// Two deliberate choices here.
    ///
    /// It does NOT go where the essence-feeding nodes go. Those sit at index 1 of the think root,
    /// above ordinary work, because starving is urgent. Tailing at that height would outrank
    /// every job in the game and Avir would simply stop working.
    ///
    /// The anchor node is matched by TYPE NAME rather than by a type reference. A hard reference
    /// would be tidier, but it also makes the whole assembly fail to load if the class is ever
    /// renamed or moved between namespaces, and this feature is not worth that risk. A miss here
    /// costs nothing: tailing still works as ordinary recreation through the joy giver.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirTailingThinkInjector
    {
        // Preferred first. IdleJoy is specifically "nothing to do, go enjoy yourself", which is
        // exactly the slot this belongs in; wandering is the broader fallback.
        private static readonly string[] AnchorTypeNames = { "JobGiver_IdleJoy", "JobGiver_WanderColony" };

        static AvirTailingThinkInjector()
        {
            try
            {
                ThinkTreeDef tree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("Humanlike");
                if (tree == null || tree.thinkRoot == null)
                {
                    Log.Warning("[Avir] Humanlike think tree not found; tailing will only happen during recreation time.");
                    return;
                }

                if (AlreadyInjected(tree.thinkRoot)) return;

                for (int a = 0; a < AnchorTypeNames.Length; a++)
                {
                    if (InsertBefore(tree.thinkRoot, AnchorTypeNames[a]))
                    {
                        Log.Message("[Avir] Idle tailing inserted ahead of " + AnchorTypeNames[a] + ".");
                        return;
                    }
                }

                Log.Warning("[Avir] No idle anchor node found in the humanlike think tree ("
                    + string.Join(", ", AnchorTypeNames)
                    + "); tailing will only happen during recreation time.");
            }
            catch (System.Exception e)
            {
                Log.Warning("[Avir] Failed to inject the idle tailing node: " + e);
            }
        }

        private static bool AlreadyInjected(ThinkNode node)
        {
            if (node == null || node.subNodes == null) return false;

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                if (node.subNodes[i] is JobGiver_AvirTailingIdle) return true;
                if (AlreadyInjected(node.subNodes[i])) return true;
            }
            return false;
        }

        private static bool InsertBefore(ThinkNode node, string anchorTypeName)
        {
            if (node == null || node.subNodes == null) return false;

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                if (node.subNodes[i] != null && node.subNodes[i].GetType().Name == anchorTypeName)
                {
                    node.subNodes.Insert(i, new JobGiver_AvirTailingIdle());
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
