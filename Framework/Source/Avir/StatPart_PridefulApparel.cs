using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Shifts Psychic sensitivity by a per-garment amount based on each worn item's quality,
    /// for pawns who have the Prideful trait OR the Prideful gene. Checking the gene too means
    /// it works even when a dev-added gene didn't retro-grant its forced trait.
    ///   Awful -20%, Poor -10%, Normal 0, Good +5%, Excellent +10%, Masterwork +15%, Legendary +20%
    /// Summed across all worn apparel. Evaluated on demand (no ticking).
    /// </summary>
    public class StatPart_PridefulApparel : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetOffset(req, out float offset))
            {
                val += offset;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            // Show the line whenever the pawn qualifies, even at +0%, so it's obvious the part is live.
            if (TryGetOffset(req, out float offset))
            {
                string sign = offset >= 0f ? "+" : "";
                return "Prideful (apparel quality): " + sign + offset.ToStringPercent();
            }
            return null;
        }

        private bool TryGetOffset(StatRequest req, out float offset)
        {
            offset = 0f;

            Pawn pawn = req.Thing as Pawn;
            if (pawn == null || pawn.apparel == null)
            {
                return false;
            }

            bool hasTrait = pawn.story != null && pawn.story.traits != null
                            && pawn.story.traits.HasTrait(AvirDefOf.Avir_PridefulTrait);
            bool hasGene = pawn.genes != null && pawn.genes.HasActiveGene(AvirDefOf.Avir_Prideful);
            if (!hasTrait && !hasGene)
            {
                return false;
            }

            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].TryGetQuality(out QualityCategory q))
                {
                    offset += QualityOffset(q);
                }
            }
            return true;
        }

        private static float QualityOffset(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return -0.40f;
                case QualityCategory.Poor: return -0.20f;
                case QualityCategory.Good: return 0.05f;
                case QualityCategory.Excellent: return 0.10f;
                case QualityCategory.Masterwork: return 0.15f;
                case QualityCategory.Legendary: return 0.20f;
                default: return 0f; // Normal
            }
        }
    }
}
