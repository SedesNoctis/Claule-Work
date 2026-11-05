using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.North
{
    // =====================================================================================
    // THE HOWLING STORM. Not a weather but an event, as toxic fallout is: it comes over a map and holds it for days -
    // or weeks. It has a power (five stages): the stronger it is, the deeper the cold it brings (as a cold snap does,
    // and more) and the harder it is to move under the open sky. Its power follows the stream: where the flow is
    // strong the storms come more often and rise higher. The sky is the howling blizzard's for as long as it lasts.
    // A roof or the spire's barrier shelters from the wind; nothing shelters from the cold but walls and fire.
    // =====================================================================================

    [DefOf]
    public static class NorthDefOf
    {
        public static WeatherDef Avir_HowlingBlizzard;
        public static HediffDef Avir_BlizzardLash;
        public static RoofDef Avir_CanopyRoof;
        public static GameConditionDef Avir_HowlingStorm;
        static NorthDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(NorthDefOf)); }
    }

    public class GameCondition_HowlingStorm : GameCondition
    {
        public const int MaxPower = 5;
        /// <summary>The storm's names by its strength - what the north calls it, never a number.</summary>
        private static readonly string[] Names = { "Breath of the North", "White Wail", "Howling Wall", "White Fury", "Wrath of the Stream" };
        /// <summary>The cold it brings at each power (degrees) - a vanilla cold snap is -20.</summary>
        private static readonly float[] Cold = { -6f, -12f, -20f, -30f, -45f };
        /// <summary>How hard the wind holds back whoever is under the open sky (the lash's severity it drives to).</summary>
        private static readonly float[] Wind = { 0.2f, 0.35f, 0.5f, 0.7f, 0.9f };

        public int power;   // 0 until Init: then by the stream
        /// <summary>The cold now: it follows the power slowly, so a rising storm cools the map over hours.</summary>
        private float cold;

        public override int TransitionTicks => 12000;
        public override string Label => Names[Mathf.Clamp(power, 1, MaxPower) - 1];
        public override string Description => def.description + "\n\n" + Names[power - 1] + " (" + power + " of " + MaxPower + "): " + Cold[power - 1].ToString("0") + "°C, "
            + "movement under the open sky down to -" + (WindMoving(Wind[power - 1]) * 100f).ToString("0") + "%.";

        public static float WindMoving(float severity) => severity >= 0.85f ? 0.5f : severity >= 0.65f ? 0.4f : severity >= 0.45f ? 0.3f : severity >= 0.25f ? 0.2f : 0.1f;

        public override void Init()
        {
            base.Init();
            if (power <= 0 || power > MaxPower) power = PowerFor(SingleMap);   // unless the caller set it
            cold = Cold[power - 1];
        }

        /// <summary>The power the stream over this map feeds: one step per tier of the flow, give or take one.</summary>
        public static int PowerFor(Map map)
        {
            int tier = map == null ? 0 : Avir.Ritualistics.MapComponent_ExternalFlow.TierIndex(Avir.Ritualistics.MapComponent_ExternalFlow.Of(map)?.Total ?? 0f);
            return Mathf.Clamp(1 + tier + Rand.RangeInclusive(-1, 1), 1, MaxPower);
        }

        public float WindTarget => Wind[Mathf.Clamp(power, 1, MaxPower) - 1] * GameConditionUtility.LerpInOutValue(this, TransitionTicks);

        public override float TemperatureOffset() => GameConditionUtility.LerpInOutValue(this, TransitionTicks, cold);

        public override WeatherDef ForcedWeather() => NorthDefOf.Avir_HowlingBlizzard;

        public override float MinWindSpeed() => 1f + 0.3f * power;

        public override void GameConditionTick()
        {
            base.GameConditionTick();
            int now = Find.TickManager.TicksGame;
            if (now % 250 == 0) cold = Mathf.MoveTowards(cold, Cold[power - 1], 0.25f);   // a degree an hour
            // once a day the storm rises or falls toward what the stream feeds
            if (now % 60000 == 30000 && SingleMap != null)
            {
                int want = PowerFor(SingleMap);
                if (want != power && Rand.Chance(0.5f))
                {
                    int was = power;
                    power += want > power ? 1 : -1;
                    Messages.Message("The howling storm " + (power > was ? "rises" : "weakens") + ": " + Names[power - 1] + ".",
                        power > was ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent, false);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref power, "power", 1);
            Scribe_Values.Look(ref cold, "cold");
        }

        public static GameCondition_HowlingStorm On(Map map) =>
            NorthDefOf.Avir_HowlingStorm == null ? null : map?.gameConditionManager.GetActiveCondition(NorthDefOf.Avir_HowlingStorm) as GameCondition_HowlingStorm;

        /// <summary>Starts a storm on the map (if none rages there yet): power 0 - by the stream.</summary>
        public static bool Start(Map map, int days, int power = 0)
        {
            if (map == null || NorthDefOf.Avir_HowlingStorm == null || On(map) != null) return false;
            var c = (GameCondition_HowlingStorm)GameConditionMaker.MakeCondition(NorthDefOf.Avir_HowlingStorm, days * 60000);
            if (power > 0) c.power = Mathf.Clamp(power, 1, MaxPower);
            map.gameConditionManager.RegisterCondition(c);
            Find.LetterStack.ReceiveLetter(c.LabelCap, c.LetterText + "\n\n" + c.Description, LetterDefOf.NegativeEvent, new LookTargets(map.Parent));
            return true;
        }
    }

    /// <summary>The storm's incident: common in the north, rarer elsewhere the colder it is, and far more frequent where
    /// the stream is strong.</summary>
    public class IncidentWorker_HowlingStorm : IncidentWorker_MakeGameCondition
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms.target is Map map) || !base.CanFireNowSub(parms)) return false;
            return MapFactor(map) > 0f;
        }

        public override float ChanceFactorNow(IIncidentTarget target) =>
            base.ChanceFactorNow(target) * (target is Map map ? MapFactor(map) : 0f);

        public static float MapFactor(Map map)
        {
            float f;
            if (map.Biome?.workerClass == typeof(BiomeWorker_AvirNorth)) f = 4f;
            else
            {
                float t = map.mapTemperature.OutdoorTemp;
                f = t < -10f ? 1.5f : t < 0f ? 1f : t < 10f ? 0.4f : 0.1f;
            }
            float flow = Avir.Ritualistics.MapComponent_ExternalFlow.Of(map)?.Total ?? 0f;
            return f * Mathf.Min(6f, 1f + flow / Avir.Ritualistics.MapComponent_ExternalFlow.Calm);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            return GameCondition_HowlingStorm.Start(map, Mathf.Max(1, Mathf.RoundToInt(def.durationDays.RandomInRange)));
        }
    }

    /// <summary>The wind: everyone under the open sky in the storm, not under the barrier, is held back - the harder the
    /// stronger the storm; sheltered, it fades fast. The cold itself is the map's temperature (the storm lowers it).</summary>
    public class MapComponent_AvirBlizzard : MapComponent
    {
        public const int Interval = 250;
        public const float RisePerInterval = 0.04f;

        public MapComponent_AvirBlizzard(Map map) : base(map) { }

        public static bool Exposed(Pawn p) =>
            p.Spawned && !p.Position.Roofed(p.Map) && !Avir.Ritualistics.BarrierDome.Covers(p.Map, p.Position);

        /// <summary>The storm's sky never stands alone: without the storm (left behind by one that has passed, or
        /// brought by anything else - another mod, a dev menu) it clears at once.</summary>
        private void KeepSkyWithStorm(GameCondition_HowlingStorm storm)
        {
            if (storm == null && map.weatherManager.curWeather == NorthDefOf.Avir_HowlingBlizzard)
                map.weatherDecider.StartNextWeather();
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 31 || NorthDefOf.Avir_BlizzardLash == null) return;
            GameCondition_HowlingStorm storm = GameCondition_HowlingStorm.On(map);
            KeepSkyWithStorm(storm);
            if (storm == null) return;
            float target = storm.WindTarget;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.health == null || !p.RaceProps.IsFlesh || !Exposed(p)) continue;
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(NorthDefOf.Avir_BlizzardLash);
                if (h == null) { if (target <= 0.02f) continue; h = p.health.AddHediff(NorthDefOf.Avir_BlizzardLash); }
                if (h.Severity < target) h.Severity = Mathf.Min(target, h.Severity + RisePerInterval);
            }
        }
    }
}
