using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using Avir.Ritualistics;

namespace Avir.Magic
{
    // =====================================================================================
    // THE WEATHER ALTAR (the author, after Fix265: "a spot of the weather-callers - a spherical altar, as the Avir's are,
    // where a pawn stands and rules the weather: rains, fogs, winds"). A caster stands at the altar about an hour and
    // talks the sky round; the weather holds about eight hours. Rain - water; fog - steam (fire, water); the wind raised
    // or stilled - air; the clouds driven off - haze (fire, air). Thunder is held back for now (the author: "we may find
    // how to work with the Storm - widen it"). The altar is told what to call on a panel of its own, never the pop-up menu.
    // =====================================================================================
    public enum WeatherCall { None, Rain, Fog, WindUp, WindDown, Clear }

    public static class WeatherCalls
    {
        public const int Channel = 2500, Holds = 20000;   // an hour of calling, eight hours held
        public const float Cost = 20f, Xp = 30f;
        public static readonly WeatherCall[] All = { WeatherCall.Rain, WeatherCall.Fog, WeatherCall.WindUp, WeatherCall.WindDown, WeatherCall.Clear };

        public static RuneKind[] Kinds(WeatherCall w)
        {
            switch (w)
            {
                case WeatherCall.Rain: return new[] { RuneKind.Water };
                case WeatherCall.Fog: return new[] { RuneKind.Fire, RuneKind.Water };
                case WeatherCall.WindUp: case WeatherCall.WindDown: return new[] { RuneKind.Air };
                case WeatherCall.Clear: return new[] { RuneKind.Fire, RuneKind.Air };
            }
            return new RuneKind[0];
        }

        public static string Label(WeatherCall w)
        {
            switch (w)
            {
                case WeatherCall.Rain: return "Rain (water)";
                case WeatherCall.Fog: return "Fog (steam)";
                case WeatherCall.WindUp: return "Raise the wind (air)";
                case WeatherCall.WindDown: return "Still the wind (air)";
                case WeatherCall.Clear: return "Drive the clouds off (haze)";
            }
            return "Leave the sky be";
        }

        public static string Desc(WeatherCall w)
        {
            switch (w)
            {
                case WeatherCall.Rain: return "A caster of water calls rain - snow, if it is freezing. Fires go out, the fields drink.";
                case WeatherCall.Fog: return "A caster of steam (fire and water) lays fog over the land: shots go astray in it.";
                case WeatherCall.WindUp: return "A caster of air raises the wind, more than twice as strong: wind turbines turn fast, smoke and gas are blown away.";
                case WeatherCall.WindDown: return "A caster of air stills the wind almost to nothing.";
                case WeatherCall.Clear: return "A caster of haze (fire and air) drives the clouds off: a clear sky, rain or snow ended.";
            }
            return "No caster is sent to the altar; the weather goes its own way.";
        }

        public static WeatherDef Weather(WeatherCall w, Map m)
        {
            switch (w)
            {
                case WeatherCall.Rain: return DefDatabase<WeatherDef>.GetNamedSilentFail(m != null && m.mapTemperature.OutdoorTemp < 0f ? "SnowGentle" : "Rain");
                case WeatherCall.Fog: return DefDatabase<WeatherDef>.GetNamedSilentFail("Fog");
                case WeatherCall.Clear: return WeatherDefOf.Clear;
            }
            return null;
        }

        public static Hediff_ArcaneCore Able(Pawn p, WeatherCall w, float cost) => w == WeatherCall.None ? null : Arcane.Able(p, cost, Kinds(w));

        public static Color Tint(WeatherCall w) => w == WeatherCall.Rain ? new Color(0.5f, 0.7f, 1f, 0.55f) : w == WeatherCall.Fog ? new Color(0.85f, 0.85f, 0.9f, 0.5f) : w == WeatherCall.Clear ? new Color(1f, 0.85f, 0.5f, 0.5f) : new Color(0.8f, 0.95f, 0.9f, 0.5f);
    }

