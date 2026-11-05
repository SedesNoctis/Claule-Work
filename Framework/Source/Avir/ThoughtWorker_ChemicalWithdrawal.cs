using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Reads a Need's live CurLevelPercentage directly, rather than relying on the addiction
    /// hediff's own CurStageIndex via the built-in ThoughtWorker_Hediff. That approach looked
    /// correct on paper (VBE's real caffeine addiction uses it) but empirically doesn't behave
    /// as expected here - a pawn's addiction hediff sat at "missing the glass" (stage 0) even
    /// at 70% severity, well past where minSeverity should have moved it to a later stage.
    /// The Need bar itself is confirmed live and independently tracked, so that's what mood
    /// should read instead.
    ///
    /// Reads the base Need class (not Need_Chemical specifically) since the NeedDefs now use
    /// EBSGFramework.Need_GenericNeed as their needClass - CurLevelPercentage is standard Need
    /// API either way, so this works unchanged regardless of which Need subclass is in use.
    ///
    /// Leaf classes say which NeedDef to read and implement their own banding, since Shadowsong
    /// (3 active stages + a silent "fully fed" state) and Crimson (4 active stages) don't share
    /// the same shape.
    /// </summary>
    public abstract class ThoughtWorker_ChemicalWithdrawal : ThoughtWorker
    {
        protected abstract NeedDef WithdrawalNeedDef { get; }

        /// <summary>Null if not addicted (the Need doesn't exist at all yet) or not found.</summary>
        protected Need GetNeed(Pawn p)
        {
            if (p?.needs == null) return null;
            NeedDef needDef = WithdrawalNeedDef;
            if (needDef == null) return null;
            return p.needs.TryGetNeed(needDef);
        }
    }

    /// <summary>
    /// 4-stage design: 4/4 (need >= 75%) is silent - no mood at all. 3/4 "wanting it",
    /// 2/4 "wanting it badly", 1/4 "withdrawal" are the three active stages below that,
    /// each a clean quarter-band (75/50/25).
    /// </summary>
    public class ThoughtWorker_ShadowsongWithdrawal : ThoughtWorker_ChemicalWithdrawal
    {
        private static NeedDef needDef;
        protected override NeedDef WithdrawalNeedDef =>
            needDef ?? (needDef = DefDatabase<NeedDef>.GetNamedSilentFail("Chemical_ShadowsongEssence"));

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Need need = GetNeed(p);
            if (need == null) return ThoughtState.Inactive;

            float pct = need.CurLevelPercentage;
            if (pct >= 0.75f) return ThoughtState.Inactive;       // 4/4 - fully fed, nothing to say
            if (pct >= 0.50f) return ThoughtState.ActiveAtStage(0); // 3/4 - wanting it
            if (pct >= 0.25f) return ThoughtState.ActiveAtStage(1); // 2/4 - wanting it badly
            return ThoughtState.ActiveAtStage(2);                   // 1/4 - withdrawal
        }
    }

    public class ThoughtWorker_CrimsonWithdrawal : ThoughtWorker_ChemicalWithdrawal
    {
        private static NeedDef needDef;
        protected override NeedDef WithdrawalNeedDef =>
            needDef ?? (needDef = DefDatabase<NeedDef>.GetNamedSilentFail("Chemical_CrimsonEssence"));

        // 4 active stages, roughly tracking a 7-day empty schedule as "days since last vial":
        // >=0.95 Inactive, 0.72-0.95 stage0, 0.43-0.72 stage1, 0.14-0.43 stage2, <0.14 stage3.
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Need need = GetNeed(p);
            if (need == null) return ThoughtState.Inactive;

            float pct = need.CurLevelPercentage;
            if (pct >= 0.95f) return ThoughtState.Inactive;
            if (pct >= 0.72f) return ThoughtState.ActiveAtStage(0);
            if (pct >= 0.43f) return ThoughtState.ActiveAtStage(1);
            if (pct >= 0.14f) return ThoughtState.ActiveAtStage(2);
            return ThoughtState.ActiveAtStage(3);
        }
    }
}
