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
    public enum WeatherCall { None, Rain, Fog, WindUp, WindDown, Clear, Storm, StormFront }

    public static class WeatherCalls
    {
        public const int Channel = 2500, Holds = 20000;   // an hour of calling, eight hours held
        public const float Cost = 20f, Xp = 30f;
        public static readonly WeatherCall[] All = { WeatherCall.Rain, WeatherCall.Fog, WeatherCall.WindUp, WeatherCall.WindDown, WeatherCall.Clear, WeatherCall.Storm, WeatherCall.StormFront };

        /// <summary>How many casters it takes at once (the author, Fix267: the storm is the work of a circle of mages):
        /// a thunderstorm - two of the Storm, a storm front - three.</summary>
        public static int Need(WeatherCall w) => w == WeatherCall.Storm ? 2 : w == WeatherCall.StormFront ? 3 : 1;
        public static bool Stormy(WeatherCall w) => w == WeatherCall.Storm || w == WeatherCall.StormFront;

        /// <summary>Hot lands: the storm comes dry there - lightning and no rain, the dry grass catches.</summary>
        public static bool Hot(Map m) => m != null && (m.mapTemperature.OutdoorTemp > 30f || (m.Biome?.defName ?? "").IndexOf("Desert", System.StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>A storm over the map now - the sky's own or a called one.</summary>
        public static bool StormOn(Map m)
        {
            if (m == null) return false;
            var c = MapComponent_WeatherCall.Of(m);
            if (c != null && c.Active && Stormy(c.call)) return true;
            return (m.weatherManager.curWeather?.defName ?? "").IndexOf("Thunderstorm", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static RuneKind[] Kinds(WeatherCall w)
        {
            switch (w)
            {
                case WeatherCall.Rain: return new[] { RuneKind.Water };
                case WeatherCall.Fog: return new[] { RuneKind.Fire, RuneKind.Water };
                case WeatherCall.WindUp: case WeatherCall.WindDown: return new[] { RuneKind.Air };
                case WeatherCall.Clear: return new[] { RuneKind.Fire, RuneKind.Air };
                case WeatherCall.Storm: case WeatherCall.StormFront: return new[] { RuneKind.Water, RuneKind.Air };
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
                case WeatherCall.Storm: return "Thunderstorm (storm; a circle of 2)";
                case WeatherCall.StormFront: return "Storm front (storm; a circle of 3)";
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
                case WeatherCall.Storm: return "Two casters of the storm (water and air) together: a thunderstorm over the land - and round the altar a quiet eye where no bolt falls (8 cells, +2 for each stage). Lightning seeks what conducts: metal, armour, wet ground, rods. In hot lands it comes dry and fires the grass. After a wet storm the open fields are richer for a while.";
                case WeatherCall.StormFront: return "Three casters of the storm together: a wall of cloud that walks the map where it is led (choose where at the altar), striking thrice as often wherever it stands; elsewhere only rain.";
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
                case WeatherCall.Storm: return DefDatabase<WeatherDef>.GetNamedSilentFail(Hot(m) ? "DryThunderstorm" : "RainyThunderstorm");
                case WeatherCall.StormFront: return Hot(m) ? WeatherDefOf.Clear : DefDatabase<WeatherDef>.GetNamedSilentFail("Rain");
            }
            return null;
        }

        public static Hediff_ArcaneCore Able(Pawn p, WeatherCall w, float cost) => w == WeatherCall.None ? null : Arcane.Able(p, cost, Kinds(w));

        public static Color Tint(WeatherCall w) => Stormy(w) ? new Color(0.65f, 0.6f, 1f, 0.55f) : w == WeatherCall.Rain ? new Color(0.5f, 0.7f, 1f, 0.55f) : w == WeatherCall.Fog ? new Color(0.85f, 0.85f, 0.9f, 0.5f) : w == WeatherCall.Clear ? new Color(1f, 0.85f, 0.5f, 0.5f) : new Color(0.8f, 0.95f, 0.9f, 0.5f);
    }

    /// <summary>What the sky has been talked into on this map, and till when.</summary>
    public class MapComponent_WeatherCall : MapComponent
    {
        public WeatherCall call;
        public int until;
        // a storm's eye round the altar; a front's place and where it is led; whether the storm was wet (Fix267)
        public IntVec3 eyeAt = IntVec3.Invalid; public float eyeR;
        public Vector3 front; public IntVec3 frontTo = IntVec3.Invalid;
        public bool wet;
        private readonly Dictionary<int, int> marks = new Dictionary<int, int>();   // the circle: who channels now (pawn id - tick)
        public const float FrontRadius = 6f;
        public MapComponent_WeatherCall(Map map) : base(map) { }
        public static MapComponent_WeatherCall Of(Map m) => m?.GetComponent<MapComponent_WeatherCall>();
        public bool Active => call != WeatherCall.None && Find.TickManager.TicksGame < until;
        public int Left => Active ? until - Find.TickManager.TicksGame : 0;

        public void Set(WeatherCall w, int ticks) => Set(w, ticks, IntVec3.Invalid, 0f, IntVec3.Invalid);

        public void Set(WeatherCall w, int ticks, IntVec3 altar, float eye, IntVec3 lead)
        {
            if (Active && Stormy(call) && !Stormy(w)) AfterStorm();
            call = w; until = Find.TickManager.TicksGame + ticks;
            eyeAt = w == WeatherCall.Storm ? altar : IntVec3.Invalid; eyeR = eye;
            wet = Stormy(w) && !WeatherCalls.Hot(map);
            if (w == WeatherCall.StormFront)
            {
                frontTo = lead.IsValid ? lead : map.Center;
                // it comes in from the edge nearest where it is led, not out of the colony
                IntVec3 t = frontTo;
                int dx = Mathf.Min(t.x, map.Size.x - 1 - t.x), dz = Mathf.Min(t.z, map.Size.z - 1 - t.z);
                IntVec3 edge = dx < dz ? new IntVec3(t.x < map.Size.x / 2 ? 0 : map.Size.x - 1, 0, t.z) : new IntVec3(t.x, 0, t.z < map.Size.z / 2 ? 0 : map.Size.z - 1);
                front = edge.ToVector3Shifted();
            }
            Hold();
        }

        private static bool Stormy(WeatherCall w) => WeatherCalls.Stormy(w);

        /// <summary>Where a bolt of the sky falls, the called storm weighed: never in the quiet eye.</summary>
        public IntVec3 Steer(IntVec3 at)
        {
            if (!Active || call != WeatherCall.Storm || !eyeAt.IsValid || at.DistanceTo(eyeAt) > eyeR) return at;
            for (int i = 0; i < 20; i++)
            {
                float a = Rand.Range(0f, 360f) * Mathf.Deg2Rad, d = eyeR + Rand.Range(2f, 9f);
                IntVec3 c = new IntVec3(Mathf.RoundToInt(eyeAt.x + Mathf.Cos(a) * d), 0, Mathf.RoundToInt(eyeAt.z + Mathf.Sin(a) * d));
                if (c.InBounds(map)) return c;
            }
            return at;
        }

        public bool InEye(IntVec3 c) => Active && call == WeatherCall.Storm && eyeAt.IsValid && c.DistanceTo(eyeAt) <= eyeR;

        // ---- the circle of mages: each caster at the altar marks itself while channelling; it is done when enough stand together
        public void Mark(Pawn p) => marks[p.thingIDNumber] = Find.TickManager.TicksGame;
        public int Together() { int now = Find.TickManager.TicksGame; return marks.Count(kv => now - kv.Value < 120); }

        /// <summary>A wet storm leaves the open fields richer for a while (lightning feeds the ground).</summary>
        private void AfterStorm()
        {
            if (!wet) return;
            var fert = MapComponent_LifeFertility.Of(map);
            if (fert == null) return;
            foreach (Zone_Growing z in map.zoneManager.AllZones.OfType<Zone_Growing>())
                foreach (IntVec3 c in z.Cells) if (!c.Roofed(map)) fert.Add(c, 0.1f, 5);
            Messages.Message("The storm has passed: the open fields are richer for a few days.", MessageTypeDefOf.PositiveEvent, false);
            wet = false;
        }

        private void Hold()
        {
            WeatherDef d = WeatherCalls.Weather(call, map);
            if (d != null && map.weatherManager.curWeather != d) map.weatherManager.TransitionTo(d);
        }

        public override void MapComponentTick()
        {
            if (call == WeatherCall.None || Find.TickManager.TicksGame % 250 != 0) return;
            if (!Active) { if (Stormy(call)) AfterStorm(); call = WeatherCall.None; return; }
            Hold();
            if (call == WeatherCall.StormFront)
            {
                // the front walks a cell toward where it is led, and strikes where it stands (thrice a thunderstorm's rate)
                Vector3 to = frontTo.IsValid ? frontTo.ToVector3Shifted() : front;
                Vector3 d = to - front; d.y = 0f;
                if (d.magnitude > 0.5f) front += d.normalized * Mathf.Min(1f, d.magnitude);
                if (Rand.Chance(0.55f))
                {
                    IntVec3 c = (front + Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up) * Vector3.forward * Rand.Range(0f, FrontRadius * 2f)).ToIntVec3();
                    if (c.InBounds(map)) map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, c));
                }
            }
        }

        public override void MapComponentUpdate()
        {
            if (!Active || Find.CurrentMap != map) return;
            // the front's cloud and the eye's ring, faint, for the player to see where they are
            if (call == WeatherCall.StormFront) GenDraw.DrawRadiusRing(front.ToIntVec3(), FrontRadius * 2f, new Color(0.55f, 0.5f, 0.95f, 0.6f));
            else if (call == WeatherCall.Storm && eyeAt.IsValid && Find.Selector.SingleSelectedThing is Building_WeatherAltar) GenDraw.DrawRadiusRing(eyeAt, eyeR, new Color(0.7f, 0.85f, 1f, 0.6f));
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref call, "call");
            Scribe_Values.Look(ref until, "until");
            Scribe_Values.Look(ref eyeAt, "eyeAt", IntVec3.Invalid);
            Scribe_Values.Look(ref eyeR, "eyeR");
            Scribe_Values.Look(ref front, "front");
            Scribe_Values.Look(ref frontTo, "frontTo", IntVec3.Invalid);
            Scribe_Values.Look(ref wet, "wet");
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
        public IntVec3 lead = IntVec3.Invalid;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref wanted, "wanted");
            Scribe_Values.Look(ref lead, "lead", IntVec3.Invalid);
        }

        /// <summary>Where the casters stand round the altar: the circle of mages takes the four sides.</summary>
        public IEnumerable<IntVec3> Places()
        {
            yield return InteractionCell;
            foreach (IntVec3 o in new[] { new IntVec3(2, 0, 0), new IntVec3(-2, 0, 0), new IntVec3(0, 0, 2), new IntVec3(0, 0, -2) })
            {
                IntVec3 c = Position + o;
                if (c != InteractionCell && c.InBounds(Map) && c.Standable(Map)) yield return c;
            }
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
            if (wanted == WeatherCall.StormFront)
                yield return new Command_Action
                {
                    defaultLabel = "Lead the front",
                    defaultDesc = "Choose where the storm front is to walk. It can be led again at any time while it lasts.",
                    icon = ArcaneIcons.For("Avir_WeatherAltarLead", "UI/Commands/Attack"),
                    action = () => Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), t =>
                    {
                        lead = t.Cell;
                        var c = MapComponent_WeatherCall.Of(Map);
                        if (c != null && c.Active && c.call == WeatherCall.StormFront) c.frontTo = t.Cell;
                    })
                };
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: call it now",
                    action = () => { if (wanted != WeatherCall.None) MapComponent_WeatherCall.Of(Map)?.Set(wanted, WeatherCalls.Holds, Position, 8f, lead); }
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
            // the circle of mages: as many as the call needs, each at a place of its own round the altar
            int need = WeatherCalls.Need(a.wanted);
            if (Callers(a).Count() >= need) return false;
            return Place(a, pawn).IsValid && pawn.CanReach(t, PathEndMode.Touch, Danger.Some);
        }

        public static IEnumerable<Pawn> Callers(Building_WeatherAltar a) => a.Map.mapPawns.FreeColonistsSpawned.Where(p => p.CurJob?.def?.defName == "Avir_WeatherCall" && p.CurJob.targetA.Thing == a);

        public static IntVec3 Place(Building_WeatherAltar a, Pawn pawn)
        {
            var taken = new HashSet<IntVec3>(Callers(a).Where(p => p != pawn).Select(p => p.CurJob.targetB.Cell));
            return a.Places().FirstOrDefault(c => !taken.Contains(c) && pawn.CanReach(c, PathEndMode.OnCell, Danger.Some) && pawn.CanReserve(c));
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_WeatherAltar a = (Building_WeatherAltar)t;
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_WeatherCall"), t, Place(a, pawn));
        }
    }

    public class JobDriver_WeatherCall : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => !job.targetB.IsValid || pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            Building_WeatherAltar a = TargetA.Thing as Building_WeatherAltar;
            WeatherCall w = a?.wanted ?? WeatherCall.None;
            this.FailOn(() => a == null || a.wanted != w);
            RuneKind[] kinds = WeatherCalls.Kinds(w);
            int need = WeatherCalls.Need(w);
            MapComponent_WeatherCall comp = MapComponent_WeatherCall.Of(pawn.Map);
            yield return job.targetB.IsValid ? Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell) : Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil ch = ArcaneChannel.Channel(this, WeatherCalls.Channel, WorkSpells.Charm("Avir_WeatherAltar", kinds), false, WeatherCalls.Tint(w));
            if (need > 1) ch.AddPreTickAction(() => comp?.Mark(pawn));
            yield return ch;
            if (need > 1)
            {
                // the circle: an hour more at most for the others to stand in; the call holds once enough channel together
                Toil wait = ToilMaker.MakeToil("WeatherCircle");
                wait.defaultCompleteMode = ToilCompleteMode.Delay;
                wait.defaultDuration = WeatherCalls.Channel;
                wait.tickAction = () =>
                {
                    comp?.Mark(pawn);
                    if (comp == null || comp.Together() >= need || (comp.Active && comp.call == w)) ReadyForNextToil();
                };
                wait.WithProgressBarToilDelay(TargetIndex.A);
                yield return wait;
            }
            Toil done = ToilMaker.MakeToil("WeatherCall");
            done.initAction = () =>
            {
                Hediff_ArcaneCore c = WeatherCalls.Able(pawn, w, WeatherCalls.Cost);
                if (c == null || comp == null) return;
                bool already = comp.Active && comp.call == w && comp.Left > WeatherCalls.Holds - WeatherCalls.Channel * 2;
                if (!already && need > 1 && comp.Together() < need)
                {
                    Messages.Message(WeatherCalls.Label(w) + " needs a circle of " + need + " casters of the storm channelling together at the altar - " + pawn.LabelShort + " stood alone too long.", a, MessageTypeDefOf.RejectInput, false);
                    return;
                }
                Arcane.Done(c, WeatherCalls.Cost, WeatherCalls.Xp, kinds);
                if (already) return;   // one of the circle: the sky is already turned
                int stage = Mathf.Min(MageClass.Stage(MageClass.XpIn(c, RuneKind.Water)), MageClass.Stage(MageClass.XpIn(c, RuneKind.Air)));
                comp.Set(w, WeatherCalls.Holds, a.Position, 8f + 2f * stage, a.lead);
                Messages.Message((need > 1 ? "The circle of " + pawn.LabelShort + " has" : pawn.LabelShort + " has") + " talked the sky round: " + WeatherCalls.Label(w).ToLower() + ", for about eight hours.", a, MessageTypeDefOf.NeutralEvent, false);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    /// <summary>The Stormcaller (the author, Fix267: aimed bolts - the top of the storm): a Master of the Storm, while a
    /// storm is over the map - the sky's own or a called one - brings one bolt down where it chooses, on what it sees under
    /// the open sky, once in ten seconds. Not drawn to rods, not kept out of the eye.</summary>
    public static class Stormcaller
    {
        public const int Cooldown = 600, MasterStage = 4;
        public const float Cost = 3f, Range = 30f;
        private static readonly Dictionary<int, int> ready = new Dictionary<int, int>();

        public static bool Is(Hediff_ArcaneCore c) => c != null && MagicWork.Knows(c, RuneKind.Water) && MagicWork.Knows(c, RuneKind.Air)
            && Mathf.Min(MageClass.Stage(MageClass.XpIn(c, RuneKind.Water)), MageClass.Stage(MageClass.XpIn(c, RuneKind.Air))) >= MasterStage;

        public static Command Gizmo(Pawn p, Hediff_ArcaneCore c)
        {
            if (p?.Map == null || !Is(c) || !WeatherCalls.StormOn(p.Map)) return null;
            int now = Find.TickManager.TicksGame;
            bool cool = ready.TryGetValue(p.thingIDNumber, out int at) && at > now;
            var cmd = new Command_Action
            {
                defaultLabel = "Call down lightning",
                defaultDesc = "A Master of the Storm brings a bolt of the storm over the map down where it chooses: on what it sees, under the open sky, within " + Range.ToString("0") + " cells. " + Cost.ToString("0") + " Arcana; once in ten seconds.",
                icon = ArcaneIcons.For("Avir_CallLightning", "UI/Commands/Attack"),
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetPawns = true, canTargetBuildings = true }, t => Strike(p, t.Cell), null, x => Ok(p, x.Cell))
            };
            if (cool) cmd.Disable("Ready in " + (at - now).ToStringTicksToPeriod());
            else if (c.arcana < Cost) cmd.Disable("Not enough Arcana.");
            return cmd;
        }

        private static bool Ok(Pawn p, IntVec3 c) => c.InBounds(p.Map) && !c.Roofed(p.Map) && c.DistanceTo(p.Position) <= Range && GenSight.LineOfSight(p.Position, c, p.Map, true);

        public static void Strike(Pawn p, IntVec3 c)
        {
            Hediff_ArcaneCore core = Mages.Core(p);
            if (core == null || !Ok(p, c) || core.arcana < Cost) { Messages.Message("The bolt cannot be brought down there: in sight, under the open sky, in reach.", MessageTypeDefOf.RejectInput, false); return; }
            core.Add(-Cost);
            MageClass.Gain(core, RuneKind.Air, 4f); MageClass.Gain(core, RuneKind.Water, 4f);
            ready[p.thingIDNumber] = Find.TickManager.TicksGame + Cooldown;
            LightningRods.Aimed = true;
            try { p.Map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(p.Map, c)); }
            finally { LightningRods.Aimed = false; }
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
