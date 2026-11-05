using RimWorld;
using Verse;

namespace Avir
{
    public static class AvirQualityUtility
    {
        public static QualityCategory GetQuality(Thing thing)
        {
            CompQuality comp = thing?.TryGetComp<CompQuality>();
            return comp != null ? comp.Quality : QualityCategory.Normal;
        }

        /// <summary>Arcane Hunger restored, as a 0-1 fraction fed into Gene_ArcaneHunger.Feed.</summary>
        public static float ArcaneHungerFor(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return 0.05f;
                case QualityCategory.Poor: return 0.10f;
                case QualityCategory.Normal: return 0.20f;
                case QualityCategory.Good: return 0.30f;
                case QualityCategory.Excellent: return 0.40f;
                case QualityCategory.Masterwork: return 0.60f;
                case QualityCategory.Legendary: return 0.80f;
                default: return 0.20f;
            }
        }

        /// <summary>
        /// Multiplier on the Psychic Tea-like stat boost (applied to how far each stat sits above
        /// its neutral baseline, not to the raw factor/offset value itself).
        /// </summary>
        public static float PsychicEffectMultiplierFor(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return 0.2f;
                case QualityCategory.Poor: return 0.6f;
                case QualityCategory.Normal: return 1.0f;
                case QualityCategory.Good: return 1.2f;
                case QualityCategory.Excellent: return 1.5f;
                case QualityCategory.Masterwork: return 2.0f;
                case QualityCategory.Legendary: return 3.0f;
                default: return 1.0f;
            }
        }

        /// <summary>
        /// HediffStage.restFallFactor - a -5% (Awful) to -40% (Legendary) reduction in how fast
        /// the Rest need falls, expressed as the multiplier itself (0.95 down to 0.60).
        /// </summary>
        public static float RestFallFactorFor(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return 0.95f;
                case QualityCategory.Poor: return 0.90f;
                case QualityCategory.Normal: return 0.85f;
                case QualityCategory.Good: return 0.80f;
                case QualityCategory.Excellent: return 0.75f;
                case QualityCategory.Masterwork: return 0.70f;
                case QualityCategory.Legendary: return 0.60f;
                default: return 0.85f;
            }
        }

        /// <summary>
        /// Severity added to Avir_ShadowsongEssenceTolerance per drink. Tuned so addiction
        /// (triggered deterministically once tolerance hits 1.0, see
        /// IngestionOutcomeDoer_ShadowsongWineIntake) lands on an exact glass count: Legendary
        /// hooks in 2 glasses, Masterwork in 3, Excellent in 5, then 7/10/14/20 for the rest -
        /// mirroring the barrel's own fermenting-day thresholds, just in reverse: the longer a
        /// vintage takes to make, the faster it consumes whoever drinks it.
        /// </summary>
        public static float EssenceToleranceGainFor(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return 1f / 20f;
                case QualityCategory.Poor: return 1f / 14f;
                case QualityCategory.Normal: return 1f / 10f;
                case QualityCategory.Good: return 1f / 7f;
                case QualityCategory.Excellent: return 1f / 5f;
                case QualityCategory.Masterwork: return 1f / 3f;
                case QualityCategory.Legendary: return 1f / 2f;
                default: return 1f / 10f;
            }
        }

        /// <summary>
        /// Exact market value per quality tier, overriding vanilla's own quality-price curve
        /// entirely (see StatPart_ShadowsongWinePrice). Excellent/Masterwork/Legendary are
        /// fixed at 50/65/80; the rest fill in a smooth curve underneath them.
        /// </summary>
        public static float MarketValueFor(QualityCategory q)
        {
            switch (q)
            {
                // raised ~25% with the three days of slower hunger the wine now gives
                case QualityCategory.Awful: return 15f;
                case QualityCategory.Poor: return 25f;
                case QualityCategory.Normal: return 35f;
                case QualityCategory.Good: return 42f;
                case QualityCategory.Excellent: return 50f;
                case QualityCategory.Masterwork: return 65f;
                case QualityCategory.Legendary: return 80f;
                default: return 35f;
            }
        }
    }
}
