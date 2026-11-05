using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.North
{
    // =====================================================================================
    // THE BURNING FOREST - a rare piece of the twilight forest where it is always autumn and the grass smoulders:
    // small fires (and now and then a golden one) flare up in the grass and die down, and the grass is never burnt
    // away; trees and plants do not burn at all. What stands on the grass can catch. Its plants give a fireproofing
    // oil when harvested or cut. With Odyssey: patches of volcanic rock, cooled lava and now and then a lava pool.
    // =====================================================================================

    /// <summary>On a biome: the season its plants always show (the burning forest: autumn).</summary>
    public class EternalSeason : DefModExtension
    {
        public Season season = Season.Fall;
    }

    /// <summary>The season the Avir plants show: the map's own, or the biome's eternal one.</summary>
    public static class AvirSeason
    {
        public static Season Of(Map map)
        {
            EternalSeason e = map?.Biome?.GetModExtension<EternalSeason>();
            return e != null ? e.season : GenLocalDate.Season(map);
        }

        /// <summary>How many days a plant turns before or after the calendar does. Every plant has its own: a third of
        /// them run 2-3 days early, a third turn on the day, a third 2-3 days late - so a forest changes colour over a
        /// week, tree by tree, not all at once.</summary>
        public static float OffsetDays(Thing t)
        {
            int id = t.thingIDNumber;
            float frac = Rand.ValueSeeded(id ^ 0x3A11);
            switch (Mathf.Abs(id * 7919 + 13) % 3)
            {
                case 0: return -(2f + frac);          // early
                case 1: return frac - 0.5f;           // on time
                default: return 2f + frac;            // late
            }
        }

        /// <summary>The season this one plant shows (eternal seasons stay eternal).</summary>
        public static Season For(Thing t)
        {
            Map map = t?.MapHeld;
            if (map == null) return Season.Undefined;
            EternalSeason e = map.Biome?.GetModExtension<EternalSeason>();
            if (e != null) return e.season;
            return For(t, Find.WorldGrid.LongLatOf(map.Tile));
        }

        /// <summary>The same, with the map's coordinates already looked up (for a pass over many plants).</summary>
        public static Season For(Thing t, Vector2 longLat)
        {
            long at = Find.TickManager.TicksAbs + (long)(OffsetDays(t) * GenDate.TicksPerDay);
            return GenDate.Season(at, longLat);
        }

        /// <summary>Whether the map is within a few days of a change of season - the only time plants need re-checking.</summary>
        public static bool NearChange(Map map)
        {
            if (map?.Biome?.GetModExtension<EternalSeason>() != null) return false;
            Vector2 ll = Find.WorldGrid.LongLatOf(map.Tile);
            long now = Find.TickManager.TicksAbs;
            return GenDate.Season(now - (long)(3.6f * GenDate.TicksPerDay), ll) != GenDate.Season(now + (long)(3.6f * GenDate.TicksPerDay), ll);
        }
    }

    /// <summary>On a biome: the smouldering grass.</summary>
    public class BurningForest : DefModExtension
    {
        /// <summary>Embers kept alight on a 250x250 map (scaled by area); fewer in rain.</summary>
        public float embersPer62500 = 30f;
        /// <summary>Share of new embers that flare up as golden fire.</summary>
        public float goldenShare = 0.25f;
        /// <summary>How long an ember on bare grass burns (ticks).</summary>
        public IntRange emberLife = new IntRange(3000, 8000);
        /// <summary>Chance an ember spreads when the fire would (every few seconds) - along the grass, or onto what
        /// stands near.</summary>
        public float spreadChance = 0.25f;
        /// <summary>A fire that started in the grass spreads no farther than this from where it started.</summary>
        public float spreadRadius = 15f;
        /// <summary>With Odyssey: the share of the ponds (and the landmarks' still water) that is lava instead.</summary>
        public float lavaPondChance = 0.5f;
        /// <summary>Glow colours of the night plants here (hot).</summary>
        public List<ColorInt> glow;
        /// <summary>Plants that keep their own glow here (the mirror tree).</summary>
        public List<ThingDef> keepGlow;
        /// <summary>With Odyssey: patches of volcanic rock and cooled lava, and the chance of a lava pool.</summary>
        public IntRange rockPatches = new IntRange(0, 2);
        /// <summary>Lava pools: lava in the middle, cooled lava around, volcanic rock at the rim.</summary>
        public IntRange lavaPools = new IntRange(3, 7);

        public static BurningForest Of(Map map) => map?.Biome?.GetModExtension<BurningForest>();
    }

    public class MapComponent_BurningForest : MapComponent
    {
        private readonly Dictionary<Fire, int> born = new Dictionary<Fire, int>();
        /// <summary>Where each grass fire's line started (spread keeps within spreadRadius of it).</summary>
        public readonly Dictionary<Fire, IntVec3> origin = new Dictionary<Fire, IntVec3>();
        private BurningForest ext;
        private bool checkedExt;

        public MapComponent_BurningForest(Map map) : base(map) { }

        public BurningForest Ext
        {
            get
            {
                if (!checkedExt) { checkedExt = true; ext = BurningForest.Of(map); }
                return ext;
            }
        }

        public static MapComponent_BurningForest Of(Map m) => m?.GetComponent<MapComponent_BurningForest>();

        /// <summary>A fire on bare growth: nothing but plants (and the ground) burns in its cell.</summary>
        public static bool OnBareGrowth(Fire f)
        {
            if (f.parent != null || !f.Spawned) return false;
            List<Thing> list = f.Map.thingGrid.ThingsListAtFast(f.Position);
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t is Fire || t is Plant || t is Mote) continue;
                if (t.def.useHitPoints && t.GetStatValue(StatDefOf.Flammability) > 0.01f) return false;
            }
            return true;
        }

        public int EmberCount
        {
            get
            {
                int n = 0;
                foreach (var kv in born) if (kv.Key.Spawned) n++;
                return n;
            }
        }

        public override void MapComponentTick()
        {
            BurningForest e = Ext;
            if (e == null) return;
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 113) return;
            // take note of every fire on growth; put out those whose time has come
            List<Thing> fires = map.listerThings.ThingsInGroup(ThingRequestGroup.Fire);
            var seen = new HashSet<Fire>();
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                if (!(fires[i] is Fire f) || !f.Spawned || !OnBareGrowth(f)) continue;
                seen.Add(f);
                if (!born.TryGetValue(f, out int b)) { born[f] = now + e.emberLife.RandomInRange; continue; }
                if (now >= b) f.Destroy();
            }
            var gone = new List<Fire>();
            foreach (var kv in born) if (!seen.Contains(kv.Key) || !kv.Key.Spawned) gone.Add(kv.Key);
            foreach (Fire f in gone) { born.Remove(f); origin.Remove(f); }
            // and light new ones in the grass
            float want = e.embersPer62500 * map.Area / 62500f;
            if (map.weatherManager.RainRate > 0.01f) want *= 0.3f;
            int have = seen.Count;
            for (int k = 0; k < 3 && have < want; k++)
            {
                IntVec3 c = CellFinder.RandomCell(map);
                Plant p = c.GetPlant(map);
                if (p == null || p.def.plant.IsTree || p.def.plant.maxMeshCount <= 1) continue;      // grass only
                if (c.Roofed(map) || c.GetFirstThing<Fire>(map) != null || map.areaManager.Home[c]) continue;
                bool golden = Rand.Chance(e.goldenShare) && Artifactorics.EssentiumFireDefOf.Avir_GoldFire != null;
                bool ok = golden ? Artifactorics.EssentiumFire.StartIn(Artifactorics.EssentiumFireDefOf.Avir_GoldFire, c, map, 0.1f, null)
                                 : FireUtility.TryStartFireIn(c, map, 0.1f, null);
                if (ok) { have++; Fire nf = c.GetFirstThing<Fire>(map); if (nf != null) origin[nf] = c; }
            }
        }
    }

    /// <summary>In the burning forest no plant takes harm from fire - the grass smoulders and is never burnt away.</summary>
    [HarmonyPatch(typeof(Fire), "DoFireDamage")]
    public static class Patch_BurningForestPlantsDoNotBurn
    {
        public static bool Prefix(Fire __instance, Thing targ)
        {
            if (targ is Plant && __instance.Spawned && MapComponent_BurningForest.Of(__instance.Map)?.Ext != null) return false;
            if (targ != null && Fireproofing.IsProtected(targ)) return false;
            return true;
        }
    }

    /// <summary>An ember on the grass stays small and seldom spreads (to grass or to whatever stands near).</summary>
    [HarmonyPatch(typeof(Fire), "DoComplexCalcs")]
    public static class Patch_BurningForestEmbersStaySmall
    {
        public static void Postfix(Fire __instance)
        {
            if (!__instance.Spawned || __instance.Destroyed) return;
            if (MapComponent_BurningForest.Of(__instance.Map)?.Ext == null) return;
            if (MapComponent_BurningForest.OnBareGrowth(__instance) && __instance.fireSize > 0.3f) __instance.fireSize = 0.3f;
        }
    }

    /// <summary>A grass fire spreads by itself here: to a neighbouring cell (grass, or whatever stands there), now and
    /// then, and never farther than spreadRadius from where its line started; past three times the embers the forest
    /// keeps, it waits. Golden fire spreads as golden fire.</summary>
    [HarmonyPatch(typeof(Fire), "TrySpread")]
    public static class Patch_BurningForestEmbersSpread
    {
        public static bool Prefix(Fire __instance)
        {
            if (!__instance.Spawned) return true;
            Map map = __instance.Map;
            MapComponent_BurningForest bf = MapComponent_BurningForest.Of(map);
            BurningForest e = bf?.Ext;
            if (e == null || !MapComponent_BurningForest.OnBareGrowth(__instance)) return true;
            if (!Rand.Chance(e.spreadChance)) return false;
            if (bf.EmberCount > e.embersPer62500 * map.Area / 62500f * 3f) return false;
            IntVec3 from = bf.origin.TryGetValue(__instance, out IntVec3 o) ? o : __instance.Position;
            IntVec3 c = __instance.Position + GenRadial.ManualRadialPattern[Rand.RangeInclusive(1, 8)];
            if (!c.InBounds(map) || (c - from).LengthHorizontal > e.spreadRadius || c.GetFirstThing<Fire>(map) != null) return false;
            if (!Rand.Chance(FireUtility.ChanceToStartFireIn(c, map))) return false;
            bool ok = __instance is Artifactorics.EssentiumFire
                ? Artifactorics.EssentiumFire.StartIn(__instance.def, c, map, 0.1f, __instance.instigator)
                : FireUtility.TryStartFireIn(c, map, 0.1f, __instance.instigator);
            if (ok) { Fire nf = c.GetFirstThing<Fire>(map); if (nf != null) bf.origin[nf] = from; }
            return false;
        }
    }

    /// <summary>The burning forest does not call the rain down on its own fires.</summary>
    [HarmonyPatch(typeof(FireWatcher), nameof(FireWatcher.LargeFireDangerPresent), MethodType.Getter)]
    public static class Patch_BurningForestNoFireRain
    {
        private static readonly AccessTools.FieldRef<FireWatcher, Map> mapOf = AccessTools.FieldRefAccess<FireWatcher, Map>("map");
        public static void Postfix(FireWatcher __instance, ref bool __result)
        {
            if (__result && MapComponent_BurningForest.Of(mapOf(__instance))?.Ext != null) __result = false;
        }
    }

    /// <summary>The night plants of the burning forest glow hot: amber, orange, red.</summary>
    public static class BurningGlow
    {
        public static readonly ColorInt[] Default = { new ColorInt(200, 120, 45, 0), new ColorInt(200, 80, 40, 0), new ColorInt(190, 150, 60, 0), new ColorInt(180, 60, 35, 0) };

        public static bool TryGet(Thing t, out ColorInt c)
        {
            c = default;
            BurningForest e = t.Spawned ? MapComponent_BurningForest.Of(t.Map)?.Ext : null;
            if (e == null) return false;
            if (e.keepGlow != null && e.keepGlow.Contains(t.def)) return false;      // the mirror trees keep their own light
            IList<ColorInt> pal = e.glow != null && e.glow.Count > 0 ? (IList<ColorInt>)e.glow : Default;
            c = pal[Mathf.Abs(t.thingIDNumber * 37 + 11) % pal.Count];
            return true;
        }
    }

    // ---------------------------------------------------------------- the ground: volcanic rock and lava (Odyssey)
    public class GenStep_BurningGround : GenStep
    {
        public override int SeedPart => 0x5A1D11;

        public override void Generate(Map map, GenStepParams parms)
        {
            BurningForest e = map.Biome?.GetModExtension<BurningForest>();
            if (e == null || !ModsConfig.OdysseyActive) return;
            TerrainDef rock = DefDatabase<TerrainDef>.GetNamedSilentFail("VolcanicRock");
            TerrainDef cooled = DefDatabase<TerrainDef>.GetNamedSilentFail("CooledLava");
            TerrainDef lavaS = DefDatabase<TerrainDef>.GetNamedSilentFail("LavaShallow");
            TerrainDef lavaD = DefDatabase<TerrainDef>.GetNamedSilentFail("LavaDeep");
            if (rock == null) return;
            // half the still water (the forest's ponds and the landmarks' lakes) is lava instead, each body as a whole
            if (lavaS != null)
            {
                AvirPonds ponds = map.Biome.GetModExtension<AvirPonds>();
                var seen = new HashSet<IntVec3>();
                foreach (IntVec3 c in map.AllCells)
                {
                    if (seen.Contains(c)) continue;
                    TerrainDef t = c.GetTerrain(map);
                    if (!Still(t, ponds)) continue;
                    var body = new List<IntVec3>();
                    map.floodFiller.FloodFill(c, x => Still(x.GetTerrain(map), ponds), x => { body.Add(x); seen.Add(x); });
                    if (!Rand.Chance(e.lavaPondChance)) continue;
                    foreach (IntVec3 x in body)
                        map.terrainGrid.SetTerrain(x, x.GetTerrain(map).IsWater && IsDeep(x.GetTerrain(map), ponds) && lavaD != null ? lavaD : lavaS);
                    foreach (IntVec3 x in body)
                        foreach (IntVec3 n in GenAdj.CellsAdjacent8Way(new TargetInfo(x, map)))
                            if (n.InBounds(map) && !n.GetTerrain(map).IsWater && n.GetTerrain(map) != lavaS && n.GetTerrain(map) != lavaD && n.GetEdifice(map) == null)
                            { map.terrainGrid.SetTerrain(n, cooled ?? rock); Plant p = n.GetPlant(map); if (p != null) p.Destroy(); }
                }
            }
            int patches = e.rockPatches.RandomInRange;
            for (int k = 0; k < patches; k++) Blob(map, rock, cooled, 4f, 9f, false, null, null);
            if (lavaS != null)
            {
                int pools = e.lavaPools.RandomInRange;
                for (int k = 0; k < pools; k++) Blob(map, cooled ?? rock, rock, 3.5f, 7f, true, lavaS, lavaD);
            }
        }

        private static bool Still(TerrainDef t, AvirPonds p) =>
            t != null && (t == TerrainDefOf.WaterShallow || t == TerrainDefOf.WaterDeep || (p != null && (t == p.shallow || t == p.deep)));
        private static bool IsDeep(TerrainDef t, AvirPonds p) => t == TerrainDefOf.WaterDeep || (p != null && t == p.deep);

        private static void Blob(Map map, TerrainDef main, TerrainDef rim, float rMin, float rMax, bool lava, TerrainDef lavaS, TerrainDef lavaD)
        {
            for (int tries = 0; tries < 60; tries++)
            {
                IntVec3 c = new IntVec3(Rand.Range(20, map.Size.x - 20), 0, Rand.Range(20, map.Size.z - 20));
                if (c.GetEdifice(map) != null || c.GetTerrain(map).IsWater || c.GetTerrain(map).defName.StartsWith("Lava")) continue;
                if (lava && GenRadial.RadialCellsAround(c, rMax * 2.2f, true).Any(x => x.InBounds(map) && x.GetTerrain(map).defName.StartsWith("Lava"))) continue;
                float r = Rand.Range(rMin, rMax);
                float seed = Rand.Value * 100f;
                foreach (IntVec3 x in GenRadial.RadialCellsAround(c, r + 2.5f, true))
                {
                    if (!x.InBounds(map) || x.GetEdifice(map) != null || x.GetTerrain(map).IsWater || x.GetTerrain(map).defName.StartsWith("Lava")) continue;
                    float d = (x - c).LengthHorizontal / (r * (0.8f + 0.4f * Mathf.PerlinNoise(x.x * 0.25f + seed, x.z * 0.25f)));
                    if (d > 1.25f) continue;
                    TerrainDef t = d > 1f ? rim : main;
                    if (lava && d < 0.45f) t = lavaD ?? lavaS;
                    else if (lava && d < 0.75f) t = lavaS;
                    if (t == null) continue;
                    map.terrainGrid.SetTerrain(x, t);
                    if (d <= 1f) { Plant p = x.GetPlant(map); if (p != null && (lava || !p.def.plant.IsTree)) p.Destroy(); }
                }
                return;
            }
        }
    }

    /// <summary>Steam: a fog that drifts and rises (the burning forest's winter weather).</summary>
    [StaticConstructorOnStartup]
    public class WeatherOverlay_AvirSteam : WeatherOverlayDualPanner
    {
        private static readonly Material SteamMat = Make();

        private static Material Make()
        {
            Material m = new Material(MatLoader.LoadMat("Weather/FogOverlayWorld"));
            if (m.HasProperty("_Color")) m.color = new Color(1f, 0.95f, 0.9f, m.color.a * 0.9f);
            return m;
        }

        public WeatherOverlay_AvirSteam()
        {
            worldOverlayMat = SteamMat;
            worldOverlayPanSpeed1 = 0.0022f;
            worldOverlayPanSpeed2 = 0.0016f;
            worldPanDir1 = new Vector2(0.35f, 1f);
            worldPanDir2 = new Vector2(-0.3f, 1f);
        }
    }
}
