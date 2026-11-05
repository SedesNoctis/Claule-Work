using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Much simpler than the Wine/Crimson Rage outcome doers - no addiction, no tolerance, no
    /// gene-gating (anyone, including babies, can eat this). Just: everyone gets the fullness
    /// hediff and the Yummy thought; Capricious pawns additionally get their craving timer reset
    /// and a one-time sentimental thought.
    /// </summary>
    public class IngestionOutcomeDoer_Blueberries : IngestionOutcomeDoer
    {
        private static HediffDef fullnessDef;
        private static HediffDef FullnessDef =>
            fullnessDef ?? (fullnessDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_BlueberryFullness"));

        private static HediffDef cravingDef;
        private static HediffDef CravingDef =>
            cravingDef ?? (cravingDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_BlueberryCraving"));

        private static ThoughtDef yummyThought;
        private static ThoughtDef YummyThought =>
            yummyThought ?? (yummyThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_Blueberries"));

        private static ThoughtDef satisfiedThought;
        private static ThoughtDef SatisfiedThought =>
            satisfiedThought ?? (satisfiedThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_CapriciousSatisfied"));

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int numTaken)
        {
            if (pawn?.health == null) return;

            try
            {
                if (FullnessDef != null)
                {
                    Hediff fullness = HediffMaker.MakeHediff(FullnessDef, pawn);
                    pawn.health.AddHediff(fullness);
                }

                if (YummyThought != null && pawn.needs?.mood != null)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(YummyThought);
                }

                // Reset the craving timer for everyone (cheap, harmless for non-Capricious
                // pawns) - only the ThoughtWorker actually reading it cares about the trait.
                if (CravingDef != null)
                {
                    Hediff craving = pawn.health.hediffSet.GetFirstHediffOfDef(CravingDef);
                    if (craving == null)
                    {
                        craving = HediffMaker.MakeHediff(CravingDef, pawn);
                        pawn.health.AddHediff(craving);
                    }
                    craving.Severity = 0f;
                }

                bool capricious = pawn.story?.traits?.HasTrait(DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Capricious")) ?? false;
                if (capricious && SatisfiedThought != null && pawn.needs?.mood != null)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(SatisfiedThought);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("[Avir] Blueberries outcome doer threw for " + pawn?.LabelShortCap + ": " + ex);
            }
        }
    }
}
