using Verse;

namespace AvirHautsCompat
{
    public class HediffCompProperties_Soulmarked : HediffCompProperties
    {
        public HediffCompProperties_Soulmarked()
        {
            this.compClass = typeof(HediffComp_Soulmarked);
        }
    }

    /// <summary>
    /// On the marked slave/prisoner. Dormant (mechanoid == null) between the first cast and
    /// the second - see HediffComp_SoulmarkPending for that window. Once bound, holds both
    /// references for the lifetime of the bond.
    ///
    /// "If slave or prisoner becomes colonist he loses hediff" - checked every 250 ticks
    /// rather than hooked to a specific recruitment event, since there are several different
    /// ways a slave/prisoner can end up a full colonist (interaction recruit, Ideology rituals,
    /// debug tools, other mods' recruitment systems) and this is guaranteed to catch all of
    /// them without chasing each one individually.
    /// </summary>
    public class HediffComp_Soulmarked : HediffComp
    {
        public Pawn architect;
        public Pawn mechanoid;

        private int lastStatusCheckTick = -1;

        public override string CompTipStringExtra
        {
            get
            {
                if (this.mechanoid == null) return null;
                return "Soulmark-bound to: " + this.mechanoid.LabelShortCap;
            }
        }

        public void Bind(Pawn mechanoidIn, Pawn architectIn)
        {
            this.mechanoid = mechanoidIn;
            this.architect = architectIn;
        }

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);

            Pawn victim = this.parent?.pawn;
            if (victim == null) return;

            if (this.lastStatusCheckTick >= 0 && Find.TickManager.TicksGame - this.lastStatusCheckTick < 250) return;
            this.lastStatusCheckTick = Find.TickManager.TicksGame;

            bool stillEligible = victim.IsSlave || victim.IsPrisoner;
            if (!stillEligible)
            {
                Sever(removeSelf: true);
            }
        }

        /// <summary>
        /// Tears the bond down from the victim's side: unlinks the mechanoid, removes this
        /// pawn from the Architect's bond tracker, and (optionally) removes this hediff
        /// itself. Called on recruitment, on the victim's death (Patch_MechanoidBond_Death),
        /// and when a resurrection attempt fails permanently.
        /// </summary>
        public void Sever(bool removeSelf)
        {
            Pawn victim = this.parent?.pawn;

            if (this.mechanoid != null && !this.mechanoid.Destroyed)
            {
                HediffDef linkDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_MechanoidLink");
                Hediff linkHediff = linkDef != null ? this.mechanoid.health?.hediffSet?.GetFirstHediffOfDef(linkDef) : null;
                if (linkHediff != null) this.mechanoid.health.RemoveHediff(linkHediff);
            }

            if (this.architect != null && !this.architect.Destroyed)
            {
                HediffDef bondsDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ArchstructrorBonds");
                Hediff bondsHediff = bondsDef != null ? this.architect.health?.hediffSet?.GetFirstHediffOfDef(bondsDef) : null;
                HediffComp_ArchstructrorBonds bondsComp = bondsHediff?.TryGetComp<HediffComp_ArchstructrorBonds>();
                bondsComp?.RemoveMechanoid(this.mechanoid);
            }

            this.mechanoid = null;
            this.architect = null;

            if (removeSelf && victim?.health != null && this.parent != null)
            {
                victim.health.RemoveHediff(this.parent);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref this.architect, "soulmarkArchitect");
            Scribe_References.Look(ref this.mechanoid, "soulmarkMechanoid");
        }
    }
}
