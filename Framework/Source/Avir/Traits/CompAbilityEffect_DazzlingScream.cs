using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Traits
{
    public class CompProperties_DazzlingScream : CompProperties_AbilityEffect
    {
        public float targetRadius = 2.5f;
        public float casterRadius = 2.5f;

        public GeneDef fearbornGeneDef;

        public HediffDef ecstaticHuntHediff;
        public MentalStateDef ecstaticHuntMentalState;
        public float ecstaticHuntMentalBreakChance = 0.25f;

        public HediffDef screamMarkedHediff;

        public CompProperties_DazzlingScream()
        {
            this.compClass = typeof(CompAbilityEffect_DazzlingScream);
        }
    }

    public class CompAbilityEffect_DazzlingScream : CompAbilityEffect
    {
        public new CompProperties_DazzlingScream Props => (CompProperties_DazzlingScream)this.props;

        // Resolved once, lazily, at runtime rather than via XML cross-reference - a wrong name
        // in this list just gets skipped instead of throwing a load-time error. Fleeing always
        // has a confirmed-working option (PanicFlee); Daze doesn't have a confirmed exact name
        // yet, so it tries a few candidates and falls back to PanicFlee too if none hit.
        private static MentalStateDef fleeStateDef;
        private static MentalStateDef dazeStateDef;
        private static bool statesResolved;

        private static void EnsureMentalStatesResolved()
        {
            if (statesResolved) return;
            statesResolved = true;

            fleeStateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("PanicFlee");

            string[] dazeCandidates = { "WanderPsychotic", "Wander_Psychotic", "PsychoticWandering", "Wander_Psychotic_Extreme" };
            foreach (string name in dazeCandidates)
            {
                dazeStateDef = DefDatabase<MentalStateDef>.GetNamedSilentFail(name);
                if (dazeStateDef != null) break;
            }

            if (dazeStateDef == null)
            {
                Log.WarningOnce(
                    "[Avir] Couldn't find a Daze-type MentalStateDef by any known candidate name - Dazzling Scream will use Flee (PanicFlee) for everyone instead. Check your RimWorld version's real defName and add it to the candidate list in CompAbilityEffect_DazzlingScream.cs.",
                    0x4A51BC);
                dazeStateDef = fleeStateDef;
            }

            if (fleeStateDef == null)
            {
                Log.WarningOnce("[Avir] Couldn't find PanicFlee at all - Dazzling Scream's non-Fearborn branch will do nothing.", 0x4A51BD);
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages)) return false;

            Pawn targetPawn = target.Thing as Pawn;
            if (targetPawn != null && this.Props.fearbornGeneDef != null
                && targetPawn.genes != null && targetPawn.genes.HasActiveGene(this.Props.fearbornGeneDef))
            {
                if (throwMessages)
                {
                    Messages.Message(
                        targetPawn.LabelShortCap + " already answers the call - the scream can't be aimed at them directly. Cast it on the ground near them instead.",
                        targetPawn,
                        MessageTypeDefOf.RejectInput,
                        false);
                }
                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            EnsureMentalStatesResolved();

            Pawn caster = this.parent.pawn;
            Map map = caster.Map;
            if (map == null) return;

            IntVec3 targetCell = target.Cell.IsValid ? target.Cell : caster.Position;

            HashSet<Pawn> affected = new HashSet<Pawn>();
            CollectPawnsInRadius(targetCell, map, this.Props.targetRadius, affected);
            CollectPawnsInRadius(caster.Position, map, this.Props.casterRadius, affected);

            foreach (Pawn pawn in affected)
            {
                if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null) continue;
                ApplyToPawn(pawn);
            }
        }

        private static void CollectPawnsInRadius(IntVec3 center, Map map, float radius, HashSet<Pawn> into)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn p) into.Add(p);
                }
            }
        }

        private void ApplyToPawn(Pawn pawn)
        {
            bool isFearborn = this.Props.fearbornGeneDef != null
                               && pawn.genes != null
                               && pawn.genes.HasActiveGene(this.Props.fearbornGeneDef);

            if (isFearborn)
            {
                ApplyEcstaticHunt(pawn);
            }
            else
            {
                ApplyDazzled(pawn);
            }
        }

        private void ApplyEcstaticHunt(Pawn pawn)
        {
            if (this.Props.ecstaticHuntHediff == null) return;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(this.Props.ecstaticHuntHediff);
            if (existing != null)
            {
                HediffComp_Disappears disappears = existing.TryGetComp<HediffComp_Disappears>();
                if (disappears != null)
                {
                    HediffCompProperties_Disappears dp = disappears.props as HediffCompProperties_Disappears;
                    if (dp != null) disappears.ticksToDisappear = dp.disappearsAfterTicks.RandomInRange;
                }
            }
            else
            {
                Hediff hediff = HediffMaker.MakeHediff(this.Props.ecstaticHuntHediff, pawn);
                pawn.health.AddHediff(hediff);
            }

            if (this.Props.ecstaticHuntMentalState != null
                && Rand.Chance(this.Props.ecstaticHuntMentalBreakChance)
                && pawn.mindState?.mentalStateHandler != null
                && !pawn.InMentalState)
            {
                TryForceMentalStateWithTracking(pawn, this.Props.ecstaticHuntMentalState);
            }
        }

        private void ApplyDazzled(Pawn pawn)
        {
            if (pawn.mindState?.mentalStateHandler == null || pawn.InMentalState) return;

            MentalStateDef chosen;
            if (!pawn.RaceProps.Humanlike)
            {
                chosen = dazeStateDef;
            }
            else
            {
                chosen = Rand.Bool ? fleeStateDef : dazeStateDef;
            }

            if (chosen == null) return;

            TryForceMentalStateWithTracking(pawn, chosen);
        }

        private void TryForceMentalStateWithTracking(Pawn pawn, MentalStateDef stateDef)
        {
            bool started = pawn.mindState.mentalStateHandler.TryStartMentalState(
                stateDef,
                reason: "the dazzling scream",
                forceWake: true,
                forced: true);

            if (!started)
            {
                Log.WarningOnce(
                    "[Avir] Dazzling Scream rejected mental state " + stateDef.defName
                    + " on " + pawn.LabelShortCap + " (already breaking, ineligible race/state, or blocked by a worker check).",
                    pawn.thingIDNumber ^ 0x4A51B6);
                return;
            }

            if (pawn.Faction == Faction.OfPlayer)
            {
                Find.LetterStack.ReceiveLetter(
                    "Mental break: " + stateDef.LabelCap,
                    pawn.LabelShortCap + " was forced into a mental break (" + stateDef.LabelCap + ") by a dazzling scream.",
                    LetterDefOf.NegativeEvent,
                    new LookTargets(pawn));
            }

            if (this.Props.screamMarkedHediff == null) return;

            Hediff existingMark = pawn.health.hediffSet.GetFirstHediffOfDef(this.Props.screamMarkedHediff);
            if (existingMark == null)
            {
                Hediff mark = HediffMaker.MakeHediff(this.Props.screamMarkedHediff, pawn);
                pawn.health.AddHediff(mark);
            }
        }
    }
}
