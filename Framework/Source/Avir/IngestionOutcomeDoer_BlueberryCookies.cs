using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Same shape as IngestionOutcomeDoer_Blueberries, plus one gene-gated branch: pawns carrying
    /// Avir_EssentiumBeakConditioning (i.e. the Avir_FrenziedBeak gene) get a real, tiny nudge on
    /// the SAME strike-conditioning tracker combat uses (HediffComp_BeakConditioning), instead of
    /// a fake parallel mechanic. Everyone else - including babies without the gene - just gets the
    /// plain Crunchy thought.
    ///
    /// NOTE: HediffComp_BeakConditioning.RegisterStrike() de-duplicates by game tick (see
    /// BeakPatches.cs). Calling it more than once here in the same tick still only counts as ONE
    /// strike (+0.001 severity, per Hediffs_Avir_Beak.xml's perStrike). That's intentional, not a
    /// bug to work around - a cookie is flavor on top of the real honing system, not a shortcut
    /// through it. Gated on gene/hediff presence per the correction, not DevelopmentalStage/age.
    /// </summary>
    public class IngestionOutcomeDoer_BlueberryCookies : IngestionOutcomeDoer
    {
        private static ThoughtDef crunchyThought;
        private static ThoughtDef CrunchyThought =>
            crunchyThought ?? (crunchyThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_BlueberryCookies"));

        private static ThoughtDef beakSharpenedThought;
        private static ThoughtDef BeakSharpenedThought =>
            beakSharpenedThought ?? (beakSharpenedThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_BeakSharpened"));

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int numTaken)
        {
            if (pawn?.health == null) return;

            try
            {
                if (CrunchyThought != null && pawn.needs?.mood != null)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(CrunchyThought);
                }

                Hediff beakConditioning = pawn.health.hediffSet
                    .GetFirstHediffOfDef(EssentiumBeakDefOf.Avir_EssentiumBeakConditioning);

                if (beakConditioning != null)
                {
                    beakConditioning.TryGetComp<HediffComp_BeakConditioning>()?.RegisterStrike();

                    if (BeakSharpenedThought != null && pawn.needs?.mood != null)
                    {
                        pawn.needs.mood.thoughts.memories.TryGainMemory(BeakSharpenedThought);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("[Avir] Blueberry Cookies outcome doer threw for " + pawn?.LabelShortCap + ": " + ex);
            }
        }
    }
}
