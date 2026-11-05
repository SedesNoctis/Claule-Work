using RimWorld;
using Verse;

namespace KeenSensesGene
{
    [DefOf]
    public static class KeenSensesDefOf
    {
        public static HediffDef KeenSensesStance;

        static KeenSensesDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(KeenSensesDefOf));
        }
    }

    /// <summary>
    /// Grants the KeenSensesStance hediff and keeps it on the pawn permanently (same
    /// reliable pattern as the Flight and Quadrupede genes). The hediff's own comp only
    /// switches it between its light-states; it never deletes itself.
    /// </summary>
    public class Gene_KeenSenses : Gene
    {
        public override void PostAdd()
        {
            base.PostAdd();
            EnsureHediff();
        }

        public override void PostRemove()
        {
            base.PostRemove();
            if (pawn?.health == null)
            {
                return;
            }

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(KeenSensesDefOf.KeenSensesStance);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }
        }

        private void EnsureHediff()
        {
            if (pawn?.health == null)
            {
                return;
            }

            if (!pawn.health.hediffSet.HasHediff(KeenSensesDefOf.KeenSensesStance))
            {
                pawn.health.AddHediff(KeenSensesDefOf.KeenSensesStance);
            }
        }
    }
}
