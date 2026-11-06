using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Our own wind (the author, Fix169: "without that mod - can we break buildings and make such animations,
    /// not by copying, but making something like it?"). A gust is a field of wind for a short while: out from a point
    /// (a hurricane's blow), round a point (a whirl), or one way (a front). Every sixth of a second it acts on what stands
    /// in it, by the wind's strength there (in m/s, the same scale as that mod) and by the weight of the thing:
    ///   things lying about are torn up and fly, tumbling, along the wind, and fall where it drops them - against a wall
    ///   they strike it; people stagger from 20, are knocked down from 28, thrown from 40 (heavier ones hold longer);
    ///   light buildings and walls take the load and can break, doors are battered, plants bend and the weak snap.
    /// With Atmosphere Overhaul loaded and its wind allowed (the setting), that mod's own wind does all this instead.</summary>
    public class MapComponent_MagicWind : MapComponent
    {
        // the wind with fire, gas, ash, water and lightning: see Carry
        public enum Shape { Out, Round, Front }
        public class Gust { public Shape shape; public Vector3 at, dir; public float radius, strength; public int born, life; public Pawn owner; public bool charged; }

        private readonly List<Gust> gusts = new List<Gust>();
        private readonly Dictionary<int, int> rest = new Dictionary<int, int>();   // a thing just moved is left alone a moment
        public MapComponent_MagicWind(Map map) : base(map) { }
        public static MapComponent_MagicWind Of(Map m) => m?.GetComponent<MapComponent_MagicWind>();

        /// <summary>charged - a storm's wind (a hurricane is a storm's): it carries lightning (Fix170).</summary>
        /// <summary>How strong a wind magic makes (Fix176), for the spell and for the editor alike. Plain air: 22 m/s by the
        /// caster's power, +30% an Amplify; the stream of the place (its tier) adds a fifth each; at most 120. A hurricane:
        /// 72 (its whirls 56), the stream as well - and it may go past the limit, up to 200: winds that spare no one
        /// but those under a ward of the storm.</summary>
        public const float AirMax = 120f, HurricaneMax = 200f;
        public static float AirStrength(float power, int amplify, Map map) =>
            Mathf.Min(AirMax, 22f * power * (1f + 0.3f * amplify) * (1f + 0.2f * Spells.FlowTierAny(map)) * AtmosphereBridge.Scale);
        public static float HurricaneStrength(float power, Map map, bool whirl = false) =>
            Mathf.Min(HurricaneMax, (whirl ? 56f : 72f) * power * (1f + 0.2f * Spells.FlowTierAny(map)) * AtmosphereBridge.Scale);
        public static string What(float s) => s >= 90f ? "tears everything up, throws everyone far" : s >= 40f ? "throws" : s >= 28f ? "fells" : s >= 20f ? "staggers" : s >= 16f ? "carries light things off" : "a breeze";

        /// <summary>Wards hold the wind off (Fix176): a ward of the storm (water and air - a hurricane's own) - entirely;
        /// one of the air (or of dust) - most of it; any other - a quarter. Holding it costs the ward a little.</summary>
        public static float Held(Pawn p, float s)
        {
            float f = 1f;
            foreach (IWard w in SpellWards.Layers(p).ToList())
            {
                if (w == null || w.Energy <= 0f) continue;
                RuneKind[] k = w.Parts ?? new RuneKind[0];
                float here = k.Contains(RuneKind.Air) && k.Contains(RuneKind.Water) ? 0f : k.Contains(RuneKind.Air) ? 0.4f : 0.75f;
                if (here < f) { f = here; w.Energy = Mathf.Max(0f, w.Energy - s * 0.02f); }
            }
            return f;
        }

        public static void Blow(Map map, Shape shape, Vector3 at, float radius, float strength, int life, Pawn owner, Vector3 dir = default, bool charged = false, float max = AirMax)
        {
            var c = Of(map);
            if (c == null || strength <= 0f) return;
            strength = Mathf.Min(strength, max);
            c.gusts.Add(new Gust { shape = shape, at = at, dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.forward, radius = radius, strength = strength, born = Find.TickManager.TicksGame, life = Mathf.Max(10, life), owner = owner, charged = charged });
        }

        /// <summary>The wind at a cell from one gust: its direction and its strength.</summary>
        private static Vector3 WindAt(Gust g, IntVec3 c, int now, out float s)
        {
            Vector3 o = c.ToVector3Shifted() - g.at; o.y = 0f;
            float d = o.magnitude;
            s = 0f;
            if (d > g.radius) return Vector3.zero;
            float age = 1f - (now - g.born) / (float)g.life;            // it dies down
            float fall = g.shape == Shape.Round ? Mathf.Clamp01(1.2f - Mathf.Abs(d - g.radius * 0.45f) / g.radius) : 1f - 0.6f * d / g.radius;
            s = g.strength * Mathf.Max(0f, fall) * Mathf.Clamp01(age * 1.4f);
            switch (g.shape)
            {
                case Shape.Out: return d < 0.3f ? Rand.InsideUnitCircleVec3.normalized : o / d;
                case Shape.Round: return d < 0.3f ? Vector3.zero : (new Vector3(-o.z, 0f, o.x) / d * 0.85f - o / d * 0.3f).normalized;
                default: return g.dir;
            }
        }

        public override void MapComponentTick()
        {
            if (gusts.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            gusts.RemoveAll(g => now - g.born > g.life);
            if (now % 10 != 0) return;
            foreach (Gust g in gusts.ToList())
            {
                IntVec3 mid = g.at.ToIntVec3();
                foreach (IntVec3 c in GenRadial.RadialCellsAround(mid, g.radius, true))
                {
                    if (!c.InBounds(map)) continue;
                    Vector3 w = WindAt(g, c, now, out float s);
                    if (s < 12f || w.sqrMagnitude < 0.01f) continue;
                    if (Rand.Chance(0.12f)) Streak(c, w, s);
                    Carry(g, c, w, s, now);
                    Act(g, c, w, s, now);
                }
            }
        }

        private bool Resting(Thing t, int now) => rest.TryGetValue(t.thingIDNumber, out int until) && until > now;
        private void Rest(Thing t, int now, int ticks) => rest[t.thingIDNumber] = now + ticks;

        private void Act(Gust g, IntVec3 c, Vector3 w, float s, int now)
        {
            foreach (Thing t in c.GetThingList(map).ToList())
            {
                if (t.Destroyed || Resting(t, now)) continue;
                if (t is Pawn p)
                {
                    if (p == g.owner || p.Dead || !p.Spawned || SpellWards.Stops(p, false)) continue;
                    float hold = Mathf.Sqrt(Mathf.Max(0.5f, p.BodySize));   // the heavier hold longer
                    float sp = s / hold * Held(p, s);
                    IntVec3 was = p.Position;
                    string did = null;
                    if (sp >= 40f)
                    {
                        IntVec3 from = (p.DrawPos - w * 3f).ToIntVec3();
                        if (from == p.Position) from = p.Position - new IntVec3(Mathf.RoundToInt(w.x * 2f), 0, Mathf.RoundToInt(w.z * 2f));
                        SpellHurl.Throw(p, from, Mathf.Clamp(Mathf.RoundToInt((sp - 34f) / 5f), 1, 6), map, g.owner, sp / 40f, 60);
                        // nowhere to fly, or held by something: knocked down where they stand, and the wind's debris strikes
                        if (p.Spawned && p.Position == was) { Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(p, 120); did = "felled (could not fly)"; } else did = "thrown";
                        // lifted and dropped: a bruise from the landing at least, more the harder it blew (debris)
                        p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Min(14f, 3f + (sp - 40f) * 0.3f), 0f, -1f, g.owner));
                        Rest(p, now, 90);
                    }
                    else if (sp >= 28f)
                    {
                        Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(p, 90);
                        p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(1f, 2.5f), 0f, -1f, g.owner));   // a fall: a small bruise
                        did = "knocked down"; Rest(p, now, 120);
                    }
                    else if (sp >= 20f) { p.stances?.stagger?.StaggerFor(60, 0.4f); did = "staggered"; Rest(p, now, 40); }
                    if (did != null && Prefs.DevMode) Log.Message("[Avir wind] " + p.LabelShort + ": " + did + " at " + sp.ToString("0") + " m/s");
                    continue;
                }
                if (t.def.EverHaulable && !(t is Corpse k && k.InnerPawn.BodySize > 1.5f))
                {
                    float mass = Mathf.Max(0.05f, t.GetStatValue(StatDefOf.Mass)) * Mathf.Sqrt(Mathf.Max(1, t.stackCount));
                    float need = 16f + 6f * Mathf.Sqrt(mass);
                    if (s < need || t.IsInAnyStorage() && s < need * 1.5f) continue;
                    WindBorne.Launch(t, w, Mathf.Clamp((s - need) / 4f + 1f, 1f, 9f), s, g.owner, g.charged && t.def.IsMetal);
                    continue;
                }
                if (t is Plant pl && s >= 26f)
                {
                    // the green bends; the weak snap, trees take the load
                    if (!pl.def.plant.IsTree && pl.def.plant.harvestYield <= 0f && Rand.Chance(0.25f)) pl.Destroy(DestroyMode.KillFinalize);
                    else if (pl.def.plant.IsTree && s >= 38f) pl.TakeDamage(new DamageInfo(DamageDefOf.Blunt, (s - 36f) * 0.6f, 0f, -1f, g.owner));
                    continue;
                }
                if (t is Building b && b.def.useHitPoints && s >= 30f)
                {
                    // the load on what stands: light things and walls break first, doors are battered
                    float light = b.MaxHitPoints <= 120 ? 2f : b.MaxHitPoints <= 300 ? 1f : 0.35f;
                    if (b.def.Fillage == FillCategory.Full || b is Building_Door || b.MaxHitPoints <= 300)
                    {
                        b.TakeDamage(new DamageInfo(DamageDefOf.Blunt, (s - 28f) * 0.35f * light, 0f, -1f, g.owner));
                        if (Rand.Chance(0.2f)) FleckMaker.ThrowDustPuffThick(b.TrueCenter(), map, 1.3f, new Color(0.7f, 0.7f, 0.68f, 0.6f));
                    }
                }
            }
        }

        /// <summary>What the wind carries (Fix170) - neither good nor bad of itself:
        ///   fire - a middling wind fans it and drives it on downwind, a strong one blows the small flames out;
        ///   gas - the game's own (smoke, tox gas, rot stink, deadlife dust - and every mod's gas that lives in the same
        ///   grid) and every gas that is a thing (ours: steam, marsh gas; any mod's of the old kind) drift with the wind,
        ///   a whirl turns them round itself; one's own gas blown back can cover one's own;
        ///   ash - blown on, it settles and feeds the ground where it lands;
        ///   water - a strong wind tears spray off water and puddles and soaks whoever is downwind;
        ///   lightning - a storm's wind (charged): the wet in it are struck now and then.</summary>
        private void Carry(Gust g, IntVec3 c, Vector3 w, float s, int now)
        {
            IntVec3 to = (c.ToVector3Shifted() + w * (s > 30f ? 2f : 1f)).ToIntVec3();
            bool canTo = to.InBounds(map) && to != c;
            // the game's gas grid: the whole cell's gas moves on, added to what is there
            if (canTo && map.gasGrid.AnyGasAt(c) && map.gasGrid.GasCanMoveTo(to) && Rand.Chance(Mathf.Clamp01(s / 30f)))
            {
                uint a = map.gasGrid.GetDirect(c), b = map.gasGrid.GetDirect(to);
                byte Sum(int sh) => (byte)Mathf.Min(255, (int)((a >> sh) & 0xFF) + (int)((b >> sh) & 0xFF));
                map.gasGrid.SetDirect(to, (uint)(Sum(0) | (Sum(8) << 8) | (Sum(16) << 16) | (Sum(24) << 24)));
                map.gasGrid.SetDirect(c, 0u);
            }
            var here = c.GetThingList(map);
            for (int i = here.Count - 1; i >= 0; i--)
            {
                Thing t = here[i];
                if (t.Destroyed || Resting(t, now)) continue;
                if (t is Gas gas && canTo && to.Walkable(map) && !to.GetThingList(map).Any(x => x.def == gas.def) && Rand.Chance(Mathf.Clamp01(s / 25f)))
                { gas.Position = to; Rest(gas, now, 20); continue; }
                if (t is Fire f)
                {
                    if (s > 35f && f.fireSize < 0.6f) { f.Destroy(); FleckMaker.ThrowSmoke(c.ToVector3Shifted(), map, 0.8f); continue; }   // blown out
                    f.fireSize = Mathf.Min(1.75f, f.fireSize + 0.04f * s / 20f);                                                         // fanned
                    if (canTo && Rand.Chance(Mathf.Clamp01((s - 10f) / 70f))) FireUtility.TryStartFireIn(to, map, 0.3f, g.owner);
                    continue;
                }
                if (t.def == ThingDefOf.Filth_Ash && canTo && to.Walkable(map) && Rand.Chance(0.4f))
                {
                    t.Position = to; Rest(t, now, 60);
                    MapComponent_LifeFertility.Of(map)?.Add(to, 0.15f, 3);   // ash settles and feeds the ground
                    continue;
                }
            }
            // spray torn off water
            if (s > 25f && Reactions.Wet(c, map) && Rand.Chance(0.3f))
            {
                FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1.2f);
                for (int k = 1; k <= 2; k++)
                {
                    IntVec3 d = (c.ToVector3Shifted() + w * k).ToIntVec3();
                    if (!d.InBounds(map)) continue;
                    foreach (Pawn p in d.GetThingList(map).OfType<Pawn>().ToList()) WaterMagic.Soak(p, 0.25f);
                }
            }
            // a storm's wind: lightning finds the wet and the metal
            if (g.charged && s > 20f)
                foreach (Pawn p in here.OfType<Pawn>().ToList())
                {
                    if (p == g.owner || p.Dead) continue;
                    bool wet = p.health.hediffSet.HasHediff(WaterDefOf.Avir_Soaked) || Reactions.Wet(c, map);
                    bool metal = SpellBody.Of(p).metalArmour;
                    if ((wet || metal) && Rand.Chance(wet && metal ? 0.12f : 0.06f))
                    {
                        p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 4f * s / 40f, 0f, -1f, g.owner));
                        p.stances?.stunner?.StunFor(40, g.owner, false, false);
                        FleckMaker.ThrowLightningGlow(p.DrawPos, map, 1.2f);
                        MapComponent_SpellSprites.Put(map, "Storm/Strike", p.DrawPos, 1.4f, 14);
                    }
                }
        }

        /// <summary>The wind at a cell now, the strongest of the gusts there (Fix175: shown in the game's own readout).</summary>
        public float At(IntVec3 c, out Vector3 dir)
        {
            dir = Vector3.zero; float best = 0f;
            if (gusts.Count == 0 || !c.InBounds(map)) return 0f;
            int now = Find.TickManager.TicksGame;
            foreach (Gust g in gusts) { Vector3 w = WindAt(g, c, now, out float s); if (s > best) { best = s; dir = w; } }
            return best;
        }

        public static string Readout(Map map, IntVec3 c)
        {
            var comp = Of(map);
            if (comp == null) return null;
            float s = comp.At(c, out Vector3 dir);
            if (s < 1f) return null;
            string arrow = dir.sqrMagnitude < 0.01f ? "" : " " + "→↗↑↖←↙↓↘"[Mathf.RoundToInt(((Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg) + 360f) % 360f / 45f) % 8];
            string what = What(s);
            return "Magic wind " + s.ToString("0") + " m/s" + arrow + " (" + what + ")";
        }

        /// <summary>A streak of dust along the wind: what the eye sees of it.</summary>
        private void Streak(IntVec3 c, Vector3 w, float s)
        {
            FleckCreationData d = FleckMaker.GetDataStatic(c.ToVector3Shifted() + new Vector3(Rand.Range(-0.4f, 0.4f), 0f, Rand.Range(-0.4f, 0.4f)), map, FleckDefOf.DustPuffThick, Rand.Range(0.6f, 1.1f));
            d.velocityAngle = w.AngleFlat();
            d.velocitySpeed = Mathf.Min(6f, s / 8f);
            d.rotationRate = Rand.Range(-90f, 90f);
            map.flecks.CreateFleck(d);
        }
    }

    /// <summary>A thing torn up by the wind: it flies along it in an arc, tumbling, a shadow under it, and comes down
    /// where the wind drops it - against a wall it strikes the wall and falls at its foot. Fragile things suffer from it.</summary>
    public class WindBorne : ThingWithComps, IThingHolder
    {
        private ThingOwner<Thing> inner;
        private Vector3 from, to;
        private int t, dur;
        private float spin, angle, height, speed;
        private Pawn by;
        private bool hitWall;

        public WindBorne() { inner = new ThingOwner<Thing>(this); }
        public ThingOwner GetDirectlyHeldThings() => inner;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        private bool charged;

        public static void Launch(Thing thing, Vector3 dir, float dist, float windSpeed, Pawn by, bool charged = false)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_WindBorne");
            Map map = thing.Map;
            if (def == null || map == null) return;
            IntVec3 start = thing.Position, end = start; bool wall = false;
            for (int i = 1; i <= Mathf.CeilToInt(dist); i++)
            {
                IntVec3 n = (start.ToVector3Shifted() + dir * i).ToIntVec3();
                if (!n.InBounds(map)) break;
                Building e = n.GetEdifice(map);
                if (e != null && e.def.Fillage == FillCategory.Full) { wall = true; if (e.def.useHitPoints) e.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Min(20f, thing.GetStatValue(StatDefOf.Mass) * windSpeed * 0.05f), 0f, -1f, by)); break; }
                end = n;
            }
            if (end == start) return;
            var w = (WindBorne)ThingMaker.MakeThing(def);
            w.from = start.ToVector3Shifted(); w.to = end.ToVector3Shifted(); w.by = by; w.hitWall = wall; w.speed = windSpeed; w.charged = charged;
            w.dur = Mathf.Clamp(Mathf.RoundToInt((w.to - w.from).magnitude * 7f), 10, 90);
            w.height = Mathf.Clamp((w.to - w.from).magnitude * 0.18f, 0.3f, 1.6f);
            w.spin = Rand.Range(240f, 720f) * (Rand.Bool ? 1f : -1f);
            thing.DeSpawn();
            w.inner.TryAdd(thing);
            GenSpawn.Spawn(w, start, map);
        }

        public override Vector3 DrawPos
        {
            get
            {
                float f = dur > 0 ? Mathf.Clamp01(t / (float)dur) : 1f;
                Vector3 p = Vector3.Lerp(from, to, f);
                p.z += height * 4f * f * (1f - f);   // the arc
                p.y = AltitudeLayer.Skyfaller.AltitudeFor();
                return p;
            }
        }

        protected override void Tick()
        {
            t++; angle += spin / 60f;
            if (Spawned && t % 4 == 0) Position = DrawPos.ToIntVec3().InBounds(Map) ? DrawPos.ToIntVec3() : Position;
            if (t >= dur) Land();
        }

        private void Land()
        {
            Map map = Map; IntVec3 at = to.ToIntVec3();
            if (map == null) { Destroy(); return; }
            Thing thing = inner.FirstOrDefault();
            if (thing != null)
            {
                inner.Remove(thing);
                // falling hurts the fragile, and whoever it lands on
                if (thing.def.useHitPoints && speed > 30f) thing.HitPoints = Mathf.Max(1, thing.HitPoints - Mathf.RoundToInt(thing.MaxHitPoints * Mathf.Min(0.3f, (speed - 30f) / 100f)));
                Pawn under = at.InBounds(map) ? at.GetFirstPawn(map) : null;
                if (under != null) under.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Min(12f, thing.GetStatValue(StatDefOf.Mass) * 2f), 0f, -1f, by));
                if (!GenPlace.TryPlaceThing(thing, at.InBounds(map) ? at : Position, map, ThingPlaceMode.Near)) thing.Destroy();
                FleckMaker.ThrowDustPuff(at, map, hitWall ? 1.2f : 0.7f);
                if (charged)
                {
                    // metal carried by a storm's wind comes down with the lightning it gathered
                    foreach (Pawn q in GenRadial.RadialDistinctThingsAround(at, map, 1.5f, true).OfType<Pawn>().ToList()) q.TakeDamage(new DamageInfo(DamageDefOf.Burn, 5f, 0f, -1f, by));
                    FleckMaker.ThrowLightningGlow(at.ToVector3Shifted(), map, 1.5f);
                    MapComponent_SpellSprites.Put(map, "Storm/Strike", at.ToVector3Shifted(), 1.6f, 14);
                }
            }
            Destroy();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Thing thing = inner.FirstOrDefault();
            if (thing == null) return;
            Vector3 shadow = Vector3.Lerp(from, to, dur > 0 ? Mathf.Clamp01(t / (float)dur) : 1f); shadow.y = AltitudeLayer.Shadows.AltitudeFor();
            Graphics.DrawMesh(MeshPool.plane08, Matrix4x4.TRS(shadow, Quaternion.identity, new Vector3(0.6f, 1f, 0.35f)), MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowCircle", ShaderDatabase.Transparent, new Color(1f, 1f, 1f, 0.5f)), 0);
            thing.Graphic?.Draw(DrawPos, Rot4.North, thing, angle);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref inner, "inner", this);
            Scribe_Values.Look(ref from, "from"); Scribe_Values.Look(ref to, "to");
            Scribe_Values.Look(ref t, "t"); Scribe_Values.Look(ref dur, "dur");
            Scribe_Values.Look(ref spin, "spin"); Scribe_Values.Look(ref angle, "angle"); Scribe_Values.Look(ref height, "height"); Scribe_Values.Look(ref speed, "speed");
            Scribe_Values.Look(ref hitWall, "hitWall"); Scribe_Values.Look(ref charged, "charged");
            Scribe_References.Look(ref by, "by");
        }
    }
}

