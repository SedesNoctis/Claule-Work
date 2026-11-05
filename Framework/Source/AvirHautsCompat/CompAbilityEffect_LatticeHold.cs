using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    public class CompProperties_LatticeHold : CompProperties_AbilityEffect
    {
        public HediffDef mechHediff;

        public CompProperties_LatticeHold()
        {
            this.compClass = typeof(CompAbilityEffect_LatticeHold);
        }
    }

    /// <summary>
    /// The half of Unbound that has to travel to a different pawn.
    ///
    /// WHY THIS EXISTS AT ALL. Repair energy is spent by the MECH, not by the Architect -
    /// JobDriver_RepairMech's repair tick does, from IL:
    ///
    ///     Mech.needs.energy.CurLevel -= Mech.GetStatValue(MechEnergyLossPerHP);
    ///
    /// so zeroing it means putting an offset on every mech, which a statOffsets block on the
    /// caster's own hediff cannot do. Hence one comp whose entire job is delivery.
    ///
    /// WHY IT IS NOT A HARMONY PATCH. It was going to be, matching Patch_ArchstructrorMechBoosts.
    /// Then StatWorker_MechEnergyLossPerHP turned out to override ValueToString and nothing
    /// else - GetValue and GetValueUnfinalized are untouched - so an ordinary hediff offset
    /// reaches the stat through the normal path and the patch would have been dead weight.
    /// Avir_LatticeHeld carries its own HediffCompProperties_Disappears, so nothing here has to
    /// track or clean up the duration either.
    ///
    /// SCOPE IS DELIBERATELY NARROWER THAN THE PASSIVE. Patch_ArchstructrorMechBoosts boosts
    /// every player mech on the map. This one uses mechanitor.OverseenPawns - the mechs this
    /// specific Architect actually answers for - because free repair is a much larger favour
    /// than +35% work speed and should not leak onto a second mechanitor's army.
    ///
    /// CONSEQUENCE WORTH KNOWING: OverseenPawns comes from Pawn.mechanitor, which is null on a
    /// pawn with no mechlink. An Architect who never took one still gets the reach and the
    /// speed from Avir_Unbound - lattice repair needs no implant, see the ability file - but
    /// gets no free repairs, because there is no list of mechs to give them to. That is a
    /// design consequence of "mechs under the Architect's control", not a bug. Mechs gestated
    /// after the cast are likewise not covered; the sweep happens once, on cast.
    /// </summary>
    public class CompAbilityEffect_LatticeHold : CompAbilityEffect
    {
        public new CompProperties_LatticeHold Props => (CompProperties_LatticeHold)this.props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            try
            {
                Pawn caster = this.parent?.pawn;
                if (caster == null) return;
                if (this.Props.mechHediff == null) return;
                if (caster.mechanitor == null) return;

                List<Pawn> mechs = caster.mechanitor.OverseenPawns;
                if (mechs == null) return;

                for (int i = 0; i < mechs.Count; i++)
                {
                    Pawn mech = mechs[i];
                    if (mech == null || mech.Dead) continue;
                    if (mech.health == null || mech.health.hediffSet == null) continue;

                    // Recast during an active Unbound restarts the clock rather than stacking a
                    // second copy - HediffComp_Disappears counts down per hediff instance, so two
                    // copies would mean two independent timers and an offset of -20.
                    Hediff existing = mech.health.hediffSet.GetFirstHediffOfDef(this.Props.mechHediff);
                    if (existing != null)
                    {
                        // GetComp, not TryGetComp - HediffWithComps declares GetComp<T>(); the
                        // TryGetComp spelling is the ThingCompUtility extension and does not
                        // apply to hediffs. SetDuration writes both disappearsAfterTicks and
                        // ticksToDisappear, which is what a clean restart wants.
                        HediffComp_Disappears disappears = (existing as HediffWithComps)?.GetComp<HediffComp_Disappears>();
                        if (disappears != null) disappears.SetDuration(DurationTicks());
                        continue;
                    }

                    mech.health.AddHediff(HediffMaker.MakeHediff(this.Props.mechHediff, mech));
                }
            }
            catch (Exception e)
            {
                Log.Warning("[AvirHautsCompat] Lattice hold failed to apply: " + e);
            }
        }

        /// <summary>
        /// Read back off the def rather than hardcoded, so changing disappearsAfterTicks in
        /// Hediffs_Avir_Lattice.xml is a one-place edit and the refresh path above cannot drift
        /// away from the initial-application path.
        ///
        /// THE SAME FIELD NAME MEANS TWO DIFFERENT TYPES AND THAT IS WHAT BROKE THE FIRST BUILD.
        /// HediffCompProperties_Disappears.disappearsAfterTicks is an IntRange (the def side, so
        /// XML can write a range). HediffComp_Disappears.disappearsAfterTicks is a plain int (the
        /// live side, already rolled). Reading the props field straight into an int is CS0029.
        /// RandomInRange is the conversion vanilla itself uses - HediffComp_Disappears.CompPostMake
        /// does exactly this.disappearsAfterTicks = Props.disappearsAfterTicks.RandomInRange - so
        /// matching it keeps a future range in the XML working instead of silently taking min.
        /// </summary>
        private int DurationTicks()
        {
            HediffCompProperties_Disappears props =
                this.Props.mechHediff.CompPropsFor(typeof(HediffComp_Disappears)) as HediffCompProperties_Disappears;
            return props != null ? props.disappearsAfterTicks.RandomInRange : 7500;
        }
    }
}
