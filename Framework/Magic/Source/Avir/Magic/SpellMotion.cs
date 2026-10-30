using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>A mage carried by their element, seen all the way: no sudden vanishing (a true jump through space
    /// belongs to the runes of space). Fire and water run along the ground; air lifts the mage high over whatever
    /// stands between; earth takes the mage under the ground - only a running crack of dust shows where they go - and
    /// throws them up again anywhere on the map, scattering the foes about them.</summary>
    public class SpellDash : PawnFlyer
    {
        public RuneKind element = RuneKind.Fire;
        public float power = 1f;

        public static void Carry(Pawn p, IntVec3 dest, RuneKind el, float power = 1f)
        {
            Map map = p.Map;
            if (map == null || !dest.InBounds(map) || !dest.Standable(map) || dest == p.Position) return;
            string name = el == RuneKind.Earth ? "Avir_SpellBurrow" : el == RuneKind.Air ? "Avir_SpellFlight" : "Avir_SpellDash";
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name) ?? DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpellDash");
            IntVec3 from = p.Position;
            if (def == null) { p.Position = dest; p.Notify_Teleported(true, false); return; }
            if (el == RuneKind.Earth) Burst(from, map, p, 1.5f, 0, power);
            var flyer = (SpellDash)MakeFlyer(def, p, dest, null, null);
            if (flyer == null) return;
            flyer.element = el;
            flyer.power = power;
            GenSpawn.Spawn(flyer, from, map);
        }

        /// <summary>Foes round a point thrown off and stunned; with dust of the element.</summary>
        public static void Burst(IntVec3 at, Map map, Pawn mage, float radius, int push, float power)
        {
            Color dust = MagicFx.EarthDust;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, radius, true))
                if (c.InBounds(map)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 2f, dust);
            if (push <= 0) return;
            foreach (Pawn q in GenRadial.RadialDistinctThingsAround(at, map, radius, true).OfType<Pawn>().ToList())
            {
                if (!MagicFx.Foe(q, mage)) continue;
                MagicFx.Push(q, at, push, map);
                MagicFx.Stun(q, Mathf.RoundToInt(120f * power), mage);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref element, "element", RuneKind.Fire);
            Scribe_Values.Look(ref power, "power", 1f);
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (element == RuneKind.Earth) return; // under the ground: nothing of the mage is seen
            base.DynamicDrawPhaseAt(phase, drawLoc, flip);
        }

        protected override void RespawnPawn()
        {
            Map map = Map; IntVec3 at = Position; Pawn mage = FlyingPawn; RuneKind el = element; float pw = power;
            base.RespawnPawn();
            if (map == null || mage == null) return;
            if (el == RuneKind.Earth)
            {
                // the ground breaks open and throws the mage up; those standing round are thrown off their feet
                Burst(at, map, mage, 2.5f + 0.5f * pw, 2, pw);
                foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 2.5f, true)) if (Rand.Chance(0.4f)) MagicFx.Rubble(c, map);
            }
            else if (el == RuneKind.Air)
            {
                // the mage comes down on a gust
                foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 2f, true)) if (c.InBounds(map) && Rand.Chance(0.6f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.6f, MagicFx.AirDust);
                foreach (Pawn q in GenRadial.RadialDistinctThingsAround(at, map, 2f, true).OfType<Pawn>().ToList()) if (MagicFx.Foe(q, mage)) MagicFx.Push(q, at, 2, map);
            }
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!Spawned) return;
            Vector3 at = DrawPos;
            if (element == RuneKind.Water)
            {
                if (this.IsHashIntervalTick(2, delta)) FleckMaker.Static(at.ToIntVec3(), Map, FleckDefOf.WaterSplash, 1.4f);
                if (this.IsHashIntervalTick(4, delta)) FleckMaker.Static(at.ToIntVec3(), Map, FleckDefOf.WaterRipple, 1.6f);
            }
            else if (element == RuneKind.Earth)
            {
                // the crack running through the ground above
                if (this.IsHashIntervalTick(3, delta)) FleckMaker.ThrowDustPuffThick(new Vector3(at.x, 0f, at.z) + Rand.InsideUnitCircleVec3 * 0.3f, Map, 1.3f, MagicFx.EarthDust);
            }
            else if (element == RuneKind.Air)
            {
                if (this.IsHashIntervalTick(4, delta)) FleckMaker.ThrowDustPuffThick(at, Map, 1.2f, MagicFx.AirDust);
            }
            else
            {
                FleckMaker.ThrowFireGlow(at, Map, 1.1f);
                if (this.IsHashIntervalTick(3, delta)) FleckMaker.ThrowMicroSparks(at, Map);
            }
        }
    }

    /// <summary>The tide: a field of mist round a water mage for a while. Foes in it are slowed and see poorly; fire
    /// will not start in it; its verbs work again every few seconds.</summary>
    public class MapComponent_Tides : MapComponent
    {
        public class Tide { public Pawn mage; public float radius, power; public int until; public List<RuneDef> verbs; public RuneKind element = RuneKind.Water; }
        public readonly List<Tide> tides = new List<Tide>();

        public MapComponent_Tides(Map map) : base(map) { }

        public static void Raise(Pawn mage, float radius, float power, List<RuneDef> verbs, RuneKind element = RuneKind.Water)
        {
            var c = mage.Map?.GetComponent<MapComponent_Tides>();
            if (c == null) return;
            c.tides.RemoveAll(t => t.mage == mage);
            c.tides.Add(new Tide { mage = mage, radius = radius, power = power, verbs = verbs, element = element, until = Find.TickManager.TicksGame + Mathf.RoundToInt(2500f * power) });
        }

        /// <summary>The earth and air fields. Earth: friends in it get skin of stone, foes wade as through mud.
        /// Air: a whirlwind - foes buffeted, slowed and shooting badly, and thrown back when they come close.</summary>
        private void Field(Tide t, int now)
        {
            IntVec3 c = t.mage.Position;
            bool earth = t.element == RuneKind.Earth;
            if (now % 15 == 0)
                for (int i = 0; i < 2; i++)
                    FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * t.radius, map, Rand.Range(1.2f, 2f), earth ? MagicFx.EarthDust : MagicFx.AirDust);
            if (now % 60 == 0)
            {
                var cells = FireMagic.Point(c, map, t.radius);
                foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                {
                    if (p == t.mage) continue;
                    if (!p.HostileTo(t.mage)) { if (earth) MagicFx.Give(p, EarthAirDefOf.Avir_StoneSkin, 120); continue; }
                    MagicFx.Give(p, earth ? EarthAirDefOf.Avir_HeavyGround : EarthAirDefOf.Avir_Buffeted, 90);
                    if (!earth && p.Position.DistanceTo(c) < 2.5f) MagicFx.Push(p, c, 2, map);
                }
            }
            if (now % 180 == 0 && t.verbs != null && t.verbs.Count > 0)
            {
                var cells = FireMagic.Point(c, map, t.radius);
                var r = new RitualReading();
                if (earth) EarthMagic.Apply(t.mage, map, t.verbs, cells, c, t.power * 0.6f, r);
                else AirMagic.Apply(t.mage, map, t.verbs, cells, c, t.power * 0.6f, r);
            }
        }

        public override void MapComponentTick()
        {
            if (tides.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            tides.RemoveAll(t => t.until <= now || t.mage == null || t.mage.Dead || !t.mage.Spawned || t.mage.Map != map);
            foreach (Tide t in tides)
            {
                IntVec3 c = t.mage.Position;
                if (t.element != RuneKind.Water) { Field(t, now); continue; }
                // the mist, all round
                if (now % 12 == 0)
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 p = c.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * t.radius;
                        FleckMaker.ThrowDustPuffThick(p, map, Rand.Range(1.6f, 2.6f), new Color(0.8f, 0.88f, 1f, 0.35f));
                    }
                if (now % 60 == 0)
                {
                    var cells = FireMagic.Point(c, map, t.radius);
                    foreach (IntVec3 x in cells) FireMagic.Extinguish(x, map);
                    foreach (Pawn p in cells.SelectMany(x => x.GetThingList(map).OfType<Pawn>()).Distinct().ToList())
                    {
                        if (p == t.mage || !p.HostileTo(t.mage)) continue;
                        WaterMagic.Soak(p, t.power * 0.5f);
                        Hediff m = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_InMist) ?? p.health.AddHediff(WaterDefOf.Avir_InMist);
                        HediffComp_Disappears d = m.TryGetComp<HediffComp_Disappears>();
                        if (d != null) d.ticksToDisappear = 90;
                    }
                }
                // its verbs again, every three seconds
                if (now % 180 == 0 && t.verbs != null && t.verbs.Count > 0) WaterMagic.ApplyAround(t.mage, t.verbs, t.radius, t.power);
            }
        }
    }

    /// <summary>Shallow water raised for a while: a moat. When it drains, the ground comes back as it was.</summary>
    public class MapComponent_RaisedWater : MapComponent
    {
        private Dictionary<IntVec3, KeyValuePair<TerrainDef, int>> raised = new Dictionary<IntVec3, KeyValuePair<TerrainDef, int>>();
        private List<IntVec3> keys; private List<TerrainDef> olds; private List<int> untils;

        public MapComponent_RaisedWater(Map map) : base(map) { }

        public void Raise(IntVec3 c, int ticks)
        {
            if (!c.InBounds(map) || c.GetEdifice(map) != null) return;
            TerrainDef cur = map.terrainGrid.TerrainAt(c);
            if (cur == null || cur.IsWater || cur.bridge) return;
            int until = Find.TickManager.TicksGame + ticks;
            if (raised.TryGetValue(c, out var kv)) { raised[c] = new KeyValuePair<TerrainDef, int>(kv.Key, Mathf.Max(kv.Value, until)); return; }
            raised[c] = new KeyValuePair<TerrainDef, int>(cur, until);
            map.terrainGrid.SetTerrain(c, TerrainDefOf.WaterShallow);
        }

        public override void MapComponentTick()
        {
            if (raised.Count == 0 || Find.TickManager.TicksGame % 250 != 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (var kv in raised.Where(x => x.Value.Value <= now).ToList())
            {
                if (map.terrainGrid.TerrainAt(kv.Key) == TerrainDefOf.WaterShallow) map.terrainGrid.SetTerrain(kv.Key, kv.Value.Key);
                raised.Remove(kv.Key);
            }
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) { keys = raised.Keys.ToList(); olds = keys.Select(k => raised[k].Key).ToList(); untils = keys.Select(k => raised[k].Value).ToList(); }
            Scribe_Collections.Look(ref keys, "keys", LookMode.Value);
            Scribe_Collections.Look(ref olds, "olds", LookMode.Def);
            Scribe_Collections.Look(ref untils, "untils", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && keys != null && olds != null && untils != null)
            {
                raised = new Dictionary<IntVec3, KeyValuePair<TerrainDef, int>>();
                for (int i = 0; i < keys.Count && i < olds.Count && i < untils.Count; i++) raised[keys[i]] = new KeyValuePair<TerrainDef, int>(olds[i], untils[i]);
            }
        }
    }

    /// <summary>A water spirit near its mage catches one shot meant for them, and is gone.</summary>
    [HarmonyPatch(typeof(Bullet), "Impact")]
    public static class Patch_SpiritCatchesShot
    {
        public static bool Prefix(Bullet __instance, Thing hitThing)
        {
            if (!AvirMagic.On || !(hitThing is Pawn p) || p.Map == null) return true;
            WaterSpirit spirit = WaterSpirit.Guarding(p);
            if (spirit == null) return true;
            FleckMaker.Static(p.Position, p.Map, FleckDefOf.WaterSplash, 2f);
            MoteMaker.ThrowText(p.DrawPos, p.Map, "Caught", 2.5f);
            spirit.Destroy();
            __instance.Destroy();
            return false;
        }
    }

    /// <summary>Wards and those who strike them: a fire ward burns whoever hits its wearer up close.</summary>
    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), "ApplyMeleeDamageToTarget")]
    public static class Patch_WardStrikesBack
    {
        public static void Postfix(Verb_MeleeAttackDamage __instance, LocalTargetInfo target)
        {
            if (!AvirMagic.On || !(target.Thing is Pawn t) || t.Dead) return;
            Pawn att = __instance.CasterPawn;
            if (att == null || att.Dead) return;
            if (t.health.hediffSet.GetFirstHediffOfDef(FireDefOf.Avir_FireWard) != null)
            {
                att.TakeDamage(new DamageInfo(DamageDefOf.Burn, 4f, 0f, -1f, t));
                FleckMaker.ThrowFireGlow(att.DrawPos, att.Map, 0.8f);
            }
            if (att.Spawned && t.health.hediffSet.GetFirstHediffOfDef(EarthAirDefOf.Avir_AirWard) != null) MagicFx.Push(att, t.Position, 2, att.Map);
        }
    }
}
