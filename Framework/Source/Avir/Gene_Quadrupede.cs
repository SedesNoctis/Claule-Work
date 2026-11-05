using RimWorld;
using Verse;

namespace QuadrupedeGene
{
    [DefOf]
    public static class QuadrupedeDefOf
    {
        /// The quadrupede gait now lives in the avian frame gene (the old Quadrupede gene was
        /// merged into it - see AvirGeneMerge.cs). Its conditional move-speed affecters are the
        /// ones QuadrupedeStance reads.
        public static GeneDef Avir_AvianFrame;
        public static HediffDef QuadrupedeManipulationPenalty;

        static QuadrupedeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(QuadrupedeDefOf));
        }
    }

    /// <summary>
    /// Gene class of Avir_AvianFrame. Grants the QuadrupedeManipulationPenalty hediff (the
    /// four-legged stance; despite the name it now costs work speed, not manipulation) the same
    /// reliable way the Flight gene's hediff is granted: added from code when the gene is
    /// present, removed when the gene is removed. The hediff then stays on the pawn permanently;
    /// its stage is switched between "upright" and "on all fours", never deleted.
    /// </summary>
    public class Gene_Quadrupede : Gene
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

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(QuadrupedeDefOf.QuadrupedeManipulationPenalty);
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

            if (!pawn.health.hediffSet.HasHediff(QuadrupedeDefOf.QuadrupedeManipulationPenalty))
            {
                pawn.health.AddHediff(QuadrupedeDefOf.QuadrupedeManipulationPenalty);
            }
        }
    }
}
