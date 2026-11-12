using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The wind of the weather and the winds from outside (the author, Fix177). The map has a direction of wind,
    /// turning slowly. In rain, storms, snowstorms and windy weather (the game's own, ReGrowth's, ours) gusts now and then
    /// cross the map as a front - out of doors only, with lee behind walls: people stagger, light things are carried off,
    /// fire is driven on. Our howling storm and blizzard lash bring hard, cold squalls. And where the stream rages (its
    /// tempest tiers), arcane gusts break out - violet, charged with lightning, by doors or not.
    /// With Atmosphere Overhaul loaded, the weather's wind is left to it; the arcane gusts remain ours.</summary>
    public class MapComponent_WeatherWind : MapComponent
    {
        public float angle = -1f;
        public MapComponent_WeatherWind(Map map) : base(map) { }

        public override void ExposeData() { Scribe_Values.Look(ref angle, "angle", -1f); }

        public Vector3 Dir => Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

        private bool Weather(params string[] bits) { string d = map.weatherManager.curWeather?.defName ?? ""; return bits.Any(b => d.IndexOf(b, System.StringComparison.OrdinalIgnoreCase) >= 0); }
        private bool Condition(string bit) => map.gameConditionManager.ActiveConditions.Any(c => c.def.defName.IndexOf(bit, System.StringComparison.OrdinalIgnoreCase) >= 0);

        public override void MapComponentTick()
        {
            if (!AvirMagic.On || AvirMod.Settings != null && !AvirMod.Settings.weatherWind) return;
            int now = Find.TickManager.TicksGame;
            if (angle < 0f) angle = Rand.Range(0f, 360f);
            if (now % 250 == 0) angle = (angle + Rand.Range(-8f, 8f) + 360f) % 360f;
            if (now % 600 != 37) return;
            WeatherGust();
            ArcaneGust();
        }

        private Vector3 SomeOutdoorSpot()
        {
            for (int i = 0; i < 20; i++) { IntVec3 c = CellFinder.RandomCell(map); if (!c.Roofed(map) && !c.Fogged(map)) return c.ToVector3Shifted(); }
            return CellFinder.RandomCell(map).ToVector3Shifted();
        }

        [LudeonTK.DebugAction("Magic", "Wind: weather gust now (34 m/s front)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevWeather() { var c = Find.CurrentMap?.GetComponent<MapComponent_WeatherWind>(); if (c == null) return; if (c.angle < 0f) c.angle = 90f; var g = MapComponent_MagicWind.Blow(c.map, MapComponent_MagicWind.Shape.Front, Verse.UI.MouseCell().ToVector3Shifted(), 12f, 34f, 300, null, c.Dir); if (g != null) g.weather = true; }

        [LudeonTK.DebugAction("Magic", "Wind: arcane gust now", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevArcane() { var c = Find.CurrentMap?.GetComponent<MapComponent_WeatherWind>(); if (c == null) return; if (c.angle < 0f) c.angle = 90f; var g = MapComponent_MagicWind.Blow(c.map, MapComponent_MagicWind.Shape.Round, Verse.UI.MouseCell().ToVector3Shifted(), 3.2f, 46f, 420, null, c.Dir, true); if (g != null) g.tint = new Color(0.72f, 0.5f, 1f, 0.85f); }

        private void WeatherGust()
        {
            if (AtmosphereBridge.On) return;                        // its own wind blows there
            if (Condition("NoWind")) return;
            float baseWind = map.windManager.WindSpeed;             // the game's: about 0 to 2
            bool howling = Weather("Howling", "BlizzardLash");
            bool stormy = howling || Weather("Rain", "Storm", "Thunder", "Blizzard", "Snow", "Windy", "Sand", "Dust") || Condition("RagingWind") || Condition("Windy");
            if (!stormy && baseWind < 1.2f) return;
            float chance = Mathf.Clamp01((baseWind - 0.5f) * 0.35f) + (stormy ? 0.15f : 0f) + (howling ? 0.25f : 0f) + (Condition("RagingWind") ? 0.3f : 0f);
            if (!Rand.Chance(chance)) return;
            float s = 12f + baseWind * 9f + (stormy ? 8f : 0f) + (howling ? 16f : 0f) + (Condition("RagingWind") ? 14f : 0f) + Rand.Range(-3f, 6f);
            var g = MapComponent_MagicWind.Blow(map, MapComponent_MagicWind.Shape.Front, SomeOutdoorSpot(), Rand.Range(9f, 15f), s * (AvirMod.Settings?.atmoWindScale ?? 1f), Rand.Range(150, 320), null, Dir);
            if (g != null) { g.weather = true; if (howling) g.tint = new Color(0.8f, 0.9f, 1f, 0.8f); }
        }

        private void ArcaneGust()
        {
            int tier = Spells.FlowTierAny(map);
            if (tier < 3 || !Rand.Chance(0.08f * (tier - 2))) return;
            bool round = Rand.Chance(0.4f);
            float s = 34f + 12f * (tier - 3) + Rand.Range(-4f, 10f);
            var g = MapComponent_MagicWind.Blow(map, round ? MapComponent_MagicWind.Shape.Round : MapComponent_MagicWind.Shape.Front, SomeOutdoorSpot(), round ? 3.2f : Rand.Range(7f, 11f), s, round ? 420 : 200, null, Dir, true);
            if (g == null) return;
            g.tint = new Color(0.72f, 0.5f, 1f, 0.85f);
            Messages.Message("The stream is raging: an arcane gust breaks loose.", new TargetInfo(g.at.ToIntVec3(), map), MessageTypeDefOf.NeutralEvent, false);
        }
    }

    /// <summary>The steady wind carries gas (Fix188). With Atmosphere Overhaul its own wind moves the game's gas grid, but
    /// not a gas that is a thing - our scalding steam and marsh gas, a mod's clouds - so those ride its wind here, read
    /// cell by cell. Without it the game's breeze (out of doors, the weather wind's direction) moves both: the grid and the
    /// things. Once a second; the stronger the wind, the likelier a cell moves on.</summary>
    public class MapComponent_GasDrift : MapComponent
    {
        private static List<ThingDef> gasDefs;
        private readonly List<IntVec3> withGas = new List<IntVec3>();
        public MapComponent_GasDrift(Map map) : base(map) { }

        private static List<ThingDef> GasDefs => gasDefs ?? (gasDefs = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.thingClass != null && typeof(Gas).IsAssignableFrom(d.thingClass)).ToList());

        /// <summary>The steady wind at a cell: direction and metres a second.</summary>
        private bool Steady(IntVec3 c, out Vector3 dir, out float s)
        {
            dir = Vector3.zero; s = 0f;
            if (AtmosphereBridge.On)
            {
                if (!AtmosphereBridge.WindAt(map, c, out Vector2 w)) return false;
                s = w.magnitude; if (s < 0.5f) return false;
                dir = new Vector3(w.x, 0f, w.y) / s;
                return true;
            }
            if (c.Roofed(map)) return false;
            s = map.windManager.WindSpeed * 6f;   // the game's 0..2 - a breeze to a gale
            if (s < 1.5f) return false;
            var ww = map.GetComponent<MapComponent_WeatherWind>();
            dir = ww != null ? ww.Dir : Vector3.forward;
            return true;
        }

        private static IntVec3 Next(IntVec3 c, Vector3 dir, float s) => (c.ToVector3Shifted() + dir * (s > 14f ? 2f : 1.2f)).ToIntVec3();

        /// <summary>How much one pass may move (Fix250): the pass is once a second, and a map full of gas must not cost more
        /// than a small map does. What is not reached this second is reached the next.</summary>
        private const int GridBudget = 1200, ThingBudget = 300;
        private readonly List<int> gasCells = new List<int>();
        private MapComponent_WeatherWind weather;
        public static int LastGridCells, LastGridMoved, LastThingMoved;   // for the dev readout

        public override void MapComponentTick()
        {
            if (AvirMod.Settings != null && !AvirMod.Settings.weatherWind) return;
            if (Find.TickManager.TicksGame % 60 != 17) return;
            bool ao = AtmosphereBridge.On;
            // the game's breeze is one for the whole map: read once, not for every cell (Fix250)
            Vector3 gdir = Vector3.forward; float gs = 0f;
            if (!ao)
            {
                gs = map.windManager.WindSpeed * 6f;
                if (gs < 1.5f) { LastGridMoved = LastThingMoved = 0; return; }
                weather = weather ?? map.GetComponent<MapComponent_WeatherWind>();
                gdir = weather != null ? weather.Dir : Vector3.forward;
            }
            // gases that are things
            int tried = 0; LastThingMoved = 0;
            foreach (ThingDef d in GasDefs)
            {
                List<Thing> list = map.listerThings.ThingsOfDef(d);
                int cnt = list.Count;
                if (cnt == 0) continue;
                int start = Rand.Range(0, cnt);
                for (int k = 0; k < cnt && tried < ThingBudget; k++)
                {
                    if (!(list[(start + k) % cnt] is Gas gas) || !gas.Spawned) continue;
                    Vector3 dir = gdir; float s = gs;
                    if (ao) { if (!Steady(gas.Position, out dir, out s)) continue; }
                    else if (gas.Position.Roofed(map)) continue;
                    tried++;
                    if (!Rand.Chance(Mathf.Clamp(s / 8f, 0f, 0.85f))) continue;
                    if (MapComponent_MagicWind.DriftGasThing(gas, map, Next(gas.Position, dir, s))) LastThingMoved++;
                }
                if (tried >= ThingBudget) break;
            }
            // the game's gas grid - only where Atmosphere Overhaul does not move it already
            if (ao) return;
            gasCells.Clear();
            int n = map.cellIndices.NumGridCells;
            for (int i = 0; i < n; i++) if (map.gasGrid.GetDirect(i) != 0u) gasCells.Add(i);
            LastGridCells = gasCells.Count; LastGridMoved = 0;
            int total = gasCells.Count;
            if (total == 0) return;
            int passes = Mathf.Min(total, GridBudget);
            float chance = Mathf.Clamp(gs / 10f, 0f, 0.8f);
            IntVec3 step = Next(IntVec3.Zero, gdir, gs) - IntVec3.Zero;
            for (int k = 0; k < passes; k++)
            {
                // a few of many: picked at random; all of few: shuffled as they are taken
                int pick = Rand.Range(k, total);
                int idx = gasCells[pick]; gasCells[pick] = gasCells[k]; gasCells[k] = idx;
                IntVec3 c = map.cellIndices.IndexToCell(idx);
                if (c.Roofed(map) || !Rand.Chance(chance)) continue;
                MapComponent_MagicWind.DriftGridGas(map, c, c + step, gdir, gs * 0.6f);
                LastGridMoved++;
            }
        }

        [LudeonTK.DebugAction("Magic", "Wind: gas drift readout (log)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevReadout()
        {
            Map m = Find.CurrentMap; if (m == null) return;
            int cells = 0, n = m.cellIndices.NumGridCells;
            for (int i = 0; i < n; i++) if (m.gasGrid.GetDirect(i) != 0u) cells++;
            int things = GasDefs.Sum(d => m.listerThings.ThingsOfDef(d).Count);
            Log.Message("[Avir] gas drift: " + cells + " grid cells with gas, " + things + " gas things; last pass moved " + LastGridMoved + " cells (budget " + GridBudget + ") and " + LastThingMoved + " things (budget " + ThingBudget + "); wind " + (m.windManager.WindSpeed * 6f).ToString("0.0") + " m/s" + (AtmosphereBridge.On ? ", Atmosphere Overhaul moves the grid" : ""));
        }

        [LudeonTK.DebugAction("Magic", "Wind: fill 40x40 with smoke here (stress)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevFill()
        {
            Map m = Find.CurrentMap; if (m == null) return;
            foreach (IntVec3 c in CellRect.CenteredOn(Verse.UI.MouseCell(), 20)) if (c.InBounds(m) && !c.Filled(m)) GasUtility.AddGas(c, m, GasType.BlindSmoke, 255);
        }
    }
}
