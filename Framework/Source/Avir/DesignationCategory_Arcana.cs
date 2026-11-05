using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// The arcana tab (and, with Better Architect Menu, each of its sub-tabs) is shown only once something in it
    /// can be built: nothing researched yet - no tab. God mode shows everything.
    /// </summary>
    [HarmonyPatch(typeof(DesignationCategoryDef), nameof(DesignationCategoryDef.Visible), MethodType.Getter)]
    public static class Patch_ArcanaTabVisible
    {
        private static readonly Dictionary<DesignationCategoryDef, (int frame, bool open)> cache = new Dictionary<DesignationCategoryDef, (int, bool)>();

        public static void Postfix(DesignationCategoryDef __instance, ref bool __result)
        {
            if (!__result || __instance.defName == null || !__instance.defName.StartsWith("Avir_Arcana") || DebugSettings.godMode) return;
            if (Current.ProgramState != ProgramState.Playing) return;
            int frame = UnityEngine.Time.frameCount;
            if (!cache.TryGetValue(__instance, out var c) || frame - c.frame > 120)
            {
                bool sub = __instance.defName != "Avir_Arcana";
                bool open = DefDatabase<ThingDef>.AllDefsListForReading.Any(d => d.designationCategory != null && Belongs(d.designationCategory, __instance, sub) && d.IsResearchFinished)
                            || DefDatabase<TerrainDef>.AllDefsListForReading.Any(d => d.designationCategory != null && Belongs(d.designationCategory, __instance, sub) && d.IsResearchFinished);
                c = (frame, open);
                cache[__instance] = c;
            }
            __result = c.open;
        }

        private static bool Belongs(DesignationCategoryDef cat, DesignationCategoryDef tab, bool sub) =>
            sub ? cat == tab : cat == tab || cat.defName.StartsWith("Avir_Arcana_");
    }
}
