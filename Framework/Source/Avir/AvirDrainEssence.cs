using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir
{
    [DefOf]
    public static class AvirDrainDefOf
    {
        public static HediffDef Avir_EssenceDrained;
        public static JobDef Avir_DrainEssenceJob;
        public static PrisonerInteractionModeDef Avir_EssenceFarm;

        [MayRequireBiotech] public static PrisonerInteractionModeDef HemogenFarm;

        static AvirDrainDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirDrainDefOf));
        }
    }

    public static class AvirDrain
    {
        /// How much of the arcane hunger a non-lethal drain restores.
        public const float HungerFill = 0.10f;

        /// Fallback only. The real threshold is the marker the player drags on the hunger gizmo.
        public const float AutoFeedThreshold = 0.45f;

        public static bool IsDrained(Pawn pawn)
        {
            return pawn != null
                && pawn.health != null
                && pawn.health.hediffSet.HasHediff(AvirDrainDefOf.Avir_EssenceDrained);
        }

        /// <summary>Can this creature be drained right now? Alive, not already drained, not an Avir.</summary>
        public static bool CanDrain(Pawn victim)
        {
            if (victim == null || victim.Dead || victim.health == null) return false;
            if (IsDrained(victim)) return false;
            if (victim.RaceProps != null && victim.RaceProps.IsMechanoid) return false;

            // Nothing left to take. Surgery already emptied them, and until that wears off there
            // is no essence in there to drink - so the ability must not pay out either. Without
            // this check a husk with zero psychic sensitivity fed an Avir exactly as well as a
            // radiant one, because the non-lethal drain pays a flat amount.
            if (AvirEssenceExtractDefOf.Avir_EssenceExtracted != null
                && victim.health.hediffSet.HasHediff(AvirEssenceExtractDefOf.Avir_EssenceExtracted))
                return false;

            // An Avir does not feed on its own kind.
            if (victim.genes != null && victim.genes.HasActiveGene(AvirNameDefOf.Fearborn)) return false;

            // Nor on an illithid. Whatever they run on, it is not what Avir drink - and the two
            // species eating each other would be a loop rather than a food chain. See
            // AvirIllithidGuard; a no-op when the illithid mod is absent.
            if (AvirIllithidGuard.IsIllithid(victim)) return false;

            return true;
        }

        public static void Drain(Pawn caster, Pawn victim)
        {
            if (caster == null || victim == null) return;

            victim.health.AddHediff(AvirDrainDefOf.Avir_EssenceDrained);
            Gene_ArcaneHunger.Feed(caster, Gene_ArcaneHunger.FillPerDrain);

            Messages.Message(
                caster.LabelShortCap + " drained the essence from " + victim.LabelShortCap + ".",
                caster, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    /// <summary>While drained, psychic sensitivity is held at 75% of what it would be, at most.</summary>
    public class StatPart_AvirDrained : StatPart
    {
        private const float Cap = 0.75f;

        public override void TransformValue(StatRequest req, ref float val)
        {
            Pawn pawn = req.Thing as Pawn;
            if (!AvirDrain.IsDrained(pawn)) return;

            val = Mathf.Min(val, Cap);
        }

        public override string ExplanationPart(StatRequest req)
        {
            Pawn pawn = req.Thing as Pawn;
            if (!AvirDrain.IsDrained(pawn)) return null;

            return "Essence drained: capped at " + Cap.ToStringPercent();
        }
    }

    [StaticConstructorOnStartup]
    public static class AvirDrainStatInjector
    {
        static AvirDrainStatInjector()
        {
            StatDef stat = StatDefOf.PsychicSensitivity;
            if (stat == null) return;

            if (stat.parts == null) stat.parts = new List<StatPart>();

            for (int i = 0; i < stat.parts.Count; i++)
            {
                if (stat.parts[i] is StatPart_AvirDrained) return;
            }

            stat.parts.Add(new StatPart_AvirDrained { parentStat = stat });
        }
    }

    // ================= THE ABILITY =================

    public class CompProperties_AvirDrainEssence : CompProperties_AbilityEffect
    {
        public CompProperties_AvirDrainEssence()
        {
            compClass = typeof(CompAbilityEffect_AvirDrainEssence);
        }
    }

    public class CompAbilityEffect_AvirDrainEssence : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn victim = target.Pawn;
            if (victim == null || victim == parent.pawn) return false;

            if (!AvirDrain.CanDrain(victim))
            {
                if (throwMessages)
                {
                    string why = AvirDrain.IsDrained(victim)
                        ? victim.LabelShortCap + " has nothing left to give yet."
                        : victim.LabelShortCap + " cannot be drained.";
                    Messages.Message(why, victim, MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            AvirDrain.Drain(parent.pawn, target.Pawn);
        }
    }

    // ================= PRISONER FARMING =================

    /// <summary>
    /// Walks an Avir over to a prisoner set to be farmed for essence and drains them. Modelled on
    /// the sanguophage bloodfeeding job, so it feels native in the prisoner tab.
    /// </summary>
    public class JobDriver_AvirDrainEssence : JobDriver
    {
        private Pawn Victim { get { return (Pawn)job.targetA.Thing; } }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !AvirDrain.CanDrain(Victim));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);

            Toil drain = Toils_General.Wait(240, TargetIndex.None);
            drain.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            drain.WithProgressBarToilDelay(TargetIndex.A);
            drain.handlingFacing = true;
            drain.tickAction = delegate { pawn.rotationTracker.FaceTarget(Victim); };
            yield return drain;

            yield return Toils_General.Do(delegate { AvirDrain.Drain(pawn, Victim); });
        }
    }

    /// <summary>
    /// A hungry Avir goes and feeds by itself. Three tiers of target, in order of preference:
    ///
    ///   1. PRISONERS flagged for essence farming (or the vanilla hemogen farm, kept so anyone
    ///      already using that checkbox is not broken). Unchanged behaviour.
    ///   2. DOWNED HOSTILES anywhere on the map. Always allowed - they are already enemies and
    ///      the drain is not lethal. Note this leaves Avir_EssenceDrained on them, so a raider
    ///      you meant to recruit comes out with capped psychic sensitivity for a while.
    ///   3. WILD ANIMALS, but only if the player ticked "hunt wild animals" on the hunger gizmo.
    ///      Faction-owned animals - livestock, pets, bonded war animals - are never eligible, no
    ///      matter what the toggle says.
    ///
    /// Cost control: everything below the first line only runs for an Avir that is ALREADY below
    /// its feed threshold, which is rare. The wide map scan is additionally throttled so a
    /// starving Avir that cannot find anything is not re-scanning every think tick.
    /// </summary>
    public class JobGiver_AvirDrainEssence : ThinkNode_JobGiver
    {
        /// Beyond this the Avir will not walk for a meal. Squared, so no sqrt in the loop.
        private const float MaxSearchDistSquared = 60f * 60f;

        /// How often the map-wide scan may run per pawn. Prisoners are a short list and are
        /// checked every time; the expensive pass is throttled to this.
        private const int WideScanInterval = 120;

        protected override Job TryGiveJob(Pawn pawn)
        {
            Gene_ArcaneHunger hunger = ArcaneHungerCache.For(pawn);
            if (hunger == null) return null;
            // Feed when below the line the player set on the gizmo, exactly like hemogen.
            if (hunger.Value >= hunger.FeedBelow) return null;
            if (pawn.Map == null || !pawn.IsColonist) return null;
            if (pawn.Drafted) return null;

            // --- Tier 1: flagged prisoners. Cheap list, always checked. ---
            Pawn best = null;
            float bestDist = float.MaxValue;

            List<Pawn> prisoners = pawn.Map.mapPawns.PrisonersOfColonySpawned;
            for (int i = 0; i < prisoners.Count; i++)
            {
                Pawn p = prisoners[i];
                if (!IsFarmed(p) || !AvirDrain.CanDrain(p)) continue;
                if (!pawn.CanReserveAndReach(p, PathEndMode.Touch, Danger.Deadly)) continue;

                float d = pawn.Position.DistanceToSquared(p.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }

            if (best != null) return JobMaker.MakeJob(AvirDrainDefOf.Avir_DrainEssenceJob, best);

            // --- Tiers 2 and 3: one throttled pass over the map. ---
            if (!pawn.IsHashIntervalTick(WideScanInterval)) return null;

            IReadOnlyList<Pawn> all = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p == pawn) continue;

                float d = pawn.Position.DistanceToSquared(p.Position);
                if (d >= MaxSearchDistSquared || d >= bestDist) continue;

                if (!IsOpportunisticPrey(pawn, p, hunger)) continue;
                if (!pawn.CanReserveAndReach(p, PathEndMode.Touch, Danger.Deadly)) continue;

                bestDist = d;
                best = p;
            }

            if (best == null) return null;
            return JobMaker.MakeJob(AvirDrainDefOf.Avir_DrainEssenceJob, best);
        }

        /// <summary>Tier 2 and 3 eligibility. Deliberately conservative.</summary>
        private static bool IsOpportunisticPrey(Pawn hunter, Pawn candidate, Gene_ArcaneHunger hunger)
        {
            if (!AvirDrain.CanDrain(candidate)) return false;

            // Never touch anything the player owns or is keeping. Prisoners were already handled
            // above and are only valid there when explicitly flagged for farming.
            if (candidate.IsPrisoner) return false;

            bool isAnimal = candidate.RaceProps != null && !candidate.RaceProps.Humanlike;

            if (isAnimal)
            {
                if (!hunger.wildFeedingAllowed) return false;
                // Wild means unowned. Livestock, pets, bonded war animals and any other faction's
                // animals all have a Faction and are excluded here.
                if (candidate.Faction != null) return false;
                if (candidate.relations != null && candidate.relations.DirectRelations != null
                    && candidate.relations.DirectRelations.Count > 0) return false;
                // Do not pick a fight with something that is going to fight back.
                if (candidate.InAggroMentalState) return false;
                return true;
            }

            // Humanlike: only if already down and hostile to us.
            if (!candidate.Downed) return false;
            if (candidate.Faction == null) return false;
            if (!candidate.HostileTo(hunter)) return false;

            return true;
        }

        private static bool IsFarmed(Pawn prisoner)
        {
            if (prisoner == null || prisoner.guest == null) return false;

            // ASKED THROUGH AvirPrisonerInteraction, NOT ExclusiveInteractionMode.
            //
            // Essence feeding is a checkbox now, not a radio choice, and a checkbox is never the
            // exclusive mode - the direct comparison this used to make would be false forever.
            //
            // The hemogen-farm fallback below was ALREADY in that state: hemogen farm has been
            // non-exclusive since Biotech shipped, so "keep working for players who already have
            // prisoners set to the vanilla blood farm" has never once returned true. It works now.
            if (AvirPrisonerInteraction.IsSetTo(prisoner, AvirDrainDefOf.Avir_EssenceFarm)) return true;

            return AvirPrisonerInteraction.IsSetTo(prisoner, AvirDrainDefOf.HemogenFarm);
        }
    }

    /// <summary>
    /// Slots the essence-feeding job giver into the humanlike think tree, just under the top, so a
    /// starving Avir prioritises it. Done in code rather than by an XML think-tree patch because
    /// the vanilla tree's shape shifts between versions and an xpath into it breaks silently.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirThinkTreeInjector
    {
        static AvirThinkTreeInjector()
        {
            try
            {
                ThinkTreeDef tree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("Humanlike");
                if (tree == null || tree.thinkRoot == null || tree.thinkRoot.subNodes == null)
                {
                    Log.Warning("[Avir] Humanlike think tree not found; Avir will not feed on prisoners automatically.");
                    return;
                }

                for (int i = 0; i < tree.thinkRoot.subNodes.Count; i++)
                {
                    if (tree.thinkRoot.subNodes[i] is JobGiver_AvirDrainEssence) return;
                }

                // Index 1: after the top-level "burning / drafted" checks, before ordinary work.
                int at = Mathf.Min(1, tree.thinkRoot.subNodes.Count);

                // Order matters. Drinking a bottle you already own is inserted FIRST so it is
                // evaluated first: an Avir should empty the larder before it goes hunting. The
                // extract node is a no-op unless the player ticked its permission box.
                tree.thinkRoot.subNodes.Insert(at, new JobGiver_AvirDrainEssence());
                tree.thinkRoot.subNodes.Insert(at, new JobGiver_AvirGetEssenceExtract());
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Failed to inject the essence-feeding think node: " + e);
            }
        }
    }
}
