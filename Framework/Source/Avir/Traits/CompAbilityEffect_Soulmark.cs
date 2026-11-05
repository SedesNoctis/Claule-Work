using RimWorld;
using Verse;

namespace Avir.Traits
{
    public class CompProperties_Soulmark : CompProperties_AbilityEffect
    {
        public HediffDef soulmarkPendingHediff;
        public HediffDef soulmarkedHediff;
        public HediffDef mechanoidLinkHediff;
        public HediffDef archstructrorBondsHediff;
        public int maxBondsPerArchitect = 7;

        public CompProperties_Soulmark()
        {
            this.compClass = typeof(CompAbilityEffect_Soulmark);
        }
    }

    /// <summary>
    /// Mirrors CompAbilityEffect_BindLife's two-cast structure, but in the opposite order:
    /// here the PERSON is chosen first (stage 1) and the MECHANOID second (stage 2), since
    /// the spec has the victim receiving the mark immediately and the Architect racing a 24h
    /// clock to pick a construct for it, rather than BindLife's "pick the crow, then pick who
    /// it watches" ordering.
    /// </summary>
    public class CompAbilityEffect_Soulmark : CompAbilityEffect
    {
        public new CompProperties_Soulmark Props => (CompProperties_Soulmark)this.props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages)) return false;

            Pawn caster = this.parent.pawn;
            Pawn targetPawn = target.Thing as Pawn;
            if (targetPawn == null || targetPawn == caster) return false;

            HediffComp_SoulmarkPending pendingComp = GetPendingComp(caster);

            if (pendingComp == null)
            {
                // Stage 1: must target a slave or prisoner - never a colonist, never an
                // animal or mechanoid.
                if (targetPawn.RaceProps == null || !targetPawn.RaceProps.Humanlike)
                {
                    if (throwMessages) Messages.Message("Soulmark needs a person for its first cast.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                if (!targetPawn.IsSlave && !targetPawn.IsPrisoner)
                {
                    if (throwMessages) Messages.Message("Soulmark can only be cast on a slave or prisoner - never a colonist.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                if (this.Props.soulmarkedHediff != null && targetPawn.health?.hediffSet != null
                    && targetPawn.health.hediffSet.HasHediff(this.Props.soulmarkedHediff))
                {
                    if (throwMessages) Messages.Message(targetPawn.LabelShortCap + " is already soulmarked.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                return true;
            }
            else
            {
                // Stage 2: must target a friendly, unbonded mechanoid.
                if (targetPawn.RaceProps == null || !targetPawn.RaceProps.IsMechanoid)
                {
                    if (throwMessages) Messages.Message(pendingComp.victim.LabelShortCap + " is already marked - choose a mechanoid to bind it to now.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                if (targetPawn.Faction != Faction.OfPlayer)
                {
                    if (throwMessages) Messages.Message("Only a construct under this colony's control can be soulmarked.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                if (this.Props.mechanoidLinkHediff != null && targetPawn.health?.hediffSet != null
                    && targetPawn.health.hediffSet.HasHediff(this.Props.mechanoidLinkHediff))
                {
                    if (throwMessages) Messages.Message(targetPawn.LabelShortCap + " is already soulmark-bound to someone else.", targetPawn, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                HediffComp_ArchstructrorBonds bondsComp = GetBondsComp(caster, createIfMissing: false);
                if (bondsComp != null && bondsComp.Count >= Avir.Artifactorics.SpherePassives.SoulmarkBonds(caster))
                {
                    if (throwMessages) Messages.Message("Already soulmark-bound to " + Avir.Artifactorics.SpherePassives.SoulmarkBonds(caster) + " constructs - none to spare.", caster, MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                return true;
            }
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = this.parent.pawn;
            Pawn targetPawn = target.Thing as Pawn;
            if (targetPawn == null) return;

            HediffComp_SoulmarkPending pendingComp = GetPendingComp(caster);

            if (pendingComp == null)
            {
                // Stage 1: mark this person, start the clock.
                if (!targetPawn.IsSlave && !targetPawn.IsPrisoner) return; // Valid() should have caught this already

                Hediff pendingHediff = HediffMaker.MakeHediff(this.Props.soulmarkPendingHediff, caster);
                caster.health.AddHediff(pendingHediff);
                HediffComp_SoulmarkPending newPendingComp = pendingHediff.TryGetComp<HediffComp_SoulmarkPending>();
                if (newPendingComp != null) newPendingComp.victim = targetPawn;

                if (this.Props.soulmarkedHediff != null && targetPawn.health?.hediffSet != null
                    && !targetPawn.health.hediffSet.HasHediff(this.Props.soulmarkedHediff))
                {
                    Hediff soulmarkedHediff = HediffMaker.MakeHediff(this.Props.soulmarkedHediff, targetPawn);
                    targetPawn.health.AddHediff(soulmarkedHediff);
                }

                Messages.Message(targetPawn.LabelShortCap + " has been soulmarked. A mechanoid must be chosen within a day, or the mark fades.", targetPawn, MessageTypeDefOf.PositiveEvent, false);
                return;
            }

            // Stage 2: complete the bond.
            Pawn victim = pendingComp.victim;
            if (victim == null || victim.Destroyed)
            {
                Messages.Message("The marked person is gone - the bond can't be completed.", caster, MessageTypeDefOf.RejectInput, false);
                RemovePendingHediff(caster);
                return;
            }

            HediffComp_ArchstructrorBonds bondsComp = GetBondsComp(caster, createIfMissing: true);
            if (bondsComp == null || !bondsComp.TryAddMechanoid(targetPawn, Avir.Artifactorics.SpherePassives.SoulmarkBonds(caster)))
            {
                Messages.Message("Couldn't complete the bond.", caster, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (this.Props.mechanoidLinkHediff != null && targetPawn.health?.hediffSet != null)
            {
                Hediff linkHediff = HediffMaker.MakeHediff(this.Props.mechanoidLinkHediff, targetPawn);
                targetPawn.health.AddHediff(linkHediff);
                HediffComp_MechanoidLink linkComp = linkHediff.TryGetComp<HediffComp_MechanoidLink>();
                if (linkComp != null)
                {
                    linkComp.victim = victim;
                    linkComp.architect = caster;
                }
            }

            if (this.Props.soulmarkedHediff != null && victim.health?.hediffSet != null)
            {
                Hediff soulmarkedHediff = victim.health.hediffSet.GetFirstHediffOfDef(this.Props.soulmarkedHediff);
                HediffComp_Soulmarked soulmarkedComp = soulmarkedHediff?.TryGetComp<HediffComp_Soulmarked>();
                soulmarkedComp?.Bind(targetPawn, caster);
            }

            RemovePendingHediff(caster);

            Messages.Message(targetPawn.LabelShortCap + " is now soulmark-bound to " + victim.LabelShortCap + ".", targetPawn, MessageTypeDefOf.PositiveEvent, false);
        }

        private HediffComp_SoulmarkPending GetPendingComp(Pawn caster)
        {
            if (this.Props.soulmarkPendingHediff == null || caster.health?.hediffSet == null) return null;
            Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(this.Props.soulmarkPendingHediff);
            return h?.TryGetComp<HediffComp_SoulmarkPending>();
        }

        private void RemovePendingHediff(Pawn caster)
        {
            if (this.Props.soulmarkPendingHediff == null || caster.health?.hediffSet == null) return;
            Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(this.Props.soulmarkPendingHediff);
            if (h != null) caster.health.RemoveHediff(h);
        }

        private HediffComp_ArchstructrorBonds GetBondsComp(Pawn caster, bool createIfMissing)
        {
            if (this.Props.archstructrorBondsHediff == null || caster.health?.hediffSet == null) return null;

            Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(this.Props.archstructrorBondsHediff);
            if (h == null)
            {
                if (!createIfMissing) return null;
                h = HediffMaker.MakeHediff(this.Props.archstructrorBondsHediff, caster);
                caster.health.AddHediff(h);
            }

            return h.TryGetComp<HediffComp_ArchstructrorBonds>();
        }
    }
}
