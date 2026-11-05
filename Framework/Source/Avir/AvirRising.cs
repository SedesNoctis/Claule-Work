using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE RISING (stage 4). Once a year, on the first of Decembary at dusk by the colony's own clock, the world
    /// sees it all at once - a world condition, so every map has it, whatever its season:
    ///   the first hour   the sun goes out;
    ///   until the third  a dark sky;
    ///   the third to the seventh  the dance: the sky turns silver, deep blue, rose and gold, an hour each, and grows
    ///                    light toward the middle as the Elder's sun stands at the centre of the sky; an Avir awake
    ///                    under the open sky then remembers it for days;
    ///   to the twelfth   a quiet dark until dawn.
    /// The festival of the Rising (Compat/Ideology) falls on the same date: its date trigger is held at day 45.
    /// </summary>
    public class GameCondition_AvirRising : GameCondition
    {
        public const int Hour = 2500;
        public const int Duration = Hour * 12;
        private const int DanceStart = Hour * 3, DanceEnd = Hour * 7;

        private static readonly Color[] Palette =
        {
            new Color(0.80f, 0.82f, 0.90f),   // silver
            new Color(0.22f, 0.32f, 0.85f),   // deep blue
            new Color(0.95f, 0.55f, 0.72f),   // rose
            new Color(1.00f, 0.80f, 0.40f),   // gold
        };

        private static readonly SkyColorSet Dark =
            new SkyColorSet(new Color(0.16f, 0.18f, 0.30f), Color.white, new Color(0.45f, 0.45f, 0.55f), 0.9f);

        public bool Dancing => TicksPassed >= DanceStart && TicksPassed < DanceEnd;

        public override int TransitionTicks => Hour;

        public override float SkyTargetLerpFactor(Map map) => GameConditionUtility.LerpInOutValue(this, TransitionTicks);

        public override SkyTarget? SkyTarget(Map map)
        {
            if (!Dancing) return new SkyTarget(0f, Dark, 1f, 0f);
            float u = (TicksPassed - DanceStart) / (float)(DanceEnd - DanceStart);
            float glow = Mathf.Sin(u * Mathf.PI) * 0.6f;
            float c = (TicksPassed - DanceStart) / (float)Hour;
            int i = (int)c % Palette.Length;
            Color sky = Color.Lerp(Palette[i], Palette[(i + 1) % Palette.Length], c - (int)c);
            return new SkyTarget(glow, new SkyColorSet(sky, Color.white, Color.Lerp(sky, Color.white, 0.5f), 1.1f), 1f, glow);
        }

        public override void GameConditionTick()
        {
            base.GameConditionTick();
            if (!Dancing || Find.TickManager.TicksGame % 250 != 0) return;
            ThoughtDef seen = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_WitnessedRising");
            if (seen == null) return;
            foreach (Map map in AffectedMaps)
                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    if (p.needs?.mood != null && p.Awake() && AvirRhythmUtility.IsAvir(p) && !p.Position.Roofed(map))
                        p.needs.mood.thoughts.memories.TryGainMemory(seen);
        }
    }

    /// <summary>Starts the Rising each year on the first of Decembary, at dusk on the colony's clock.</summary>
    public class GameComponent_AvirRising : GameComponent
    {
        public const int DayOfYear = 45;   // the first of Decembary
        private int lastYear = -1;

        public GameComponent_AvirRising(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 != 0) return;
            Map home = Find.AnyPlayerHomeMap;
            if (home == null) return;
            int year = GenLocalDate.Year(home);
            if (year == lastYear || GenLocalDate.DayOfYear(home) != DayOfYear || GenLocalDate.HourOfDay(home) < 18) return;
            lastYear = year;
            Start();
        }

        public static void Start()
        {
            GameConditionDef def = DefDatabase<GameConditionDef>.GetNamedSilentFail("Avir_Rising");
            if (def == null || Find.World?.gameConditionManager == null || Find.World.gameConditionManager.ConditionIsActive(def)) return;
            Find.World.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(def, GameCondition_AvirRising.Duration));
            Find.LetterStack.ReceiveLetter(def.LabelCap, def.letterText, def.letterDef ?? LetterDefOf.PositiveEvent);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastYear, "avirRisingYear", -1);
        }

        [DebugAction("Avir", "Start the Rising now", allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugStart() => Start();
    }

    /// <summary>The festival of the Rising is always on the first of Decembary, whatever the date trigger rolled
    /// (also corrects an ideoligion made before this).</summary>
    [HarmonyPatch(typeof(RitualObligationTrigger_Date), nameof(RitualObligationTrigger_Date.Tick))]
    public static class Patch_AvirRisingDate
    {
        public static void Prefix(RitualObligationTrigger_Date __instance)
        {
            if (__instance.ritual?.def?.defName == "Avir_Ritual_Rising" && __instance.triggerDaysSinceStartOfYear != GameComponent_AvirRising.DayOfYear)
                __instance.triggerDaysSinceStartOfYear = GameComponent_AvirRising.DayOfYear;
        }
    }
}
