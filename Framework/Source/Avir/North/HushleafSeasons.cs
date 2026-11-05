using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.North
{
    /// <summary>
    /// In the warm forests (twilight and burning) the wild hushleaf follows the calendar: the warm kind comes up
    /// in spring, summer and autumn, the pale northern kind only in winter. Elsewhere both behave as their biomes
    /// list them. The calendar is the map's real one, so the burning forest's eternal autumn colours do not keep
    /// its winters from bringing the pale herb.
    /// </summary>
    [HarmonyPatch(typeof(WildPlantSpawner), "CalculatePlantsWhichCanGrowAt")]
    public static class Patch_HushleafSeasons
    {
        private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> mapRef = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

        public static void Postfix(WildPlantSpawner __instance, IntVec3 c, List<ThingDef> outPlants)
        {
            if (outPlants == null || outPlants.Count == 0) return;
            Map map = mapRef(__instance);
            if (map == null) return;
            string biome = map.BiomeAt(c)?.defName;
            if (biome != "Avir_TwilightForest" && biome != "Avir_BurningForest") return;
            Season s = GenLocalDate.Season(map);
            bool winter = s == Season.Winter || s == Season.PermanentWinter;
            outPlants.RemoveAll(p => p.defName == (winter ? "Avir_PlantDuskHushleaf" : "Avir_PlantHushleaf"));
        }
    }
}
