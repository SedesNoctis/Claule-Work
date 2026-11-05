using System.Collections.Generic;
using Avir;
using RimWorld;
using Verse;

namespace Avir.Traits
{
    public class CompProperties_CourtOfShadows : CompProperties_AbilityEffect
    {
        public float arcaneHungerCost = 0.15f;
        public float radius = 1.5f; // 3x3
        public HediffDef livingShadowHediff;
        public int durationTicksOverride = 12500; // 5 hours - overrides Living Shadow's own 3h XML default for this cast only

        public CompProperties_CourtOfShadows()
        {
            this.compClass = typeof(CompAbilityEffect_CourtOfShadows);
        }
    }

    /// <summary>
    /// Applies the existing Avir_LivingShadow hediff (unchanged - same one Shadowstride grants)
    /// to every allied pawn in a 3x3 around the caster, self included. Uses Gene_ArcaneHunger's
    /// own real static helpers (HasAtLeast / Spend) rather than manual Value arithmetic - found
    /// via the Shadowstride reference bundle, not something this compat assembly discovered
    /// itself before now.
    /// </summary>
    public class CompAbilityEffect_CourtOfShadows : CompAbilityEffect
    {
        public new CompProperties_CourtOfShadows Props => (CompProperties_CourtOfShadows)this.props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages)) return false;

            Pawn caster = this.parent.pawn;
            if (!Gene_ArcaneHunger.HasAtLeast(caster, this.Props.arcaneHungerCost))
            {
                if (throwMessages)
                {
                    Messages.Message(
                        caster.LabelShortCap + " doesn't have enough arcane hunger to cast this.",
                        caster, MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = this.parent.pawn;
            Map map = caster.Map;
            if (map == null || this.Props.livingShadowHediff == null) return;

            if (!Gene_ArcaneHunger.Spend(caster, this.Props.arcaneHungerCost)) return;

            HashSet<Pawn> affected = new HashSet<Pawn>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(caster.Position, this.Props.radius, true))
            {
                if (!cell.InBounds(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn p && p.Faction == caster.Faction && !p.Dead)
                    {
                        affected.Add(p);
                    }
                }
            }

            foreach (Pawn pawn in affected)
            {
                ApplyLivingShadow(pawn);
            }
        }

        private void ApplyLivingShadow(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null) return;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(this.Props.livingShadowHediff);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(this.Props.livingShadowHediff, pawn);
                pawn.health.AddHediff(hediff);
            }

            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = this.Props.durationTicksOverride;
            }
        }
    }
}
