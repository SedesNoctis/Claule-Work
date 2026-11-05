using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Reusable passive gene: grants the hediff named in its HediffGrantExtension when the
    /// gene is added and removes it when the gene is removed. Same reliable pattern as the
    /// Fearborn / Quadrupede gene classes, but reads the target hediff from XML so one class
    /// serves any number of genes.
    /// </summary>
    public class Gene_GrantHediff : Gene
    {
        private HediffDef HediffToGrant => def.GetModExtension<HediffGrantExtension>()?.hediffDef;

        public override void PostAdd()
        {
            base.PostAdd();

            HediffDef hediff = HediffToGrant;
            if (hediff == null || pawn?.health == null)
            {
                return;
            }

            if (!pawn.health.hediffSet.HasHediff(hediff))
            {
                pawn.health.AddHediff(hediff);
            }
        }

        public override void PostRemove()
        {
            base.PostRemove();

            HediffDef hediff = HediffToGrant;
            if (hediff == null || pawn?.health == null)
            {
                return;
            }

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediff);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }
        }
    }
}
