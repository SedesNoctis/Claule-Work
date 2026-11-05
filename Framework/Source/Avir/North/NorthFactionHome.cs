using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir.North
{
    /// <summary>On a faction def: its settlements stand only on these biomes (the A'Shaign Order - the northern
    /// waste and the frost oasis). If the world has none of them, the faction settles as usual.</summary>
    public class AvirHomeBiomes : DefModExtension
    {
        public List<BiomeDef> biomes;
    }

    [HarmonyPatch(typeof(TileFinder), nameof(TileFinder.RandomSettlementTileFor), new[] { typeof(PlanetLayer), typeof(Faction), typeof(bool), typeof(Predicate<PlanetTile>) })]
    public static class Patch_AvirHomeBiomes
    {
        private static int checkedWorld = -1;
        private static readonly Dictionary<FactionDef, bool> worldHas = new Dictionary<FactionDef, bool>();

        // first of all prefixes: Faction Control replaces the search with its own prefix, which only honours the
        // validator it is handed - ours must be in it by then
        [HarmonyPriority(Priority.First)]
        public static void Prefix(PlanetLayer layer, Faction faction, ref Predicate<PlanetTile> extraValidator)
        {
            List<BiomeDef> home = faction?.def.GetModExtension<AvirHomeBiomes>()?.biomes;
            if (home.NullOrEmpty() || layer == null || !HasAny(layer, faction.def, home)) return;
            Predicate<PlanetTile> inner = extraValidator;
            extraValidator = t => home.Contains(layer[t].PrimaryBiome) && (inner == null || inner(t));
        }

        private static bool HasAny(PlanetLayer layer, FactionDef def, List<BiomeDef> home)
        {
            int seed = Find.World?.info?.Seed ?? 0;
            if (seed != checkedWorld) { checkedWorld = seed; worldHas.Clear(); }
            if (!worldHas.TryGetValue(def, out bool has))
            {
                for (int i = 0; i < layer.TilesCount && !has; i++)
                {
                    Tile tile = layer[i];
                    has = home.Contains(tile.PrimaryBiome) && tile.hilliness != Hilliness.Impassable;
                }
                worldHas[def] = has;
            }
            return has;
        }
    }
}
