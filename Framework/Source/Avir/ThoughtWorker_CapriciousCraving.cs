using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Active only for pawns with the Capricious trait, and only once it's been at least 3 days
    /// since their last Blueberries! (tracked via Avir_BlueberryCraving's severity, reset to 0
    /// on every ingestion by IngestionOutcomeDoer_Blueberries). No tracker hediff at all counts
    /// the same as "maxed out" - a Capricious pawn who has simply never had one yet is still
    /// craving one, not exempt from it.
    /// </summary>
    public class ThoughtWorker_CapriciousCraving : ThoughtWorker
    {
        private static TraitDef capriciousTrait;
        private static TraitDef CapriciousTrait =>
            capriciousTrait ?? (capriciousTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Capricious"));

        private static HediffDef cravingDef;
        private static HediffDef CravingDef =>
            cravingDef ?? (cravingDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_BlueberryCraving"));

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            TraitDef trait = CapriciousTrait;
            if (trait == null || p?.story?.traits == null) return ThoughtState.Inactive;
            if (!p.story.traits.HasTrait(trait)) return ThoughtState.Inactive;

            HediffDef def = CravingDef;
            if (def == null) return ThoughtState.Inactive;

            Hediff craving = p.health?.hediffSet?.GetFirstHediffOfDef(def);
            bool wanting = craving == null || craving.Severity >= 1f;

            return wanting ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
        }
    }
}
