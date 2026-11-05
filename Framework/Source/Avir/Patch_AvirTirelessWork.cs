using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Avir_HA_ObsessiveFocus and Avir_HA_TirelessPerformer are not just stat sticks - this is
    /// the actual behavior the user asked for: "works tirelessly, forgetting about anything
    /// around him, refusing his own needs over a project."
    ///
    /// Vanilla decides "time to stop and eat/sleep" via two ThinkNode job-givers running high in
    /// every pawn's think tree - JobGiver_GetFood and JobGiver_GetRest. This prefixes both:
    /// while the pawn holds one of the two traits AND is currently doing a work job in their own
    /// wheelhouse (Construction/Research/Art/Crafting) AND the relevant need is still above a
    /// safety floor, the vanilla job offer is skipped outright - so the pawn simply keeps
    /// working straight through the point a normal pawn would break. Below the floor, this gets
    /// entirely out of the way and vanilla behaves exactly as it always has - this is a pawn who
    /// pushes hunger and exhaustion further than anyone should, not a pawn who can starve or
    /// collapse from working. That distinction is the whole point: real, visible neglect with a
    /// hard backstop, not a broken pawn.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirTirelessWork
    {
        // How far past the normal "go eat/go sleep" point they push - 0.12 means they keep
        // working right down to 12% before vanilla is allowed back in to save them.
        private const float SafetyFloor = 0.12f;

        private static readonly HashSet<WorkTypeDef> OwnWork = new HashSet<WorkTypeDef>();
        private static TraitDef ObsessiveFocus;
        private static TraitDef TirelessPerformer;

        static AvirTirelessWork()
        {
            void Add(string defName)
            {
                WorkTypeDef def = DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
                if (def != null) OwnWork.Add(def);
            }

            Add("Construction");
            Add("Art");
            Add("Research");
            // Crafting exists as WorkTypeDefOf.Crafting elsewhere in this mod, but the string
            // lookup here keeps this file self-contained and equally self-disabling if a future
            // RimWorld version ever renames any of these.
            Add("Crafting");

            ObsessiveFocus = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_HA_ObsessiveFocus");
            TirelessPerformer = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_HA_TirelessPerformer");
        }

        /// <summary>True if this WorkTypeDef is a High Artificer's "own lane" - Construction,
        /// Research, Art, Crafting. Shared with HediffComp_AvirRestlessness so both mechanics
        /// agree on exactly the same definition of "working on their own thing."</summary>
        public static bool IsOwnWork(WorkTypeDef workType)
        {
            return workType != null && OwnWork.Contains(workType);
        }

        /// <summary>True if this pawn should currently be pushing through the given need
        /// instead of vanilla stopping them for it.</summary>
        public static bool ShouldPushThrough(Pawn pawn, Need need)
        {
            if (pawn?.story?.traits == null || need == null) return false;
            if (need.CurLevelPercentage > 1f || need.CurLevelPercentage <= SafetyFloor) return false;

            bool hasTrait = (ObsessiveFocus != null && pawn.story.traits.HasTrait(ObsessiveFocus))
                          || (TirelessPerformer != null && pawn.story.traits.HasTrait(TirelessPerformer));
            if (!hasTrait) return false;

            WorkTypeDef workType = pawn.CurJob?.workGiverDef?.workType;
            return IsOwnWork(workType);
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class Patch_AvirTirelessWork_Food
    {
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (AvirTirelessWork.ShouldPushThrough(pawn, pawn?.needs?.food))
                {
                    __result = null;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Tireless-work food check failed: " + e, 0x4A51E5);
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
    public static class Patch_AvirTirelessWork_Rest
    {
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (AvirTirelessWork.ShouldPushThrough(pawn, pawn?.needs?.rest))
                {
                    __result = null;
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Tireless-work rest check failed: " + e, 0x4A51E6);
            }

            return true;
        }
    }
}
