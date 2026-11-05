using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace Avir.North
{
    /// <summary>
    /// The game builds a biome's animal table with Dictionary.Add: an animal listed both in the biome's own list and in
    /// its own "lives in" list for the same biome throws, the map is generated with no animals at all, and the table is
    /// left half-built for the rest of the game. Our biomes take beasts from many mods (NorthAnimals, BiomeKin, patches),
    /// so for them the table is built here, where a beast named twice simply keeps the larger weight.
    /// </summary>
    [HarmonyPatch(typeof(BiomeDef), nameof(BiomeDef.CommonalityOfAnimal))]
    public static class Patch_AvirAnimalCommonality
    {
        private static readonly AccessTools.FieldRef<BiomeDef, Dictionary<PawnKindDef, float>> Cache =
            AccessTools.FieldRefAccess<BiomeDef, Dictionary<PawnKindDef, float>>("cachedAnimalCommonalities");
        private static readonly AccessTools.FieldRef<BiomeDef, List<BiomeAnimalRecord>> Wild =
            AccessTools.FieldRefAccess<BiomeDef, List<BiomeAnimalRecord>>("wildAnimals");

        public static void Prefix(BiomeDef __instance)
        {
            if (Cache(__instance) != null || __instance.defName == null || !__instance.defName.StartsWith("Avir_")) return;
            var d = new Dictionary<PawnKindDef, float>();
            void Put(PawnKindDef k, float c) { if (k != null && (!d.TryGetValue(k, out float old) || c > old)) d[k] = c; }
            List<BiomeAnimalRecord> wild = Wild(__instance);
            if (wild != null) foreach (BiomeAnimalRecord r in wild) Put(r?.animal, r?.commonality ?? 0f);
            foreach (PawnKindDef k in DefDatabase<PawnKindDef>.AllDefs)
            {
                List<AnimalBiomeRecord> wb = k.RaceProps?.wildBiomes;
                if (wb == null) continue;
                foreach (AnimalBiomeRecord r in wb) if (r.biome == __instance) Put(k, r.commonality);
            }
            Cache(__instance) = d;
        }
    }

    public static class AvirAnimalDiagnostics
    {
        [DebugAction("Avir", "Why so few wild animals?", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Diagnose()
        {
            Map map = Find.CurrentMap;
            var sb = new StringBuilder("[Avir animals] ==== diagnosis ====\n");
            try
            {
                sb.AppendLine("biome " + map.Biome?.defName + ", tile animal density " + map.TileInfo.AnimalDensity.ToString("F2")
                    + ", outdoor " + map.mapTemperature.OutdoorTemp.ToString("F0") + ", seasonal " + map.mapTemperature.SeasonalTemp.ToString("F0"));
                sb.AppendLine(map.wildAnimalSpawner.DebugString().Replace("\n", ", "));
                foreach (BiomeDef b in map.Biomes)
                {
                    var all = b.AllWildAnimals.ToList();
                    var ok = all.Where(k => map.mapTemperature.SeasonAcceptableFor(k.race)).ToList();
                    sb.AppendLine("  " + b.defName + ": " + all.Count + " kinds listed, " + ok.Count + " can live here this season: "
                        + string.Join(", ", ok.Select(k => k.defName + " " + b.CommonalityOfAnimal(k).ToString("0.###"))));
                }
                int wild = map.mapPawns.AllPawnsSpawned.Count(p => p.Faction == null && p.RaceProps.Animal);
                sb.AppendLine("wild animals on the map now: " + wild);
                foreach (TileMutatorDef m in map.TileInfo.Mutators)
                    if (m.animalDensityFactor != 1f) sb.AppendLine("  mutator " + m.defName + " animal density x" + m.animalDensityFactor);
                foreach (GameCondition c in map.gameConditionManager.ActiveConditions)
                    sb.AppendLine("  condition " + c.def.defName);
            }
            catch (Exception e) { sb.AppendLine("diagnosis failed: " + e); }
            Log.Message(sb.ToString());
            Messages.Message("Avir: animal diagnosis written to the log.", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
