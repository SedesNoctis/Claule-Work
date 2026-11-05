using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Everything that happens when a pawn drinks Shadowsong Wine, gated in one place on
    /// whether the drinker has an active Arcane Hunger gene (Gene_ArcaneHunger.Of != null,
    /// same Fearborn gate used everywhere else this gene matters).
    ///
    /// Fearborn: Arcane Hunger fill, the clarity hediff, a rest offset, an ordinary alcohol
    /// buzz, the quality-scaled mood thought, the craving thought, and a hand-managed
    /// tolerance/addiction to Avir_ShadowsongEssence (quality-scaled tolerance gain, a chance
    /// of addiction once tolerance crosses the threshold below).
    ///
    /// Non-Fearborn: none of the above, ever - not even the ordinary alcohol buzz. The wine
    /// simply does nothing for them: no vomit, no rejection hediff, no message, just the
    /// recreation any drink gives and a mild "strange aftertaste" thought. They can never
    /// build tolerance or addiction to this wine, structurally - the tolerance-gain code below
    /// is only ever reached in the Fearborn branch.
    /// </summary>
    public class IngestionOutcomeDoer_ShadowsongWineIntake : IngestionOutcomeDoer
    {
        // ----- Shadowsong essence dependency tuning -----
        // Addiction triggers deterministically, no RNG: once tolerance fills to 1.0 the
        // addiction is granted outright. EssenceToleranceGainFor is tuned per quality so this
        // lands on an exact glass count (2 for Legendary, 3 for Masterwork, 5 for Excellent...).
        private const float MinToleranceToAddict = 1.0f;
        private const float InitialAddictionSeverity = 0.3f;

        // ----- Ordinary alcohol buzz (mirrors vanilla Beer's numbers) -----
        private const float AlcoholHighBaseSeverity = 0.15f;
        private const float AlcoholToleranceGain = 0.016f;

        private static HediffDef toleranceDef;
        private static HediffDef addictionDef;
        private static HediffDef alcoholHighDef;
        private static HediffDef alcoholToleranceDef;
        private static ThoughtDef qualityThoughtDef;
        private static ThoughtDef cravingThoughtDef;
        private static ThoughtDef wrongThoughtDef;

        private static HediffDef ToleranceDef => toleranceDef ?? (toleranceDef = DefDatabase<HediffDef>.GetNamed("Avir_ShadowsongEssenceTolerance"));
        private static HediffDef AddictionDef => addictionDef ?? (addictionDef = DefDatabase<HediffDef>.GetNamed("Avir_ShadowsongEssenceAddiction"));
        private static HediffDef AlcoholHighDef => alcoholHighDef ?? (alcoholHighDef = DefDatabase<HediffDef>.GetNamed("AlcoholHigh"));
        private static HediffDef AlcoholToleranceDef => alcoholToleranceDef ?? (alcoholToleranceDef = DefDatabase<HediffDef>.GetNamed("AlcoholTolerance"));
        private static ThoughtDef QualityThoughtDef => qualityThoughtDef ?? (qualityThoughtDef = DefDatabase<ThoughtDef>.GetNamed("Avir_ShadowsongWineMood"));
        private static ThoughtDef CravingThoughtDef => cravingThoughtDef ?? (cravingThoughtDef = DefDatabase<ThoughtDef>.GetNamed("Avir_ShadowsongWineCravingMood"));
        private static ThoughtDef WrongThoughtDef => wrongThoughtDef ?? (wrongThoughtDef = DefDatabase<ThoughtDef>.GetNamed("Avir_ShadowsongWineWrongMood"));

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int numTaken)
        {
            if (pawn?.health == null) return;

            if (Gene_ArcaneHunger.Of(pawn) != null)
            {
                DoFearbornOutcome(pawn, ingested);
            }
            else
            {
                DoInertOutcome(pawn);
            }
        }

        private void DoFearbornOutcome(Pawn pawn, Thing ingested)
        {
            QualityCategory quality = AvirQualityUtility.GetQuality(ingested);

            // Arcane Hunger fill.
            Gene_ArcaneHunger.Feed(pawn, AvirQualityUtility.ArcaneHungerFor(quality));

            // Shadowsong clarity hediff (Psychic Tea-like effect), quality baked in.
            Hediff_ShadowsongWine clarity = (Hediff_ShadowsongWine)HediffMaker.MakeHediff(
                DefDatabase<HediffDef>.GetNamed("Avir_Effect_ShadowsongWine"), pawn);
            clarity.sourceQuality = quality;
            clarity.Severity = 1f;
            pawn.health.AddHediff(clarity);

            // Chases off tiredness.
            if (pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevel = UnityEngine.Mathf.Clamp01(pawn.needs.rest.CurLevel + 0.4f);
            }

            GiveAlcoholBuzz(pawn);
            GiveEssenceDependency(pawn, quality);

            // Mood: quality-scaled memory (stage 0 = Awful ... 6 = Legendary matches
            // QualityCategory's own int order) plus the craving thought, every time.
            if (pawn.needs?.mood != null)
            {
                Thought_Memory qualityThought = (Thought_Memory)ThoughtMaker.MakeThought(QualityThoughtDef, (int)quality);
                pawn.needs.mood.thoughts.memories.TryGainMemory(qualityThought);

                Thought_Memory craving = (Thought_Memory)ThoughtMaker.MakeThought(CravingThoughtDef);
                pawn.needs.mood.thoughts.memories.TryGainMemory(craving);
            }
        }

        private void GiveAlcoholBuzz(Pawn pawn)
        {
            float existingTolerance = pawn.health.hediffSet.GetFirstHediffOfDef(AlcoholToleranceDef)?.Severity ?? 0f;
            float actualHighSeverity = AlcoholHighBaseSeverity * (1f - existingTolerance);

            if (actualHighSeverity > 0f)
            {
                Hediff high = pawn.health.hediffSet.GetFirstHediffOfDef(AlcoholHighDef);
                if (high == null)
                {
                    high = HediffMaker.MakeHediff(AlcoholHighDef, pawn);
                    high.Severity = actualHighSeverity;
                    pawn.health.AddHediff(high);
                }
                else
                {
                    high.Severity += actualHighSeverity;
                }
            }

            float toleranceGain = AlcoholToleranceGain / pawn.BodySize;
            Hediff tolerance = pawn.health.hediffSet.GetFirstHediffOfDef(AlcoholToleranceDef);
            if (tolerance == null)
            {
                tolerance = HediffMaker.MakeHediff(AlcoholToleranceDef, pawn);
                tolerance.Severity = toleranceGain;
                pawn.health.AddHediff(tolerance);
            }
            else
            {
                tolerance.Severity += toleranceGain;
            }
        }

        private void GiveEssenceDependency(Pawn pawn, QualityCategory quality)
        {
            Hediff existingAddiction = pawn.health.hediffSet.GetFirstHediffOfDef(AddictionDef);
            if (existingAddiction != null)
            {
                // Already hooked - drinking again feeds the Need rather than piling up more
                // tolerance. The Need, not this hediff, is what the withdrawal mood tracks now.
                RefillEssenceNeed(pawn);
                return;
            }

            Hediff tolerance = pawn.health.hediffSet.GetFirstHediffOfDef(ToleranceDef);
            if (tolerance == null)
            {
                tolerance = HediffMaker.MakeHediff(ToleranceDef, pawn);
                pawn.health.AddHediff(tolerance);
            }
            tolerance.Severity = UnityEngine.Mathf.Clamp01(tolerance.Severity + AvirQualityUtility.EssenceToleranceGainFor(quality));

            if (tolerance.Severity < MinToleranceToAddict) return;

            Hediff addiction = HediffMaker.MakeHediff(AddictionDef, pawn);
            addiction.Severity = InitialAddictionSeverity;
            pawn.health.AddHediff(addiction);

            if (pawn.Spawned)
            {
                Find.LetterStack.ReceiveLetter(
                    "Avir_ShadowsongAddictionGainedLabel".Translate(),
                    "Avir_ShadowsongAddictionGained".Translate(pawn.LabelShortCap),
                    LetterDefOf.NegativeEvent,
                    pawn);
            }

            // The Need only gets added to the pawn once an addiction hediff for its chemical
            // exists, so this is the first point it can be filled.
            RefillEssenceNeed(pawn);
        }

        private void RefillEssenceNeed(Pawn pawn)
        {
            NeedDef needDef = DefDatabase<NeedDef>.GetNamedSilentFail("Chemical_ShadowsongEssence");
            if (needDef == null) return;

            Need need = pawn.needs?.TryGetNeed(needDef);
            if (need != null)
            {
                need.CurLevel = 1f;
            }
        }

        /// <summary>
        /// Non-Fearborn drinker. Deliberately does almost nothing: the alcohol and essence
        /// comps are already gated by Comp_FearbornDrug, and the recreation from the drink
        /// itself is handed out by the engine from ingestible.joy before this ever runs. All
        /// that is left is a flavour memory.
        /// </summary>
        private void DoInertOutcome(Pawn pawn)
        {
            if (pawn.needs?.mood != null)
            {
                Thought_Memory wrong = (Thought_Memory)ThoughtMaker.MakeThought(WrongThoughtDef);
                pawn.needs.mood.thoughts.memories.TryGainMemory(wrong);
            }
        }
    }
}
