using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.North
{
    // =====================================================================================
    // THE AVIR SKIES. Over the four Avir lands the evening and the night are not plain: as the sun goes down
    // the sky takes on the land's own colours and slowly shifts between them - violet and teal over the
    // twilight forest, ember red and gold over the burning one, emerald over the frost oasis, ice-white and blue
    // over the waste - and the night is a little lighter for it. On most evenings (chance on the biome), and
    // never against another sky: an eclipse, a vanilla aurora, smoke or anything else that colours the sky
    // takes over and ours stands aside.
    //
    // Those who watch it under the open sky are moved by it; the Avir, whose eyes take in more colours,
    // more so.
    // =====================================================================================
    public class AvirSkyGlow : DefModExtension
    {
        public List<Color> colors = new List<Color>();
        /// <summary>How much of the sky the colours take at full night (0..1).</summary>
        public float strength = 0.55f;
        /// <summary>The least light of the night while the sky glows.</summary>
        public float nightGlow = 0.22f;
        /// <summary>The share of evenings that bring it.</summary>
        public float chance = 0.8f;
        /// <summary>Hours a colour takes to fade into the next.</summary>
        public float cycleHours = 1.5f;
        /// <summary>The sky of each season: its own colours, brightness and frequency. A season left out keeps the
        /// colours, strength and chance above. Seasons hand over to each other over a few days.</summary>
        public List<AvirSkySeason> seasons = new List<AvirSkySeason>();

        public AvirSkySeason For(Season s)
        {
            if (s == Season.PermanentSummer) s = Season.Summer;
            if (s == Season.PermanentWinter) s = Season.Winter;
            foreach (AvirSkySeason x in seasons) if (x.season == s) return x;
            return null;
        }
    }

    public class AvirSkySeason
    {
        public Season season = Season.Summer;
        public List<Color> colors = new List<Color>();
        /// <summary>Multiplies the biome's strength (how much of the sky it takes).</summary>
        public float strength = 1f;
        /// <summary>Multiplies the biome's chance of a glowing evening.</summary>
        public float chance = 1f;
    }

    /// <summary>The sky's palette, strength and chance for the moment, blended across the days around a change of
    /// season so the colours turn slowly, as the land's real calendar (hemisphere and all) turns.</summary>
    public struct AvirSkyNow
    {
        public Color[] colors;
        public float strength, chance;

        private const int Samples = 5;          // days looked at, centred on today

        public static AvirSkyNow Of(Map map, AvirSkyGlow e)
        {
            int n = Mathf.Max(1, e.colors.Count);
            foreach (AvirSkySeason x in e.seasons) n = Mathf.Max(n, x.colors.Count);
            var now = new AvirSkyNow { colors = new Color[n] };
            Vector2 longLat = Find.WorldGrid.LongLatOf(map.Tile);
            long abs = Find.TickManager.TicksAbs;
            for (int k = 0; k < Samples; k++)
            {
                Season s = GenDate.Season(abs + (k - Samples / 2) * (long)GenDate.TicksPerDay, longLat);
                AvirSkySeason x = e.For(s);
                List<Color> cols = x != null && x.colors.Count > 0 ? x.colors : e.colors;
                for (int i = 0; i < n; i++)
                    now.colors[i] += cols.Count == 0 ? Color.white : cols[i % cols.Count];
                now.strength += e.strength * (x?.strength ?? 1f);
                now.chance += e.chance * (x?.chance ?? 1f);
            }
            for (int i = 0; i < n; i++) now.colors[i] /= Samples;
            now.strength /= Samples;
            now.chance = Mathf.Clamp01(now.chance / Samples);
            return now;
        }
    }

    public class MapComponent_AvirSky : MapComponent
    {
        private bool tonight;
        private bool wasDay = true;
        private float intensity;

        public MapComponent_AvirSky(Map map) : base(map) { }

        public AvirSkyGlow Ext => map.Biome?.GetModExtension<AvirSkyGlow>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tonight, "avirSkyTonight");
            Scribe_Values.Look(ref wasDay, "avirSkyWasDay", true);
        }

        /// <summary>0 by day, rising through the evening to 1 in the full night - when tonight is a night for it.</summary>
        public float Intensity => intensity;

        private static readonly List<GameCondition> tmp = new List<GameCondition>();

        public static bool OtherSky(Map map)
        {
            if (map.gameConditionManager.IsAlwaysDarkOutside) return true;
            tmp.Clear();
            map.gameConditionManager.GetAllGameConditionsAffectingMap(map, tmp);
            bool other = false;
            foreach (GameCondition c in tmp) if (c.SkyTarget(map).HasValue) { other = true; break; }
            tmp.Clear();
            return other;
        }

        private AvirSkyNow now;
        private int nowAt = -999999;

        /// <summary>This season's sky (refreshed every couple of in-game hours).</summary>
        public AvirSkyNow Now(AvirSkyGlow e)
        {
            int t = Find.TickManager.TicksGame;
            if (now.colors == null || t - nowAt > 5000 || t < nowAt) { now = AvirSkyNow.Of(map, e); nowAt = t; }
            return now;
        }

        public Color CurrentColor(AvirSkyGlow e)
        {
            Color[] cols = Now(e).colors;
            if (cols == null || cols.Length == 0) return Color.white;
            if (cols.Length == 1) return cols[0];
            float t = Find.TickManager.TicksAbs / (2500f * Mathf.Max(0.1f, e.cycleHours));
            int i = Mathf.FloorToInt(t) % cols.Length;
            float f = t - Mathf.Floor(t);
            f = f * f * (3f - 2f * f);
            return Color.Lerp(cols[i], cols[(i + 1) % cols.Length], f);
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 != 17) return;
            AvirSkyGlow e = Ext;
            if (e == null) { intensity = 0f; return; }
            float sun = GenCelestial.CurCelestialSunGlow(map);
            bool day = sun > 0.6f;
            if (wasDay && !day) tonight = Rand.Chance(Now(e).chance);   // a new evening
            wasDay = day;
            float target = tonight && !OtherSky(map) ? Mathf.InverseLerp(0.6f, 0.15f, sun) : 0f;
            intensity = Mathf.MoveTowards(intensity, target, 0.02f);

            // the watchers: once an hour, everyone awake under the open sky while it is bright enough
            if (intensity > 0.35f && Find.TickManager.TicksGame % 2500 == 17 + 60 * 3)
            {
                ThoughtDef plain = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_SawAvirSky");
                ThoughtDef avir = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_SawAvirSkyAvir");
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                {
                    if (p.needs?.mood == null || !p.Awake() || p.Position.Roofed(map)) continue;
                    if (!PawnCapacityUtility.CapableOf(p, PawnCapacityDefOf.Sight)) continue;
                    ThoughtDef t = AvirBody.IsAvir(p) && avir != null ? avir : plain;
                    if (t != null) p.needs.mood.thoughts.memories.TryGainMemory(t);
                }
            }
        }
    }

    internal static class PawnCapacityUtility
    {
        public static bool CapableOf(Pawn p, PawnCapacityDef c) => p.health?.capacities == null || p.health.capacities.CapableOf(c);
    }

    /// <summary>Lays our sky over whatever the weather made of it - after the conditions, so it can lighten the night
    /// (the conditions' own path may only darken). Only where nothing else colours the sky.</summary>
    [HarmonyPatch(typeof(SkyManager), "CurrentSkyTarget")]
    public static class Patch_AvirSky
    {
        private static readonly AccessTools.FieldRef<SkyManager, Map> MapRef = AccessTools.FieldRefAccess<SkyManager, Map>("map");

        public static void Postfix(SkyManager __instance, ref SkyTarget __result)
        {
            Map map = MapRef(__instance);
            MapComponent_AvirSky c = map?.GetComponent<MapComponent_AvirSky>();
            if (c == null || c.Intensity <= 0.001f) return;
            AvirSkyGlow e = c.Ext;
            if (e == null) return;
            Color col = c.CurrentColor(e);
            SkyColorSet cur = __result.colors;
            var ours = new SkyTarget(
                Mathf.Max(__result.glow, e.nightGlow),
                new SkyColorSet(Color.Lerp(cur.sky, col, 0.6f), cur.shadow, Color.Lerp(cur.overlay, col, 0.35f), Mathf.Max(cur.saturation, 1.1f)),
                __result.lightsourceShineSize, __result.lightsourceShineIntensity);
            __result = SkyTarget.Lerp(__result, ours, Mathf.Clamp01(c.Intensity * c.Now(e).strength));
        }
    }
}
