using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace Avir.North
{
    // =====================================================================================
    // WATER AND SNOW IN THE NORTH.
    //  - Two or three ponds per map, each one body of water, in the snowfields (rarely elsewhere) (1.5-3.5% of the map, sometimes up to 6%, rarely 10%), a small lake now and then;
    //    rivers and the ocean come on top. In the cold it freezes over like any water.
    //  - Wild plants take root on hardened snow far less often than its fertility says (AvirWildPlantFactor).
    // =====================================================================================
    public class GenStep_AvirPonds : GenStep
    {
        public override int SeedPart => 0x5A1D0F;

        public override void Generate(Map map, GenStepParams parms)
        {
            AvirPonds s = map.Biome?.GetModExtension<AvirPonds>();
            if (s == null)
            {
                if (map.Biome?.workerClass != typeof(BiomeWorker_AvirNorth)) return;
                s = AvirPonds.North;
            }
            // two or three ponds on the map, each ONE body of water, set in the snowfields (only now and then elsewhere);
            // one of them is now and then a small lake
            MapGenFloatGrid elevation = MapGenerator.Elevation;
            ModuleBase wobble = new Perlin(0.12, 2.0, 0.5, 2, Rand.Int, QualityMode.Low);
            var snow = new List<IntVec3>();
            foreach (IntVec3 c in map.AllCells)
                if (s.preferTerrain != null && c.x >= 20 && c.z >= 20 && c.x < map.Size.x - 20 && c.z < map.Size.z - 20 && c.GetTerrain(map).defName.StartsWith(s.preferTerrain))
                    snow.Add(c);
            int count = s.count.RandomInRange, made = 0, cells = 0;
            bool lake = Rand.Chance(s.lakeChance);
            var centres = new List<IntVec3>();
            for (int k = 0; k < 200 && made < count; k++)
            {
                IntVec3 c = snow.Count > 0 && !Rand.Chance(s.elsewhereChance) ? snow.RandomElement()
                    : new IntVec3(Rand.Range(20, map.Size.x - 20), 0, Rand.Range(20, map.Size.z - 20));
                if (!Ok(c, map, elevation) || !centres.TrueForAll(o => (o - c).LengthHorizontal > map.Size.x / (count > 3 ? 6f : 4f))) continue;
                float radius = lake ? s.lakeRadius.RandomInRange : s.radius.RandomInRange;
                lake = false;
                foreach (IntVec3 x in GenRadial.RadialCellsAround(c, radius * 1.35f, true))
                {
                    if (!x.InBounds(map) || !Ok(x, map, elevation)) continue;
                    float d = (x - c).LengthHorizontal / (radius * (1f + 0.25f * (float)wobble.GetValue(x.x, 0, x.z)));
                    if (d > 1f) continue;
                    map.terrainGrid.SetTerrain(x, radius >= 8f && d < 0.55f ? s.Deep : s.Shallow);   // small ponds stay shallow
                    cells++;
                }
                centres.Add(c); made++;
            }
            if (Prefs.DevMode)
                Log.Message("[Avir] " + map.Biome.label + " ponds: " + made + " ponds, " + cells + " cells (" + (cells * 100f / map.Area).ToString("0.#") + "% of the map)");
        }

        private static bool Ok(IntVec3 c, Map map, MapGenFloatGrid elevation) =>
            c.GetEdifice(map) == null && !c.GetTerrain(map).IsWater && (elevation == null || elevation[c] < 0.55f);
    }

    /// <summary>On a terrain def: wild plants take root here this much as often as its fertility would say.</summary>
    public class AvirWildPlantFactor : DefModExtension
    {
        public float factor = 1f;
    }

    [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.GetBaseDesiredPlantsCountAt))]
    public static class Patch_SnowPlantsBase
    {
        private static readonly AccessTools.FieldRef<WildPlantSpawner, Map> mapOf = AccessTools.FieldRefAccess<WildPlantSpawner, Map>("map");

        public static void Postfix(WildPlantSpawner __instance, IntVec3 c, ref float __result)
        {
            if (__result <= 0f) return;
            Map map = mapOf(__instance);
            float? f = map != null ? c.GetTerrain(map)?.GetModExtension<AvirWildPlantFactor>()?.factor : null;
            if (f.HasValue) __result *= f.Value;
        }
    }
}
