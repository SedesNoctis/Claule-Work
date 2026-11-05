using RimWorld;
using Verse;

namespace Avir.Traits
{
    public class CompProperties_BindLife : CompProperties_AbilityEffect
    {
        public HediffDef crowMarkedHediff;
        public HediffDef watchedOverHediff;
        public HediffDef watchingOverHediff;
        public HediffDef crowWatcherBondsHediff;
        public int maxBondsPerWatcher = 3;

        public CompProperties_BindLife()
        {
            this.compClass = typeof(CompAbilityEffect_BindLife);
        }
    }

    public class CompAbilityEffect_BindLife : CompAbilityEffect
    {
        private static ThingDef crowDef;
        private static bool defsResolved;

        private static void EnsureDefsResolved()
        {
            if (defsResolved) return;
            defsResolved = true;
            crowDef = DefDatabase<ThingDef>.GetNamedSilentFail("Crow");

            if (crowDef == null)
                Log.Message("[Avir] ThingDef 'Crow' not found - expected if Odyssey isn't owned. Avir_EyeOfTheCrow won't be granted (see Patch_EyeOfTheCrow_PostProcess).");
        }

        public new CompProperties_BindLife Props => (CompProperties_BindLife)this.props;

        private static bool IsLiveCrow(Pawn p) => p != null && !p.Dead && p.Spawned && p.def == crowDef;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages)) return false;

            EnsureDefsResolved();
            if (crowDef == null) return false;

            Pawn caster = this.parent.pawn;
            Pawn targetPawn = target.Thing as Pawn;
            if (targetPawn == null) return false;

            HediffComp_CrowMarked markComp = GetMarkComp(caster);
            Pawn markedCrow = markComp?.markedCrow;
            bool hasValidMark = markedCrow != null && IsLiveCrow(markedCrow);

            // Stale mark (crow died/despawned since marking) - treat as no mark, let them
            // re-mark. Cleanup happens for real in Apply.
            if (markComp != null && !hasValidMark) markComp = null;

            if (markComp == null)
            {
                // Stage 1: must target a crow.
                if (!IsLiveCrow(targetPawn))
                {
                    if (throwMessages) Messages.Message("Choose a crow first.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                return true;
            }
            else
            {
                // Stage 2: must target a person, not the already-marked crow, not already bonded.
                if (targetPawn == markedCrow || targetPawn.RaceProps.Animal)
                {
                    if (throwMessages) Messages.Message(markedCrow.LabelShortCap + " is already chosen - target a person now.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                if (targetPawn.health?.hediffSet != null && this.Props.watchedOverHediff != null
                    && targetPawn.health.hediffSet.HasHediff(this.Props.watchedOverHediff))
                {
                    if (throwMessages) Messages.Message(targetPawn.LabelShortCap + " is already watched over by another crow.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                HediffComp_CrowWatcherBonds bondsComp = GetBondsComp(caster, createIfMissing: false);
                if (bondsComp != null && bondsComp.Count >= this.Props.maxBondsPerWatcher)
                {
                    if (throwMessages) Messages.Message("Already watching over " + this.Props.maxBondsPerWatcher + " people through crows - none to spare.", caster, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                return true;
            }
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            EnsureDefsResolved();
            if (crowDef == null) return;

            Pawn caster = this.parent.pawn;
            Pawn targetPawn = target.Thing as Pawn;
            if (targetPawn == null) return;

            HediffComp_CrowMarked markComp = GetMarkComp(caster);
            Pawn markedCrow = markComp?.markedCrow;
            bool hasValidMark = markedCrow != null && IsLiveCrow(markedCrow);

            if (!hasValidMark)
            {
                // Stale mark cleanup, if any.
                if (markComp != null) RemoveMarkHediff(caster);

                // Stage 1: mark this crow.
                if (!IsLiveCrow(targetPawn)) return; // Valid() should have caught this already

                Hediff markHediff = HediffMaker.MakeHediff(this.Props.crowMarkedHediff, caster);
                caster.health.AddHediff(markHediff);
                HediffComp_CrowMarked newMarkComp = markHediff.TryGetComp<HediffComp_CrowMarked>();
                if (newMarkComp != null) newMarkComp.markedCrow = targetPawn;

                Messages.Message(targetPawn.LabelShortCap + " has agreed. Choose who it will watch over.", targetPawn, MessageTypeDefOf.PositiveEvent, false);
                return;
            }

            // Stage 2: complete the bond.
            HediffComp_CrowWatcherBonds bondsComp = GetBondsComp(caster, createIfMissing: true);
            if (bondsComp == null || !bondsComp.TryAddCrow(markedCrow, this.Props.maxBondsPerWatcher))
            {
                Messages.Message("Couldn't complete the bond.", caster, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (this.Props.watchedOverHediff != null && targetPawn.health?.hediffSet != null
                && !targetPawn.health.hediffSet.HasHediff(this.Props.watchedOverHediff))
            {
                Hediff watchedHediff = HediffMaker.MakeHediff(this.Props.watchedOverHediff, targetPawn);
                targetPawn.health.AddHediff(watchedHediff);
                HediffComp_WatchedOver watchedComp = watchedHediff.TryGetComp<HediffComp_WatchedOver>();
                if (watchedComp != null)
                {
                    watchedComp.crow = markedCrow;
                    watchedComp.crowWatcher = caster;
                }
            }

            if (this.Props.watchingOverHediff != null && markedCrow.health?.hediffSet != null)
            {
                Hediff watchingHediff = HediffMaker.MakeHediff(this.Props.watchingOverHediff, markedCrow);
                markedCrow.health.AddHediff(watchingHediff);
                HediffComp_WatchingOver watchingComp = watchingHediff.TryGetComp<HediffComp_WatchingOver>();
                if (watchingComp != null)
                {
                    watchingComp.watchedPawn = targetPawn;
                    watchingComp.crowWatcher = caster;
                }
            }

            RemoveMarkHediff(caster);

            Messages.Message(markedCrow.LabelShortCap + " now watches over " + targetPawn.LabelShortCap + ".", targetPawn, MessageTypeDefOf.PositiveEvent, false);
        }

        private HediffComp_CrowMarked GetMarkComp(Pawn caster)
        {
            if (this.Props.crowMarkedHediff == null || caster.health?.hediffSet == null) return null;
            Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(this.Props.crowMarkedHediff);
            return h?.TryGetComp<HediffComp_CrowMarked>();
        }

        private void RemoveMarkHediff(Pawn caster)
        {
            if (this.Props.crowMarkedHediff == null || caster.health?.hediffSet == null) return;
            Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(this.Props.crowMarkedHediff);
            if (h != null) caster.health.RemoveHediff(h);
        }

        private HediffComp_CrowWatcherBonds GetBondsComp(Pawn caster, bool createIfMissing)
        {
            if (this.Props.crowWatcherBondsHediff == null || caster.health?.hediffSet == null) return null;

            Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(this.Props.crowWatcherBondsHediff);
            if (h == null)
            {
                if (!createIfMissing) return null;
                h = HediffMaker.MakeHediff(this.Props.crowWatcherBondsHediff, caster);
                caster.health.AddHediff(h);
            }

            return h.TryGetComp<HediffComp_CrowWatcherBonds>();
        }
    }
}
