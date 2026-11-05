using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Psychic Tea-like effect, but the strength scales with the quality of the specific bottle
    /// that was drunk (baked in as sourceQuality at creation time) and it only lasts one day
    /// instead of three - see the HediffCompProperties_SeverityPerDay on the HediffDef.
    /// </summary>
    public class Hediff_ShadowsongWine : HediffWithComps
    {
        public QualityCategory sourceQuality = QualityCategory.Normal;

        private HediffStage cachedStage;
        private QualityCategory cachedForQuality;

        private static StatDef meditationFocusGainStat;
        private static StatDef psychicEntropyMaxStat;
        private static StatDef psychicEntropyRecoveryRateStat;
        private static StatDef psychicSensitivityStat;

        private static StatDef MeditationFocusGainStat =>
            meditationFocusGainStat ?? (meditationFocusGainStat = DefDatabase<StatDef>.GetNamed("MeditationFocusGain"));

        private static StatDef PsychicEntropyMaxStat =>
            psychicEntropyMaxStat ?? (psychicEntropyMaxStat = DefDatabase<StatDef>.GetNamed("PsychicEntropyMax"));

        private static StatDef PsychicEntropyRecoveryRateStat =>
            psychicEntropyRecoveryRateStat ?? (psychicEntropyRecoveryRateStat = DefDatabase<StatDef>.GetNamed("PsychicEntropyRecoveryRate"));

        private static StatDef PsychicSensitivityStat =>
            psychicSensitivityStat ?? (psychicSensitivityStat = DefDatabase<StatDef>.GetNamed("PsychicSensitivity"));

        public override HediffStage CurStage
        {
            get
            {
                if (cachedStage == null || cachedForQuality != sourceQuality)
                {
                    float m = AvirQualityUtility.PsychicEffectMultiplierFor(sourceQuality);

                    cachedStage = new HediffStage
                    {
                        statFactors = new List<StatModifier>
                        {
                            new StatModifier { stat = MeditationFocusGainStat, value = 1f + 1.3f * m },
                            new StatModifier { stat = PsychicEntropyMaxStat, value = 1f + 1.3f * m },
                            new StatModifier { stat = PsychicEntropyRecoveryRateStat, value = 1f + 1.3f * m },
                            new StatModifier { stat = PsychicSensitivityStat, value = 1f + 1.3f * m },
                        },
                        statOffsets = new List<StatModifier>
                        {
                            new StatModifier { stat = StatDefOf.ComfyTemperatureMin, value = 12f * m },
                            new StatModifier { stat = StatDefOf.ComfyTemperatureMax, value = -10f * m },
                            new StatModifier { stat = StatDefOf.PainShockThreshold, value = -0.18f * m },
                        },
                        restFallFactor = AvirQualityUtility.RestFallFactorFor(sourceQuality),
                    };
                    cachedForQuality = sourceQuality;
                }
                return cachedStage;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sourceQuality, "sourceQuality", QualityCategory.Normal);
        }
    }
}
