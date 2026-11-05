using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Situational - reads Avir_CrimsonRage's live severity every tick, same stage bands as
    /// the hediff itself, so the mood always matches whatever stage the pawn is actually in.
    /// </summary>
    public class ThoughtWorker_CrimsonRageStage : ThoughtWorker
    {
        private static HediffDef crimsonRageDef;
        private static HediffDef CrimsonRageDef =>
            crimsonRageDef ?? (crimsonRageDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CrimsonRage"));

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            HediffDef def = CrimsonRageDef;
            if (def == null) return ThoughtState.Inactive;

            Hediff hediff = p?.health?.hediffSet?.GetFirstHediffOfDef(def);
            if (hediff == null) return ThoughtState.Inactive;

            float sev = hediff.Severity;
            if (sev >= 0.81f) return ThoughtState.ActiveAtStage(4); // Crimson Rage - Ecstatic Violence
            if (sev >= 0.61f) return ThoughtState.ActiveAtStage(3); // Crimson Patience - Ecstatic
            if (sev >= 0.41f) return ThoughtState.ActiveAtStage(2); // Crimson Mind - Excited
            if (sev >= 0.21f) return ThoughtState.ActiveAtStage(1); // Crimson Thoughts - Vile Thoughts
            return ThoughtState.ActiveAtStage(0);                  // Crimson Hunger - Dreams Of Violence
        }
    }
}
