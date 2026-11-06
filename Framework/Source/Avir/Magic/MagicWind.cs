using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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
        public enum Shape { Out, Round, Front }
        public class Gust { public Shape shape; public Vector3 at, dir; public float radius, strength; public int born, life; public Pawn owner; }

        private readonly List<Gust> gusts = new List<Gust>();
        private readonly Dictionary<int, int> rest = new Dictionary<int, int>();   // a thing just moved is left alone a moment
        public MapComponent_MagicWind(Map map) : base(map) { }
        public static MapComponent_MagicWind Of(Map m) => m?.GetComponent<MapComponent_MagicWind>();

        public static void Blow(Map map, Shape shape, Vector3 at, float radius, float strength, int life, Pawn owner, Vector3 dir = default)
        {
            var c = Of(map);
            if (c == null || strength <= 0f) return;
            c.gusts.Add(new Gust { shape = shape, at = at, dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.forward, radius = radius, strength = strength, born = Find.TickManager.TicksGame, life = Mathf.Max(10, life), owner = owner });
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
                    float sp = s / hold;
                    if (sp >= 40f)
                    {
                        IntVec3 from = (p.DrawPos - w * 2f).ToIntVec3();
                        SpellHurl.Throw(p, from, Mathf.Clamp(Mathf.RoundToInt((sp - 34f) / 5f), 1, 6), map, g.owner, sp / 40f, 60);
                        Rest(p, now, 90);
                    }
                    else if (sp >= 28f && p.jobs != null) { Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(p, 90); Rest(p, now, 120); }
                    else if (sp >= 20f) { p.stances?.stagger?.StaggerFor(60, 0.4f); Rest(p, now, 40); }
                    continue;
                }
                if (t.def.EverHaulable && !(t is Corpse k && k.InnerPawn.BodySize > 1.5f))
                {
                    float mass = Mathf.Max(0.05f, t.GetStatValue(StatDefOf.Mass)) * Mathf.Sqrt(Mathf.Max(1, t.stackCount));
                    float need = 16f + 6f * Mathf.Sqrt(mass);
                    if (s < need || t.IsInAnyStorage() && s < need * 1.5f) continue;
                    WindBorne.Launch(t, w, Mathf.Clamp((s - need) / 4f + 1f, 1f, 9f), s, g.owner);
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

        public static void Launch(Thing thing, Vector3 dir, float dist, float windSpeed, Pawn by)
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
            w.from = start.ToVector3Shifted(); w.to = end.ToVector3Shifted(); w.by = by; w.hitWall = wall; w.speed = windSpeed;
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
            Scribe_Values.Look(ref hitWall, "hitWall");
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
