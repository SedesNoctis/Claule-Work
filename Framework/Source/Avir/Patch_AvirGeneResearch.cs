using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Gene-gated research. All three Avir "living metal" projects - Cloudweaving, Apex
    /// Transmutation, and Pinnacle of Construction - have no requiredResearchBuilding, so
    /// they show up as workable at any research bench and are selectable by anyone in the
    /// research tab. What's different is enforced here in code: only pawns carrying the
    /// Fearborn gene can ever be assigned work on any of these three, regardless of
    /// Intellectual skill. Everyone else is turned away at the WorkGiver level, so they never
    /// even walk to the bench for it; the ResearchPerformed prefix is a belt-and-braces
    /// backstop in case a forced job or another mod ever gets a non-Fearborn pawn onto one
    /// anyway.
    ///
    /// Every other research project in the game is completely untouched - this only ever
    /// fires when the project actively selected for research is one of these three.
    /// </summary>
    public static class Patch_AvirGeneResearch
    {
        public static bool IsRestrictedProjectActive()
        {
            if (Find.ResearchManager == null)
            {
                return false;
            }

            ResearchProjectDef current = Find.ResearchManager.GetProject();
            if (current == null)
            {
                return false;
            }

            return current == AvirDefOf.Avir_Cloudweaving
                || current == AvirDefOf.Avir_ApexAlloycraft
                || current == AvirDefOf.Avir_PinnacleOfConstruction;
        }

        public static bool HasFearborn(Pawn pawn)
        {
            return pawn != null && pawn.genes != null && pawn.genes.HasActiveGene(AvirDefOf.Fearborn);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Researcher), nameof(WorkGiver_Researcher.JobOnThing))]
    public static class Patch_AvirGeneResearch_JobOnThing
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result == null)
            {
                return;
            }

            if (!Patch_AvirGeneResearch.IsRestrictedProjectActive())
            {
                return;
            }

            if (Patch_AvirGeneResearch.HasFearborn(pawn))
            {
                return;
            }

            JobFailReason.Is("Only the fearborn can grasp this working.");
            __result = null;
        }
    }

    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed))]
    public static class Patch_AvirGeneResearch_ResearchPerformed
    {
        // Backstop only. The WorkGiver postfix above should mean a non-Fearborn pawn never
        // starts this job in the first place - this just makes sure that if one somehow does
        // (a forced order, a debug tool, another mod's job assignment), no progress is made
        // and the pawn isn't left standing at the bench accomplishing nothing forever.
        public static bool Prefix(Pawn researcher)
        {
            if (!Patch_AvirGeneResearch.IsRestrictedProjectActive())
            {
                return true;
            }

            if (Patch_AvirGeneResearch.HasFearborn(researcher))
            {
                return true;
            }

            if (researcher != null && researcher.jobs != null && researcher.jobs.curJob != null)
            {
                researcher.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }

            return false;
        }
    }
}
