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

        public override void MapComponentTick()
        {
            if (AvirMod.Settings != null && !AvirMod.Settings.weatherWind) return;
            if (Find.TickManager.TicksGame % 60 != 17) return;
            // gases that are things
            foreach (ThingDef d in GasDefs)
                foreach (Thing t in map.listerThings.ThingsOfDef(d).ToList())
                {
                    if (!(t is Gas gas) || !Steady(t.Position, out Vector3 dir, out float s)) continue;
                    if (!Rand.Chance(Mathf.Clamp(s / 8f, 0f, 0.85f))) continue;
                    MapComponent_MagicWind.DriftGasThing(gas, map, Next(t.Position, dir, s));
                }
            // the game's gas grid - only where Atmosphere Overhaul does not move it already
            if (AtmosphereBridge.On) return;
            withGas.Clear();
            int n = map.cellIndices.NumGridCells;
            for (int i = 0; i < n; i++) if (map.gasGrid.GetDirect(i) != 0u) withGas.Add(map.cellIndices.IndexToCell(i));
            foreach (IntVec3 c in withGas.InRandomOrder())
            {
                if (!Steady(c, out Vector3 dir, out float s) || !Rand.Chance(Mathf.Clamp(s / 10f, 0f, 0.8f))) continue;
                MapComponent_MagicWind.DriftGridGas(map, c, Next(c, dir, s), dir, s * 0.6f);
            }
        }
    }
}