    /// <summary>What the sky has been talked into on this map, and till when.</summary>
    public class MapComponent_WeatherCall : MapComponent
    {
        public WeatherCall call;
        public int until;
        public MapComponent_WeatherCall(Map map) : base(map) { }
        public static MapComponent_WeatherCall Of(Map m) => m?.GetComponent<MapComponent_WeatherCall>();
        public bool Active => call != WeatherCall.None && Find.TickManager.TicksGame < until;
        public int Left => Active ? until - Find.TickManager.TicksGame : 0;

        public void Set(WeatherCall w, int ticks)
        {
            call = w; until = Find.TickManager.TicksGame + ticks;
            Hold();
        }

        private void Hold()
        {
            WeatherDef d = WeatherCalls.Weather(call, map);
            if (d != null && map.weatherManager.curWeather != d) map.weatherManager.TransitionTo(d);
        }

        public override void MapComponentTick()
        {
            if (call == WeatherCall.None || Find.TickManager.TicksGame % 250 != 0) return;
            if (!Active) { call = WeatherCall.None; return; }
            Hold();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref call, "call");
            Scribe_Values.Look(ref until, "until");
        }

        public float WindFactor => !Active ? 1f : call == WeatherCall.WindUp ? 2.2f : call == WeatherCall.WindDown ? 0.08f : 1f;
    }

    /// <summary>The wind raised or stilled by the altar.</summary>
    [HarmonyPatch(typeof(WindManager), "WindSpeed", MethodType.Getter)]
    public static class Patch_WindByAltar
    {
        private static readonly AccessTools.FieldRef<WindManager, Map> mapOf = AccessTools.FieldRefAccess<WindManager, Map>("map");
        public static void Postfix(WindManager __instance, ref float __result)
        {
            Map m = mapOf(__instance);
            var c = MapComponent_WeatherCall.Of(m);
            if (c != null && c.call != WeatherCall.None) __result *= c.WindFactor;
        }
    }

    public class Building_WeatherAltar : Building
    {
        public WeatherCall wanted;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref wanted, "wanted");
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Weather: " + WeatherCalls.Label(wanted),
                defaultDesc = "What a caster at this altar is to call, and keep calling while it is wanted. " + WeatherCalls.Desc(wanted),
                icon = ArcaneIcons.For("Avir_WeatherAltar", "UI/Commands/DesirePower"),
                action = () => Find.WindowStack.Add(new Dialog_WeatherAltar(this))
            };
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: call it now",
                    action = () => { if (wanted != WeatherCall.None) MapComponent_WeatherCall.Of(Map)?.Set(wanted, WeatherCalls.Holds); }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: let the sky go",
                    action = () => { var c = MapComponent_WeatherCall.Of(Map); if (c != null) c.until = 0; }
                };
            }
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            var c = MapComponent_WeatherCall.Of(Map);
            string add = "Wanted: " + WeatherCalls.Label(wanted);
            if (c != null && c.Active) add += "\nThe sky holds: " + WeatherCalls.Label(c.call) + ", " + c.Left.ToStringTicksToPeriod() + " more";
            return s.NullOrEmpty() ? add : s + "\n" + add;
        }
    }

    /// <summary>The altar's own panel: what to call.</summary>
    public class Dialog_WeatherAltar : Window
    {
        private readonly Building_WeatherAltar altar;
        public Dialog_WeatherAltar(Building_WeatherAltar a) { altar = a; doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = false; draggable = true; }
        public override Vector2 InitialSize => new Vector2(460f, 96f + 58f * (WeatherCalls.All.Length + 1));

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium; GUI.color = new Color(0.95f, 0.88f, 0.7f);
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "The weather altar");
            Text.Font = GameFont.Small; GUI.color = new Color(0.8f, 0.78f, 0.72f);
            Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 24f), "A caster talks the sky round for an hour; it holds about eight.");
            GUI.color = Color.white;
            var list = new List<WeatherCall> { WeatherCall.None };
            list.AddRange(WeatherCalls.All);
            Map m = altar.Map;
            for (int i = 0; i < list.Count; i++)
            {
                WeatherCall w = list[i];
                Rect r = new Rect(inRect.x, inRect.y + 64f + i * 58f, inRect.width, 54f);
                bool on = altar.wanted == w;
                bool someone = w == WeatherCall.None || (m != null && m.mapPawns.FreeColonistsSpawned.Any(p => WeatherCalls.Able(p, w, 0f) != null));
                Widgets.DrawBoxSolid(r, on ? new Color(0.55f, 0.42f, 0.18f, 0.45f) : Mouse.IsOver(r) ? new Color(1f, 0.9f, 0.6f, 0.1f) : new Color(0f, 0f, 0f, 0.18f));
                if (on) { GUI.color = new Color(1f, 0.85f, 0.45f); Widgets.DrawBox(r, 1); }
                GUI.color = new Color(0.95f, 0.9f, 0.8f);
                Widgets.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 24f), WeatherCalls.Label(w) + (someone ? "" : "   - no caster here knows it"));
                GUI.color = new Color(0.75f, 0.73f, 0.68f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(r.x + 8f, r.y + 26f, r.width - 16f, 28f), WeatherCalls.Desc(w));
                Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonInvisible(r)) { altar.wanted = w; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
            }
        }
    }

    public class WorkGiver_WeatherAltar : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_WeatherAltar>();
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !AvirMagic.On || !pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_WeatherAltar>().Any(a => a.wanted != WeatherCall.None);

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_WeatherAltar a) || a.wanted == WeatherCall.None || t.IsForbidden(pawn)) return false;
            var c = MapComponent_WeatherCall.Of(t.Map);
            // called already and holding for more than an hour - nothing to do yet
            if (c != null && c.Active && c.call == a.wanted && c.Left > WeatherCalls.Channel) return false;
            if (WeatherCalls.Able(pawn, a.wanted, WeatherCalls.Cost) == null) return false;
            return pawn.CanReserveAndReach(t, PathEndMode.InteractionCell, Danger.Some, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_WeatherCall"), t);
    }

    public class JobDriver_WeatherCall : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            Building_WeatherAltar a = TargetA.Thing as Building_WeatherAltar;
            WeatherCall w = a?.wanted ?? WeatherCall.None;
            this.FailOn(() => a == null || a.wanted != w);
            RuneKind[] kinds = WeatherCalls.Kinds(w);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return ArcaneChannel.Channel(this, WeatherCalls.Channel, WorkSpells.Charm("Avir_WeatherAltar", kinds), false, WeatherCalls.Tint(w));
            Toil done = ToilMaker.MakeToil("WeatherCall");
            done.initAction = () =>
            {
                Hediff_ArcaneCore c = WeatherCalls.Able(pawn, w, WeatherCalls.Cost);
                if (c == null) return;
                MapComponent_WeatherCall.Of(pawn.Map)?.Set(w, WeatherCalls.Holds);
                Arcane.Done(c, WeatherCalls.Cost, WeatherCalls.Xp, kinds);
                Messages.Message(pawn.LabelShort + " has talked the sky round: " + WeatherCalls.Label(w).ToLower() + ", for about eight hours.", a, MessageTypeDefOf.NeutralEvent, false);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    public static class WeatherDev
    {
        [LudeonTK.DebugAction("Magic", "Weather altar: what the sky holds (log)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Readout()
        {
            var c = MapComponent_WeatherCall.Of(Find.CurrentMap);
            Messages.Message(c == null || !c.Active ? "The sky is its own." : "The sky holds " + WeatherCalls.Label(c.call) + " for " + c.Left.ToStringTicksToPeriod() + "; wind x" + c.WindFactor.ToString("0.00") + ", wind now " + Find.CurrentMap.windManager.WindSpeed.ToString("0.00"), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
