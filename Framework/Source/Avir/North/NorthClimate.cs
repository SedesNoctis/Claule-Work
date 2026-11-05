using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace Avir.North
{
    // =====================================================================================
    // THE NORTHERN MAGIC FOREST: the frozen waste (more loose snow) and the frost oasis (less). Two biomes on the cold
    // side of the world, in patches where a slow noise says the north is "thin" - and with a climate of their own:
    // each season keeps to its own band (XML: Avir.NorthClimate on the biome), whatever the tile's latitude says.
    // =====================================================================================

    /// <summary>A biome's own climate: the cold and warm ends of each season (the day swings between them).</summary>
    public class NorthClimate : DefModExtension
    {
        public FloatRange winter = new FloatRange(-32f, -18f);
        public FloatRange spring = new FloatRange(-15f, 3f);
        public FloatRange summer = new FloatRange(-4f, 9f);
        public FloatRange autumn = new FloatRange(-16f, -2f);
        /// <summary>How much of vanilla's daily random swing (±7) is kept.</summary>
        public float dailyNoise = 0.35f;
        /// <summary>The whole climate shifts this much per degree of latitude nearer the equator than latRef (colder
        /// toward the pole), so neighbouring tiles of one biome differ a little.</summary>
        public float latFollow = 0f, latRef = 45f;
        /// <summary>Relative climate: the tile's own (vanilla) seasons kept, their swing widened `swing` times and the
        /// whole year moved by `bias` - the bands above are not used. So the biome follows the land around it.</summary>
        public bool relative;
        public float swing = 1f, bias = 0f;
        /// <summary>Relative climate: the cold half of the year is scaled by this (the burning forest's winters are mild).</summary>
        public float coldScale = 1f;
        /// <summary>Degrees warmer while this weather lies on the map (the burning forest's steam).</summary>
        public WeatherDef warmWeather;
        public float warmWeatherBonus = 0f;

        public float Shift(PlanetTile tile) =>
            latFollow == 0f ? 0f : (latRef - Mathf.Abs(Find.WorldGrid.LongLatOf(tile).y)) * latFollow;

        /// <summary>The year's average: the middle of the four seasons' bands, shifted for the tile.</summary>
        public float YearAverage(PlanetTile tile) => relative ? Find.WorldGrid[tile].temperature + bias :
            (winter.Average + spring.Average + summer.Average + autumn.Average) * 0.25f + Shift(tile);

        /// <summary>Where in the year this tick is: 0 mid-winter, 0.25 mid-spring, 0.5 mid-summer, 0.75 mid-autumn.</summary>
        public static float SeasonPos(int absTick, PlanetTile tile)
        {
            float year = absTick / 60000f % 60f / 60f;
            float p = year - Season.Winter.GetMiddleTwelfth(0f).GetBeginningYearPct();
            if (tile.Valid && Find.WorldGrid.LongLatOf(tile).y < 0f) p += 0.5f;
            return ((p % 1f) + 1f) % 1f;
        }

        private FloatRange At(int i) => i == 0 ? winter : i == 1 ? spring : i == 2 ? summer : autumn;

        /// <summary>The season's band at this point of the year, eased from one season's middle to the next.</summary>
        public FloatRange Band(float pos)
        {
            float f = pos * 4f;
            int a = Mathf.FloorToInt(f) % 4, b = (a + 1) % 4;
            float t = (1f - Mathf.Cos((f - Mathf.Floor(f)) * Mathf.PI)) * 0.5f;
            FloatRange A = At(a), B = At(b);
            return new FloatRange(Mathf.Lerp(A.min, B.min, t), Mathf.Lerp(A.max, B.max, t));
        }

        public static NorthClimate Of(PlanetTile tile)
        {
            if (!tile.Valid || Find.World?.grid == null) return null;
            try { return Find.WorldGrid[tile]?.PrimaryBiome?.GetModExtension<NorthClimate>(); }
            catch { return null; }
        }
    }

    /// <summary>The seasonal part: the middle of the season's band (the tile's own temperature is cancelled out).</summary>
    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.OffsetFromSeasonCycle))]
    public static class Patch_NorthSeason
    {
        public static void Postfix(int absTick, PlanetTile tile, ref float __result)
        {
            NorthClimate c = NorthClimate.Of(tile);
            if (c == null) return;
            if (c.relative)
            {
                float r = __result * c.swing;
                if (r < 0f) r *= c.coldScale;
                __result = r + c.bias;
                if (c.warmWeather != null && c.warmWeatherBonus != 0f)
                {
                    Map m = Current.Game?.FindMap(tile);
                    if (m != null && m.weatherManager.curWeather == c.warmWeather) __result += c.warmWeatherBonus;
                }
                return;
            }
            FloatRange band = c.Band(NorthClimate.SeasonPos(absTick, tile));
            __result = (band.min + band.max) * 0.5f + c.Shift(tile) - Find.WorldGrid[tile].temperature;
        }
    }

    /// <summary>The world inspector's "average temperature" is the tile's own: for a biome with its own climate, the
    /// climate's year average.</summary>
    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.GetAverageTemperatureLabel))]
    public static class Patch_NorthAverageLabel
    {
        public static bool Prefix(PlanetTile tile, ref string __result)
        {
            NorthClimate c = tile.Valid ? NorthClimate.Of(tile) : null;
            if (c == null) return true;
            __result = c.YearAverage(tile).ToStringTemperature() + string.Format(" ({0} {1} {2})",
                GenTemperature.MinTemperatureAtTile(tile).ToStringTemperature("F0"), "RangeTo".Translate(),
                GenTemperature.MaxTemperatureAtTile(tile).ToStringTemperature("F0"));
            return false;
        }
    }

    /// <summary>The day's swing: from the band's cold end before dawn to its warm end in the afternoon.</summary>
    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.OffsetFromSunCycle))]
    public static class Patch_NorthDay
    {
        public static void Postfix(int absTick, PlanetTile tile, ref float __result)
        {
            NorthClimate c = NorthClimate.Of(tile);
            if (c == null || c.relative) return;
            FloatRange band = c.Band(NorthClimate.SeasonPos(absTick, tile));
            __result = __result / 7f * (band.max - band.min) * 0.5f;
        }
    }

    /// <summary>Vanilla's random day-to-day swing is ±7 - too much for a climate kept to its bands: it is damped.</summary>
    [HarmonyPatch]
    public static class Patch_NorthDailyNoise
    {
        private static AccessTools.FieldRef<object, PlanetTile> tileOf;

        public static System.Reflection.MethodBase TargetMethod()
        {
            Type t = AccessTools.Inner(typeof(TileTemperaturesComp), "CachedTileTemperatureData");
            tileOf = AccessTools.FieldRefAccess<PlanetTile>(t, "tile");
            return AccessTools.Method(t, "OffsetFromDailyRandomVariation");
        }

        public static void Postfix(object __instance, ref float __result)
        {
            NorthClimate c = NorthClimate.Of(tileOf(__instance));
            if (c != null) __result *= c.dailyNoise;
        }
    }

    /// <summary>Where the north is thin: cold land in the patches of a slow noise; the waste where it is colder,
    /// the oasis where it is milder and wetter. XML (Avir.NorthPlacement on the biome) tunes it.</summary>
    /// <summary>Where a northern biome lies. One cold region of the world (a perlin field above noiseAbove, any tile no
    /// warmer than maxTemperature on average - no lower bound, arctic ice included); inside it a second, finer field
    /// picks the oases - small tight clusters, about a fifth of the region - and the waste takes the rest.</summary>
    public class NorthPlacement : DefModExtension
    {
        public float noiseAbove = 0.53f;
        public float maxTemperature = 0f;
        /// <summary>true: the oasis clusters; false: the waste around them.</summary>
        public bool oasis;
        /// <summary>The cluster field's threshold: higher, fewer and smaller oases.</summary>
        public float clusterAbove = 0.6f;
        public float score = 100f;
    }

    public class BiomeWorker_AvirNorth : BiomeWorker
    {
        private static Perlin perlin, cluster;
        private static int perlinSeed = int.MinValue;

        private static void EnsureNoise()
        {
            int seed = Gen.HashCombineInt(Find.World.info.Seed, 0x5A1D0E);
            if (perlin != null && perlinSeed == seed) return;
            perlinSeed = seed;
            // broad, smooth fields: the Avir regions lie as a few whole lands (about thirty features round the
            // planet at this frequency), not scattered patches through the other forests
            perlin = new Perlin(0.05, 2.0, 0.4, 3, normalized: true, invert: false, seed, QualityMode.Medium);
            cluster = new Perlin(0.75, 2.0, 0.45, 2, normalized: true, invert: false, Gen.HashCombineInt(seed, 0x0A515), QualityMode.Medium);
        }

        /// <summary>The mod setting "Avir regions on the world map": every noise threshold of the Avir biomes is moved by
        /// the same amount, so the north and the forest ring around it grow or shrink together.</summary>
        public static float Shifted(float threshold) => threshold - ((AvirMod.Settings?.avirRegionCoverage ?? 1f) - 1f) * 0.12f;
        public static bool CoverageOff => (AvirMod.Settings?.avirRegionCoverage ?? 1f) <= 0.001f;

        public static float NoiseAt(PlanetTile tile)
        {
            EnsureNoise();
            Vector3 c = Find.WorldGrid.GetTileCenter(tile);
            return (float)perlin.GetValue(c.x, c.y, c.z);
        }

        public static float ClusterAt(PlanetTile tile)
        {
            EnsureNoise();
            Vector3 c = Find.WorldGrid.GetTileCenter(tile);
            return (float)cluster.GetValue(c.x, c.y, c.z);
        }

        public override float GetScore(BiomeDef biome, Tile tile, PlanetTile planetTile)
        {
            if (tile.WaterCovered) return -100f;
            NorthPlacement p = biome.GetModExtension<NorthPlacement>() ?? new NorthPlacement();
            if (tile.temperature > p.maxTemperature) return 0f;
            if (CoverageOff) return 0f;
            if (NoiseAt(planetTile) < Shifted(p.noiseAbove)) return 0f;
            bool inCluster = ClusterAt(planetTile) >= p.clusterAbove;
            return inCluster == p.oasis ? p.score : 0f;
        }
    }
}
