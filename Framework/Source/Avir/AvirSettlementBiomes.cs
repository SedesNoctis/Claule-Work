using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir
{
    /// <summary>
    /// Keeps a faction's settlements out of some biomes at world generation. The vanilla
    /// minSettlementTemperatureChanceCurve only knows a tile's coldest temperature, so it cannot
    /// tell a mild desert from a mild forest; this adds a biome filter on top of it.
    ///   excludedBiomes          - exact biomes
    ///   excludedBiomeKeywords   - any biome whose defName contains one of these (case-insensitive),
    ///                             so modded deserts and jungles are caught too
    /// Used by the A'Shaign Order and the A'Schialis Legion (cold and temperate lands only).
    /// </summary>
    public class AvirSettlementBiomeExtension : DefModExtension
    {
        public List<BiomeDef> excludedBiomes = new List<BiomeDef>();
        public List<string> excludedBiomeKeywords = new List<string>();
        /// <summary>If set: the faction settles only in these biomes (when the world has any of them at all).</summary>
        public List<BiomeDef> onlyBiomes = new List<BiomeDef>();

        public bool Allows(BiomeDef b, bool strict = false)
        {
            if (b == null) return true;
            if (strict && !onlyBiomes.NullOrEmpty() && !onlyBiomes.Contains(b)) return false;
            if (excludedBiomes != null && excludedBiomes.Contains(b)) return false;
            if (excludedBiomeKeywords != null)
                foreach (string k in excludedBiomeKeywords)
                    if (!k.NullOrEmpty() && b.defName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(TileFinder), nameof(TileFinder.RandomSettlementTileFor),
        new[] { typeof(PlanetLayer), typeof(Faction), typeof(bool), typeof(Predicate<PlanetTile>) })]
    public static class Patch_AvirSettlementBiomes
    {
        // first of all prefixes: Faction Control replaces the search with its own prefix, which only honours the
        // validator it is handed - ours must be in it by then
        [HarmonyPriority(Priority.First)]
        public static void Prefix(PlanetLayer layer, Faction faction, ref Predicate<PlanetTile> extraValidator)
        {
            AvirSettlementBiomeExtension ext = faction?.def?.GetModExtension<AvirSettlementBiomeExtension>();
            if (ext == null || layer == null) return;
            Predicate<PlanetTile> inner = extraValidator;
            PlanetLayer l = layer;
            bool strict = !ext.onlyBiomes.NullOrEmpty() && WorldHas(l, ext.onlyBiomes);
            extraValidator = t => ext.Allows(l[t]?.PrimaryBiome, strict) && (inner == null || inner(t));
        }

        // whether the world has any tile of these biomes (a world made without the Avir regions keeps the old rules)
        private static readonly Dictionary<(int, string), bool> has = new Dictionary<(int, string), bool>();
        private static bool WorldHas(PlanetLayer l, List<BiomeDef> biomes)
        {
            var key = (Find.World?.info?.Seed ?? 0, string.Join(",", biomes.ConvertAll(b => b?.defName)));
            if (has.TryGetValue(key, out bool v)) return v;
            v = false;
            for (int i = 0; i < l.TilesCount && !v; i++) if (biomes.Contains(l[i]?.PrimaryBiome)) v = true;
            if (has.Count > 16) has.Clear();
            has[key] = v;
            return v;
        }
    }
}
