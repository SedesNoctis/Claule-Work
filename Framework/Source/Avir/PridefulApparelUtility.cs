using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Stage indices for Avir_PridefulApparelQuality. Must stay in sync with the <stages> order
    /// in Defs/ThoughtDefs/Thoughts_Avir_Prideful.xml - index 0 is the first &lt;li&gt;, etc.
    /// </summary>
    public static class PridefulApparelStage
    {
        public const int Awful = 0;
        public const int Poor = 1;
        public const int Normal = 2;
        public const int Good = 3;
        public const int Excellent = 4;
        public const int Masterwork = 5;
        public const int Legendary = 6;
        public const int TaintedOrTattered = 7;
    }

    public static class PridefulApparelUtility
    {
        /// <summary>
        /// Hit-point fraction at or below which a garment counts as "tattered" for a Prideful
        /// pawn's mood. RimWorld doesn't expose a public "IsTattered" flag on Apparel, so this
        /// is our own threshold.
        /// </summary>
        public const float TatteredHitPointsFraction = 0.50f;

        public static bool IsPrideful(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            bool hasTrait = pawn.story != null && pawn.story.traits != null
                            && pawn.story.traits.HasTrait(AvirDefOf.Avir_PridefulTrait);
            bool hasGene = pawn.genes != null && pawn.genes.HasActiveGene(AvirDefOf.Avir_Prideful);
            return hasTrait || hasGene;
        }

        public static bool IsTattered(Apparel apparel)
        {
            return apparel != null && apparel.MaxHitPoints > 0
                   && (float)apparel.HitPoints / apparel.MaxHitPoints <= TatteredHitPointsFraction;
        }

        public static bool IsTainted(Apparel apparel)
        {
            return apparel != null && apparel.WornByCorpse;
        }

        /// <summary>
        /// Tainted/tattered overrides quality entirely - a legendary coat stripped off a corpse
        /// still reads as stage 7, not stage 6. Otherwise, maps QualityCategory straight across.
        /// </summary>
        public static int GetStageIndex(Apparel apparel)
        {
            if (IsTainted(apparel) || IsTattered(apparel))
            {
                return PridefulApparelStage.TaintedOrTattered;
            }

            if (!apparel.TryGetQuality(out QualityCategory quality))
            {
                return PridefulApparelStage.Normal;
            }

            switch (quality)
            {
                case QualityCategory.Awful: return PridefulApparelStage.Awful;
                case QualityCategory.Poor: return PridefulApparelStage.Poor;
                case QualityCategory.Good: return PridefulApparelStage.Good;
                case QualityCategory.Excellent: return PridefulApparelStage.Excellent;
                case QualityCategory.Masterwork: return PridefulApparelStage.Masterwork;
                case QualityCategory.Legendary: return PridefulApparelStage.Legendary;
                default: return PridefulApparelStage.Normal;
            }
        }
    }
}
