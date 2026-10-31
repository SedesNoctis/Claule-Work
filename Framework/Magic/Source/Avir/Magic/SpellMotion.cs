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
        /// <summary>Set when the step is turned round (a thread in the circle): not the mage going, but someone
        /// dragged to the mage - and they were not ready for it.</summary>
        public Pawn puller;

        /// <summary>A step of two elements met: carried the way that meeting goes - a cloud, a bolt, through lava or
        /// mire under the ground, unseen in a shimmer, in a dust devil - with its own landing.</summary>
        public ComboKind combo;

        public static void Travel(Pawn p, IntVec3 dest, ComboKind k, float power)
        {
            Map map = p.Map;
            if (map == null || !dest.InBounds(map) || !dest.Standable(map) || dest == p.Position) return;
            string name = k == ComboKind.Magma || k == ComboKind.Bog ? "Avir_SpellBurrow" : k == ComboKind.Storm ? "Avir_SpellBoltRide" : k == ComboKind.Steam ? "Avir_SpellDash" : "Avir_SpellFlight";
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name) ?? DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpellDash");
            if (def == null) return;
            ComboMagic.Depart(k, p, dest, power);
            IntVec3 from = p.Position;
            var flyer = (SpellDash)MakeFlyer(def, p, dest, null, null);
            if (flyer == null) return;
            flyer.combo = k; flyer.power = power;
            flyer.element = k == ComboKind.Magma || k == ComboKind.Bog ? RuneKind.Earth : k == ComboKind.Steam ? RuneKind.Water : RuneKind.Air;
            GenSpawn.Spawn(flyer, from, map);
        }

        /// <summary>Who the last pull took - the chain of a spell waits until they have arrived.</summary>
        public static List<Pawn> LastPulled;

        /// <summary>The step turned round: whoever is chosen is carried to the mage the way the mage would have gone
        /// to them. Water gathers every foe at the point; fire, earth and air take one. A friend comes unharmed; a foe
        /// was not ready, and suffers for it: fire bursts where they stood (and they arrive without the flame), water
        /// soaks and knocks them down, air leaves them reeling, earth drags them under and they come up stunned
        /// senseless. Those the earth roots in place cannot be moved.</summary>
        public static void Pull(Pawn mage, LocalTargetInfo target, RuneKind el, float power, float radius)
        {
            Map map = mage.Map;
            if (map == null || !target.Cell.InBounds(map)) return;
            var taken = new List<Pawn>();
            if (target.Thing is Pawn chosen && chosen != mage) taken.Add(chosen);
            else
            {
                var near = MagicFx.PawnsIn(FireMagic.Point(target.Cell, map, radius), map).Where(p => p != mage).OrderBy(p => p.Position.DistanceToSquared(target.Cell)).ToList();
                if (el == RuneKind.Water) taken.AddRange(near.Where(p => MagicFx.Foe(p, mage)));
                else { Pawn one = near.FirstOrDefault(p => MagicFx.Foe(p, mage)) ?? near.FirstOrDefault(); if (one != null) taken.Add(one); }
            }
            if (taken.Count == 0) { Messages.Message("No one there to draw in.", MessageTypeDefOf.RejectInput, false); return; }
            string name = el == RuneKind.Earth ? "Avir_SpellBurrow" : el == RuneKind.Air ? "Avir_SpellFlight" : "Avir_SpellDash";
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
            if (def == null) return;
            var used = new HashSet<IntVec3>();
            foreach (Pawn p in taken)
            {
                if (p.Dead || !p.Spawned || MagicFx.Rooted(p)) continue;
                IntVec3 to = BesideMage(mage, p.Position, used);
                if (!to.IsValid) break;
                used.Add(to);
                bool foe = MagicFx.Foe(p, mage);
                IntVec3 from = p.Position;
                var flyer = (SpellDash)MakeFlyer(def, p, to, null, null);
                if (flyer == null) continue;
                flyer.element = el; flyer.power = power; flyer.puller = foe ? mage : null;
                GenSpawn.Spawn(flyer, from, map);
                if (foe && el == RuneKind.Fire)
                {
                    // where the path begins the fire bursts for real - torn out of it, the foe takes the burns along
                    GenExplosion.DoExplosion(from, map, 1.9f + 0.3f * power, DamageDefOf.Flame, mage, Mathf.RoundToInt(8f * power), chanceToStartFire: 0.5f);
                }
                (LastPulled ?? (LastPulled = new List<Pawn>())).Add(p);
            }
        }

        /// <summary>A free cell by the mage, on the side the pulled one comes from.</summary>
        private static IntVec3 BesideMage(Pawn mage, IntVec3 from, HashSet<IntVec3> used)
        {
            Map map = mage.Map;
            return GenRadial.RadialCellsAround(mage.Position, 3.5f, false)
                .Where(c => c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null && !used.Contains(c))
                .OrderBy(c => c.DistanceToSquared(mage.Position) * 4 + c.DistanceToSquared(from) * 0.01f)
                .FirstOrFallback(IntVec3.Invalid);
        }

        /// <summary>A foe dragged to the mage lands unready.</summary>
        private static void Arrive(Pawn p, Pawn mage, RuneKind el, float power, Map map)
        {
            // dragged across the ground, the foe comes to their senses only after they have got up:
            // while they lie they neither strike nor shoot
            int down = Mathf.RoundToInt((el == RuneKind.Water ? 150f : el == RuneKind.Air ? 90f : 120f) * power);
            switch (el)
            {
                case RuneKind.Water:
                    WaterMagic.Soak(p, power);
                    FleckMaker.Static(p.Position, map, FleckDefOf.WaterSplash, 2f);
                    p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5f * power, 0f, -1f, mage));
                    if (!p.Dead) Down(p, mage, down);
                    break;
                case RuneKind.Air:
                    MagicFx.Give(p, EarthAirDefOf.Avir_Dizzy, Mathf.RoundToInt(GenDate.TicksPerHour * 0.5f * power));
                    p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 4f * power, 0f, -1f, mage));
                    if (!p.Dead) Down(p, mage, down);
                    break;
                case RuneKind.Earth:
                    FleckMaker.ThrowDustPuffThick(p.DrawPos, map, 2.2f, MagicFx.EarthDust);
                    MagicFx.Rubble(p.Position, map);
                    p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 6f * power, 0f, -1f, mage));
                    if (!p.Dead) p.stances?.stunner?.StunFor(Mathf.RoundToInt(420f * power), mage);
                    break;
                case RuneKind.Fire:
                    // burnt through the flash: the skin smoulders, the foe is thrown down at the mage's feet
                    FleckMaker.ThrowFireGlow(p.DrawPos, map, 1.5f);
                    FleckMaker.ThrowSmoke(p.DrawPos, map, 1.2f);
                    p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 7f * power, 0f, -1f, mage));
                    p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 3f * power, 0f, -1f, mage));
                    if (!p.Dead) Down(p, mage, down);
                    break;
                default:
                    p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 4f * power, 0f, -1f, mage));
                    if (!p.Dead) Down(p, mage, down);
                    break;
            }
        }

        /// <summary>Laid on the ground and stunned for as long: the knockdown alone lets a pawn in melee strike from the floor.</summary>
        private static void Down(Pawn p, Pawn mage, int ticks)
        {
            int t = Mathf.Max(60, Mathf.RoundToInt(ticks * Avir.Artifactorics.AmuletBattleTelekinesis.KnockdownFactor(p)));
            p.stances?.stunner?.StunFor(t, mage, addBattleLog: false, showMote: false);
            Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(p, t);
        }

        private static void AmuletLayDown(Pawn p, int ticks) =>
            Avir.Artifactorics.AmuletBattleTelekinesis.LayDown(p, Mathf.Max(30, Mathf.RoundToInt(ticks * Avir.Artifactorics.AmuletBattleTelekinesis.KnockdownFactor(p))));

        /// <summary>The verbs of the step's own circle, worked round the mage where they come out (earth, air).</summary>
        public List<RuneDef> verbs;
        public int amplify, breadth;

        public static void Carry(Pawn p, IntVec3 dest, RuneKind el, float power = 1f, List<RuneDef> verbs = null, RitualReading r = null)
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
            if (verbs != null && verbs.Count > 0) { flyer.verbs = verbs.ToList(); flyer.amplify = r?.amplify ?? 0; flyer.breadth = r?.breadth ?? 0; }
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
                MagicFx.Fell(q, at, push, map, mage, power, Mathf.RoundToInt(120f * power));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref element, "element", RuneKind.Fire);
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_References.Look(ref puller, "puller");
            Scribe_Values.Look(ref combo, "combo", ComboKind.None);
            Scribe_Collections.Look(ref verbs, "verbs", LookMode.Def);
            Scribe_Values.Look(ref amplify, "amplify");
            Scribe_Values.Look(ref breadth, "breadth");
        }

        private static Material waveMat;

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (element == RuneKind.Earth || combo == ComboKind.Haze || combo == ComboKind.Steam) return; // under the ground, in a shimmer, a cloud: nothing of the mage is seen
            if (element == RuneKind.Water && puller == null && phase == DrawPhase.Draw)
            {
                // the wave itself, rolling ahead of the mage
                if (waveMat == null) waveMat = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Wave_Water", ShaderDatabase.Transparent);
                Vector3 dir = (DestinationPos - drawLoc); dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    float ang = dir.AngleFlat();
                    Vector3 at = drawLoc + dir.normalized * 0.7f; at.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.AngleAxis(ang, Vector3.up), new Vector3(3f, 1f, 3f)), waveMat, 0);
                }
            }
            base.DynamicDrawPhaseAt(phase, drawLoc, flip);
        }

        protected override void RespawnPawn()
        {
            Map map = Map; IntVec3 at = Position; Pawn mage = FlyingPawn; RuneKind el = element; float pw = power; Pawn by = puller;
            base.RespawnPawn();
            if (map == null || mage == null) return;
            if (by != null) { if (!mage.Dead) Arrive(mage, by, el, pw, map); return; }
            if (combo != ComboKind.None) { if (!mage.Dead) ComboMagic.Land(combo, mage, at, pw); return; }
            if (verbs != null && verbs.Count > 0 && !mage.Dead)
            {
                // the circle's verbs, outward of the step: they work where the mage comes out
                var rr = new RitualReading { amplify = amplify, breadth = breadth };
                var cells = FireMagic.Point(at, map, FireMagic.PointRadius(rr)).Where(c => c != at).ToList();
                if (el == RuneKind.Earth) EarthMagic.Apply(mage, map, verbs, cells, at, pw, rr);
                else if (el == RuneKind.Air) AirMagic.Apply(mage, map, verbs, cells, at, pw, rr);
            }
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
            if (combo != ComboKind.None)
            {
                // the meeting's own trail: a cloud, sparks of a bolt, a glowing crack, a shimmer, bubbles of mire, dust
                if (this.IsHashIntervalTick(3, delta))
                {
                    IntVec3 c = new Vector3(at.x, 0f, at.z).ToIntVec3();
                    if (combo == ComboKind.Magma) FleckMaker.ThrowFireGlow(new Vector3(at.x, 0f, at.z), Map, 0.9f);
                    else if (combo == ComboKind.Bog) { if (c.InBounds(Map)) FleckMaker.Static(c, Map, FleckDefOf.WaterRipple, 1.2f); }
                    else if (combo == ComboKind.Storm) FleckMaker.ThrowLightningGlow(at, Map, 0.9f);
                    else if (combo == ComboKind.Haze) { if (c.InBounds(Map)) FleckMaker.ThrowHeatGlow(c, Map, 1.2f); }
                    else if (combo == ComboKind.Dust) MapComponent_SpellSprites.Put(Map, "Dust/Devil", new Vector3(at.x, 0f, at.z), 2.2f, 20, -1f, -8f);
                    else FleckMaker.ThrowDustPuffThick(at, Map, 1.5f, Combos.ColorOf(combo));
                }
                return;
            }
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
    // on the blow itself, whatever weapon or mod makes it: any melee hit that lands
    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class Patch_WardStrikesBack
    {
        public static void Postfix(Verb_MeleeAttack __instance, bool __result)
        {
            if (!__result) return;
            LocalTargetInfo target = __instance.CurrentTarget;
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
