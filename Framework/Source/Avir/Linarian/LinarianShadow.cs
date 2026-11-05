using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian
{
    // =====================================================================================
    // IN THE SHADOW OF THE FLOW. The rarest Herald and the meanest: she leans on the arcane, plays with space and
    // time, and fouls the land itself.
    //   The black fog: rises around her (by her choice, or by day on its own while she is hostile), creeps through
    //     open doors, and in it shots go wild and plants stop growing.
    //   The flare: every machine dies until dawn (a hostile one does it every night).
    //   The eternal night: for three days the land believes it is night. It is an illusion - the sun still shines,
    //     plants bloom and solar panels work; only minds are fooled. The deaf are not, and say so.
    // =====================================================================================
    public static class LinarianShadow
    {
        public static void Heal(Pawn p, float amount)
        {
            foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
            {
                if (amount <= 0f) break;
                float h = Mathf.Min(inj.Severity, amount);
                inj.Heal(h);
                amount -= h;
            }
        }

        public static void Younger(Pawn p, float years)
        {
            if (p?.ageTracker == null) return;
            long min = 22L * GenDate.TicksPerYear;
            p.ageTracker.AgeBiologicalTicks = System.Math.Max(min, p.ageTracker.AgeBiologicalTicks - (long)(years * GenDate.TicksPerYear));
        }

        /// <summary>The call through the dark: one standing in the dark falls through the shadow to her side.</summary>
        public static void ThroughTheDark(Pawn q, Pawn her)
        {
            if (q?.Map == null || q.Map != her.Map || q.Map.glowGrid.GroundGlowAt(q.Position) > 0.3f) return;
            IntVec3 at = CellFinder.StandableCellNear(her.Position, her.Map, 2f);
            if (!at.IsValid) return;
            LinarianActs.Leap(q, at);
        }

        // ---------------------------------------------------------------- fog and flare

        public static void FogAndFlare(Pawn her, IntVec3 at, float radius)
        {
            Map map = her.Map;
            if (map == null) return;
            MapComponent_BlackFog.Of(map)?.Raise(at, Mathf.Max(8f, radius), GenDate.TicksPerHour * 6);
            Flare(map);
        }

        public static void FogAndFlare(Pawn her, bool chosen)
        {
            Map map = her.Map;
            if (map == null) return;
            MapComponent_BlackFog.Of(map)?.Raise(her.Position, 18f, GenDate.TicksPerHour * 6);
            Flare(map);
        }

        /// <summary>A flare that holds until dawn.</summary>
        public static void Flare(Map map)
        {
            GameConditionDef def = DefDatabase<GameConditionDef>.GetNamedSilentFail("SolarFlare");
            if (def == null || map.gameConditionManager.ConditionIsActive(def)) return;
            int hour = GenLocalDate.HourOfDay(map);
            int hours = hour >= 6 ? 30 - hour : 6 - hour;
            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(def, Mathf.Max(1, hours) * GenDate.TicksPerHour));
        }

        // ---------------------------------------------------------------- the eternal night

        public static GameConditionDef EternalNightDef => DefDatabase<GameConditionDef>.GetNamedSilentFail("Avir_EternalNight");

        public static bool EternalNightOn(Map map) => map != null && EternalNightDef != null && map.gameConditionManager.ConditionIsActive(EternalNightDef);

        public static void EternalNight(Map map, Pawn her)
        {
            if (map == null || EternalNightDef == null || EternalNightOn(map)) return;
            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(EternalNightDef, GenDate.TicksPerDay * 3));
            Pawn deaf = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(c => c.GetStatValue(StatDefOf.PsychicSensitivity) <= 0.01f);
            if (deaf != null)
                Find.LetterStack.ReceiveLetter("The sky is wrong", deaf.LabelShortCap + " looks up and frowns: the sun is right there, burning in a clear sky. Everyone else swears it is night.\n\nIt is an illusion - a vast one. A Singer is near.",
                    LetterDefOf.ThreatSmall, deaf);
            else if (her?.Faction == Faction.OfPlayer)
                Messages.Message("Night falls in the middle of the day. It will last three days.", MessageTypeDefOf.NeutralEvent, false);
        }

        public static float RealSun(Map map) => GenCelestial.CurCelestialSunGlow(map);
    }

    /// <summary>The eternal night: the sky of a deep night, for three days. Only the look: the plants and the
    /// panels below still have their sun (Patch_EternalNightPlants, Patch_EternalNightSolar).</summary>
    public class GameCondition_EternalNight : GameCondition
    {
        private static readonly SkyColorSet Night = new SkyColorSet(new Color(0.30f, 0.32f, 0.46f), new Color(0.85f, 0.85f, 0.9f), new Color(0.42f, 0.44f, 0.58f), 0.75f);

        public override float SkyTargetLerpFactor(Map map) => GameConditionUtility.LerpInOutValue(this, 2500f);

        public override SkyTarget? SkyTarget(Map map) => new SkyTarget(0.08f, Night, 1f, 1f);
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRateFactor_Light), MethodType.Getter)]
    public static class Patch_EternalNightPlants
    {
        public static void Postfix(Plant __instance, ref float __result)
        {
            Map map = __instance.Map;
            if (map == null || !LinarianShadow.EternalNightOn(map) || __instance.Position.Roofed(map)) return;
            __result = Mathf.Max(__result, PlantUtility.GrowthRateFactorFor_Light(__instance.def, LinarianShadow.RealSun(map)));
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.GrowthRate), MethodType.Getter)]
    public static class Patch_BlackFogPlants
    {
        public static void Postfix(Plant __instance, ref float __result)
        {
            if (__result > 0f && __instance.Spawned && MapComponent_BlackFog.Of(__instance.Map)?.Thick(__instance.Position) == true) __result = 0f;
        }
    }

    [HarmonyPatch(typeof(CompPowerPlantSolar), "DesiredPowerOutput", MethodType.Getter)]
    public static class Patch_EternalNightSolar
    {
        public static void Postfix(CompPowerPlantSolar __instance, ref float __result)
        {
            Map map = __instance.parent?.Map;
            if (map == null || !LinarianShadow.EternalNightOn(map)) return;
            float sky = map.skyManager.CurSkyGlow;
            if (sky > 0.01f) __result *= LinarianShadow.RealSun(map) / sky;
        }
    }

    /// <summary>In the black fog shots go wild.</summary>
    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_BlackFogShots
    {
        private static readonly System.Reflection.FieldInfo Gas = AccessTools.Field(typeof(ShotReport), "factorFromCoveringGas");

        public static void Postfix(Thing caster, LocalTargetInfo target, ref ShotReport __result)
        {
            Map map = caster?.Map;
            MapComponent_BlackFog fog = MapComponent_BlackFog.Of(map);
            if (fog == null || !fog.Any || Gas == null) return;
            if (!fog.Thick(caster.Position) && !fog.Thick(target.Cell)) return;
            object boxed = __result;
            Gas.SetValue(boxed, (float)Gas.GetValue(boxed) * 0.15f);
            __result = (ShotReport)boxed;
        }
    }

    // ---------------------------------------------------------------- the black fog itself

    /// <summary>The black fog is a real gas now: it rises as the game's own smoke - spreading, seeping through open doors,
    /// thinning in the wind, blinding the aim - and is drawn black where she raised it.</summary>
    public class MapComponent_BlackFog : MapComponent
    {
        private List<FogSource> sources = new List<FogSource>();
        private List<FogSource> trails = new List<FogSource>();
        private static readonly Material[] mats = new Material[10];

        public class FogSource : IExposable
        {
            public IntVec3 at;
            public float radius;
            public int until;
            public void ExposeData()
            {
                Scribe_Values.Look(ref at, "at");
                Scribe_Values.Look(ref radius, "radius");
                Scribe_Values.Look(ref until, "until");
            }
        }

        public MapComponent_BlackFog(Map map) : base(map) { }

        public static MapComponent_BlackFog Of(Map m) => m?.GetComponent<MapComponent_BlackFog>();

        public bool Any => sources.Count > 0 || trails.Count > 0;

        /// <summary>Whether the smoke in this cell is hers (near where she raised it, or where it lingers after).</summary>
        public bool Ours(IntVec3 c)
        {
            for (int i = 0; i < sources.Count; i++) if (Near(sources[i], c)) return true;
            for (int i = 0; i < trails.Count; i++) if (Near(trails[i], c)) return true;
            return false;
        }

        private static bool Near(FogSource s, IntVec3 c)
        {
            float r = s.radius * 1.8f + 4f;
            return (c - s.at).LengthHorizontalSquared <= r * r;
        }

        public bool Thick(IntVec3 c) => Any && c.InBounds(map) && c.GasDensity(map, GasType.BlindSmoke) > 90 && Ours(c);

        public void Raise(IntVec3 at, float radius, int ticks)
        {
            sources.Add(new FogSource { at = at, radius = radius, until = Find.TickManager.TicksGame + ticks });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref sources, "fogSources", LookMode.Deep);
            Scribe_Collections.Look(ref trails, "fogTrails", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (sources == null) sources = new List<FogSource>();
                if (trails == null) trails = new List<FogSource>();
            }
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 120 != 37 || !Any) return;
            foreach (FogSource s in sources.Where(s => s.until < now).ToList())
            {
                sources.Remove(s);
                s.until = now + GenDate.TicksPerHour * 2;   // what is left of it still lingers, black
                trails.Add(s);
            }
            trails.RemoveAll(s => s.until < now);
            foreach (FogSource s in sources)
                foreach (IntVec3 c in GenRadial.RadialCellsAround(s.at, Mathf.Min(s.radius, GenRadial.MaxRadialPatternRadius - 1f), true))
                    if (((c.x + c.z) & 1) == 0 && c.InBounds(map) && map.gasGrid.GasCanMoveTo(c))
                        map.gasGrid.AddGas(c, GasType.BlindSmoke, 120, true);
        }

        public override void MapComponentUpdate()
        {
            if (!Any || Find.CurrentMap != map) return;
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(1).ClipInsideMap(map);
            float y = AltitudeLayer.Gas.AltitudeFor() + 0.02f;
            float t = Time.realtimeSinceStartup * 4f;
            foreach (IntVec3 c in view)
            {
                byte d = c.GasDensity(map, GasType.BlindSmoke);
                if (d <= 8 || !Ours(c)) continue;
                int k = Mathf.Clamp(d * 10 / 256, 0, 9);
                Material m = mats[k] ?? (mats[k] = MaterialPool.MatFrom("Things/Gas/GasCloudThickA", ShaderDatabase.Transparent, new Color(0.03f, 0.02f, 0.06f, 0.18f + 0.07f * k)));
                float turn = (c.x * 37 + c.z * 101) % 360 + t;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(c.ToVector3Shifted().WithY(y), Quaternion.AngleAxis(turn, Vector3.up), new Vector3(1.7f, 1f, 1.7f)), m, 0);
            }
        }
    }
}