namespace Avir.Magic
{
    public static class MagicWindDev
    {
        [LudeonTK.DebugAction("Avir magic", "Wind: blast out here (46 m/s)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Out() => MapComponent_MagicWind.Blow(Find.CurrentMap, MapComponent_MagicWind.Shape.Out, Verse.UI.MouseCell().ToVector3Shifted(), 6f, 46f, 60, null);

        [LudeonTK.DebugAction("Avir magic", "Wind: whirl here (44 m/s, 5 s)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Round() => MapComponent_MagicWind.Blow(Find.CurrentMap, MapComponent_MagicWind.Shape.Round, Verse.UI.MouseCell().ToVector3Shifted(), 3f, 44f, 300, null);

        [LudeonTK.DebugAction("Avir magic", "Wind: front east (32 m/s, 3 s)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Front() => MapComponent_MagicWind.Blow(Find.CurrentMap, MapComponent_MagicWind.Shape.Front, Verse.UI.MouseCell().ToVector3Shifted(), 7f, 32f, 180, null, UnityEngine.Vector3.right);
    }
}

namespace Avir.Magic
{
    /// <summary>The magic wind in the game's own readout at the bottom left (the author's choice, Fix175): our line goes
    /// at the bottom of it, the game's lines move up by one.</summary>
    [HarmonyLib.HarmonyPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
    public static class Patch_WindReadout
    {
        private static readonly System.Reflection.FieldInfo botLeft = HarmonyLib.AccessTools.Field(typeof(MouseoverReadout), "BotLeft");
        private static Vector2 saved; private static bool moved;

        public static void Prefix()
        {
            moved = false;
            if (botLeft == null || Event.current.type != EventType.Repaint || Find.CurrentMap == null || Find.MainTabsRoot.OpenTab != null) return;
            string line = MapComponent_MagicWind.Readout(Find.CurrentMap, Verse.UI.MouseCell());
            if (line == null) return;
            saved = (Vector2)botLeft.GetValue(null);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.88f, 1f);
            Widgets.Label(new Rect(saved.x, Verse.UI.screenHeight - saved.y, 999f, 999f), line);
            GUI.color = Color.white;
            botLeft.SetValue(null, new Vector2(saved.x, saved.y + 19f));
            moved = true;
        }

        public static void Postfix() { if (moved && botLeft != null) botLeft.SetValue(null, saved); moved = false; }
    }
}
