using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Everything that happens when a pawn drinks Crimson Rage, gated on Gene_ArcaneHunger.Of
    /// != null (the same Fearborn gate used throughout the mod).
    ///
    /// Fearborn: the one-time "initial consumption" effects (rest, joy, a flat Arcane Hunger
    /// gain) every time, then either a normal dose (Crimson Patience, 4/5, decays normally -
    /// this is the first vial, or any vial more than 48 in-game hours after the last one) or a
    /// forced relapse (second vial within 48h: straight to 5/5, the Crimson Rage mental break,
    /// and guaranteed addiction - no RNG, per the brief's "addiction is inevitable").
    ///
    /// Non-Fearborn: immediate vomit, a forced vanilla Berserk mental break (looked up by
    /// defName rather than DefOf, same defensive style as Patch_AvirEarlyHatch/
    /// Patch_PredatorMindset_NoPanicFlee elsewhere in this mod), the rejection hediff, and the
    /// "tasted wrong" thought. Never any tolerance or addiction risk.
    /// </summary>
    public class IngestionOutcomeDoer_CrimsonRage : IngestionOutcomeDoer
    {
        // ----- Initial consumption effects (every dose, Fearborn only) -----
        private const float RestGain = 0.80f;
        private const float JoyGain = 1.0f;
        private const float ArcaneHungerGain = 0.10f;

        // ----- Forced relapse addiction: "inevitable" means the Hediff_Addiction is granted
        // unconditionally, no roll. Severity starts LOW (just fed, minimal withdrawal) since
        // severity now drives the withdrawal stage (see Hediffs_Avir_CrimsonRage.xml) rather
        // than representing raw addiction strength - starting it maxed would put a
        // freshly-addicted pawn straight into the worst withdrawal stage, which is backwards.
        // Kept just above 0 rather than exactly 0: a hediff with a SeverityPerDay comp gets
        // automatically removed by the engine once its severity hits 0, which was silently
        // pruning the addiction the same tick it was granted. -----
        private const float RelapseAddictionSeverity = 0.05f;

        private static HediffDef crimsonRageDef;
        private static HediffDef addictionDef;
        private static HediffDef rejectionDef;
        private static ThoughtDef wrongThoughtDef;
        private static ThingDef vomitFilthDef;
        private static MentalStateDef vanillaBerserkDef;

        private static HediffDef CrimsonRageDef => crimsonRageDef ?? (crimsonRageDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CrimsonRage"));
        private static HediffDef AddictionDef => addictionDef ?? (addictionDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CrimsonEssenceAddiction"));
        private static HediffDef RejectionDef => rejectionDef ?? (rejectionDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CrimsonRejection"));
        private static ThoughtDef WrongThoughtDef => wrongThoughtDef ?? (wrongThoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_CrimsonRageWrongMood"));
        private static ThingDef VomitFilthDef => vomitFilthDef ?? (vomitFilthDef = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_Vomit"));
        private static MentalStateDef VanillaBerserkDef => vanillaBerserkDef ?? (vanillaBerserkDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("Berserk"));

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int numTaken)
        {
            if (pawn?.health == null) return;

            try
            {
                if (Gene_ArcaneHunger.Of(pawn) != null)
                {
                    DoFearbornOutcome(pawn);
                }
                else
                {
                    DoRejectionOutcome(pawn);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("[Avir] Crimson Rage outcome doer threw for " + pawn?.LabelShortCap + ": " + ex);
            }
        }

        private void DoFearbornOutcome(Pawn pawn)
        {
            if (CrimsonRageDef == null)
            {
                Log.Error("[Avir] Avir_CrimsonRage HediffDef not found - skipping Crimson Rage effects entirely.");
                return;
            }

            // Initial consumption effects - every vial, relapse or not.
            if (pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevel = UnityEngine.Mathf.Clamp01(pawn.needs.rest.CurLevel + RestGain);
            }
            if (pawn.needs?.joy != null)
            {
                pawn.needs.joy.CurLevel = UnityEngine.Mathf.Clamp01(pawn.needs.joy.CurLevel + JoyGain);
            }
            Gene_ArcaneHunger.Feed(pawn, ArcaneHungerGain);

            Hediff_CrimsonRage rageHediff = pawn.health.hediffSet.GetFirstHediffOfDef(CrimsonRageDef) as Hediff_CrimsonRage;
            bool relapse = rageHediff != null && rageHediff.WouldBeRelapseNow();

            if (rageHediff == null)
            {
                rageHediff = (Hediff_CrimsonRage)HediffMaker.MakeHediff(CrimsonRageDef, pawn);
                pawn.health.AddHediff(rageHediff);
            }

            if (relapse)
            {
                rageHediff.TriggerRelapse();
                ForceAddiction(pawn);
            }
            else
            {
                rageHediff.ApplyNormalDose();
                // Normal case: no forced addiction - the item's own CompProperties_Drug
                // (wrapped in Comp_FearbornDrug) rolls real vanilla addiction chance
                // automatically on ingestion. Nothing else needed here.
            }
        }

        private void ForceAddiction(Pawn pawn)
        {
            if (AddictionDef == null)
            {
                Log.Error("[Avir] Avir_CrimsonEssenceAddiction HediffDef not found - relapse addiction was NOT granted.");
                return;
            }

            Hediff addiction = pawn.health.hediffSet.GetFirstHediffOfDef(AddictionDef);
            bool wasAlreadyAddicted = addiction != null;

            if (addiction == null)
            {
                addiction = HediffMaker.MakeHediff(AddictionDef, pawn);
                addiction.Severity = RelapseAddictionSeverity;
                pawn.health.AddHediff(addiction);
            }
            else
            {
                addiction.Severity = RelapseAddictionSeverity;
            }

            if (!wasAlreadyAddicted && pawn.Spawned)
            {
                Find.LetterStack.ReceiveLetter(
                    "Avir_CrimsonAddictionGainedLabel".Translate(),
                    "Avir_CrimsonAddictionGained".Translate(pawn.LabelShortCap),
                    LetterDefOf.NegativeEvent,
                    pawn);
            }

            NeedDef needDef = DefDatabase<NeedDef>.GetNamedSilentFail("Chemical_CrimsonEssence");
            if (needDef == null) return;
            Need need = pawn.needs?.TryGetNeed(needDef);
            if (need != null)
            {
                need.CurLevel = 1f;
            }
        }

        private void DoRejectionOutcome(Pawn pawn)
        {
            if (pawn.Spawned && pawn.Map != null && VomitFilthDef != null)
            {
                FilthMaker.TryMakeFilth(pawn.Position, pawn.Map, VomitFilthDef);
            }

            if (RejectionDef != null)
            {
                pawn.health.AddHediff(RejectionDef);
            }

            if (pawn.needs?.mood != null && WrongThoughtDef != null)
            {
                Thought_Memory wrong = (Thought_Memory)ThoughtMaker.MakeThought(WrongThoughtDef);
                pawn.needs.mood.thoughts.memories.TryGainMemory(wrong);
            }

            if (pawn.Spawned && VanillaBerserkDef != null && pawn.mindState?.mentalStateHandler != null)
            {
                pawn.mindState.mentalStateHandler.TryStartMentalState(
                    VanillaBerserkDef,
                    reason: "Avir_CrimsonRageRejectedReason".Translate(pawn.LabelShortCap),
                    forceWake: true);
            }
        }
    }
}
