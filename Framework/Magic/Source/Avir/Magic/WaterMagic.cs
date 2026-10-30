using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    // =====================================================================================
    // WATER, by the grammar of the runes. Water does not burn: it pushes, soaks, slows and quenches. A soaked foe is
    // slower and hard to set alight; held water lies where it was put and keeps fire away; water drawn out of a body
    // weakens it, and the mage keeps what was drawn for the next spell. Its price: the mage is chilled to the bone.
    //   release - a burst: foes knocked back and soaked, fires out      hold - standing water: slows foes, keeps fire off
    //   drain - water drawn from foes (parched), kept by the mage       lift - a geyser: foes thrown up and dropped
    //   growth - the water spreads and plants drink                     purge - washes filth and poison away, fires out
    //   aura - round the mage   beam - a jet   shape - a curtain of water   step - the mage rides a wave
    //   restore - wounds washed and bound   enchant - blows that knock back   summon - a water spirit   sense - moving bodies
    //   barrier - a ward of water: fire bites far less
    // =====================================================================================
    public static class WaterMagic
    {
        public static bool Is(RuneDef el) => el != null && el.kind == RuneKind.Water;

        public static void Cast(Pawn caster, RitualSchema s, RitualReading r, LocalTargetInfo target, float power)
        {
            Map map = caster.Map;
            Hediff kept = caster.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_StoredWater);
            bool drains = r.verbs.Any(v => v.kind == RuneKind.Drain);
            if (kept != null && !drains) { power *= 1f + kept.Severity; caster.health.RemoveHediff(kept); }
            IntVec3 at = target.Cell;
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            float radius = FireMagic.PointRadius(r);
            if (r.barrier > 0) Ward(caster, forms.Any(f => f.kind == RuneKind.Aura) ? caster.Position : at, map, Mathf.Max(0.9f, radius - 1f), power);
            if (forms.Count == 0) { if (verbs.Count > 0) Apply(caster, map, verbs, FireMagic.Point(at, map, radius), at, power, r); return; }
            foreach (RuneDef f in forms)
            {
                switch (f.kind)
                {
                    case RuneKind.Aura: Apply(caster, map, verbs, FireMagic.Point(caster.Position, map, radius + 1.5f), caster.Position, power, r); break;
                    case RuneKind.Beam: Apply(caster, map, verbs, FireMagic.Beam(caster.Position, at, map, r.breadth), caster.Position, power, r); break;
                    case RuneKind.Shape:
                        var wall = FireMagic.WallTo.IsValid ? FireMagic.WallLine(at, FireMagic.WallTo, map, FireMagic.WallMax(r)) : FireMagic.Wall(caster.Position, at, map, 5 + 2 * r.breadth);
                        foreach (IntVec3 c in wall) HeldWater.Place(c, map, caster, power);
                        Apply(caster, map, verbs, wall, at, power, r);
                        break;
                    case RuneKind.Step:
                        var path = GenSight.BresenhamCellsBetween(caster.Position, at).Where(c => c.InBounds(map) && c != caster.Position).ToList();
                        // the wave carries the mage; whoever stands in the way is thrown aside
                        foreach (IntVec3 c in path) foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList()) if (p != caster && p.HostileTo(caster)) Push(p, c + (c - caster.Position).RotatedBy(Rot4.East), 2, map);
                        if (at.Standable(map)) { caster.Position = at; caster.Notify_Teleported(true, false); }
                        foreach (IntVec3 c in path) { FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1.2f); FireMagic.Extinguish(c, map); }
                        if (verbs.Count > 0) Apply(caster, map, verbs, path, at, power, r);
                        break;
                    case RuneKind.Restore: Wash(caster, at, map, Mathf.Max(0.9f, radius - 1f), power); break;
                    case RuneKind.Enchant: Enchant(caster, at, map, power); break;
                    case RuneKind.Summon: Spirit(caster, at, map, power, r.amplify); break;
                    case RuneKind.Sense: FireMagic.Sense(caster, map, 20f + 10f * r.reach, power); break;
                    default: if (verbs.Count > 0) Apply(caster, map, verbs, FireMagic.Point(at, map, radius), at, power, r); break;
                }
            }
        }

        private static IEnumerable<Pawn> PawnsIn(List<IntVec3> cells, Map map) =>
            cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Where(p => !p.Dead).Distinct().ToList();

        private static void Apply(Pawn caster, Map map, List<RuneDef> verbs, List<IntVec3> cells, IntVec3 from, float power, RitualReading r)
        {
            if (cells.Count == 0) return;
            foreach (RuneDef v in verbs)
            {
                switch (v.kind)
                {
                    case RuneKind.Release:
                        foreach (IntVec3 c in cells) FireMagic.Extinguish(c, map);
                        foreach (Pawn p in PawnsIn(cells, map))
                        {
                            if (p == caster || !p.HostileTo(caster)) continue;
                            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5f * power, 0f, -1f, caster));
                            Soak(p, power);
                            Push(p, from, 2 + r.amplify, map);
                        }
                        foreach (IntVec3 c in cells) if (Rand.Chance(0.5f)) FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1.6f);
                        break;
                    case RuneKind.Hold:
                        foreach (IntVec3 c in cells) HeldWater.Place(c, map, caster, power);
                        break;
                    case RuneKind.Drain:
                        float took = 0f;
                        foreach (Pawn p in PawnsIn(cells, map))
                        {
                            if (p == caster || !p.HostileTo(caster) || !p.RaceProps.IsFlesh) continue;
                            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Parched) ?? p.health.AddHediff(WaterDefOf.Avir_Parched);
                            h.Severity = Mathf.Min(1f, h.Severity + 0.35f * power);
                            took += 0.3f;
                            FleckMaker.ConnectingLine(p.DrawPos, caster.DrawPos, FleckDefOf.LineEMP, map);
                        }
                        foreach (IntVec3 c in cells) foreach (HeldWater w in c.GetThingList(map).OfType<HeldWater>().ToList()) { took += 0.1f; w.Destroy(); }
                        if (took > 0f)
                        {
                            Hediff k = caster.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_StoredWater) ?? caster.health.AddHediff(WaterDefOf.Avir_StoredWater);
                            k.Severity = Mathf.Min(1f, k.Severity + took);
                        }
                        break;
                    case RuneKind.Lift:
                        foreach (Pawn p in PawnsIn(cells, map))
                        {
                            if (p == caster || !p.HostileTo(caster)) continue;
                            p.stances?.stunner?.StunFor(Mathf.RoundToInt(120f * power), caster);
                            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 4f * power, 0f, -1f, caster));
                            Soak(p, power);
                        }
                        foreach (IntVec3 c in cells) FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 2.4f);
                        break;
                    case RuneKind.Growth:
                        var near = cells.SelectMany(c => GenAdj.CellsAdjacent8Way(new TargetInfo(c, map))).Concat(cells).Where(c => c.InBounds(map)).Distinct().ToList();
                        foreach (IntVec3 c in near)
                        {
                            Plant plant = c.GetPlant(map);
                            if (plant != null && plant.LifeStage == PlantLifeStage.Growing) plant.Growth = Mathf.Min(1f, plant.Growth + 0.1f * power);
                            if (c.GetThingList(map).Any(t => t is HeldWater)) foreach (IntVec3 n in GenAdj.CellsAdjacentCardinal(c, Rot4.North, IntVec2.One)) if (Rand.Chance(0.5f)) HeldWater.Place(n, map, caster, power * 0.7f);
                        }
                        break;
                    case RuneKind.Purge:
                        foreach (IntVec3 c in cells)
                        {
                            FireMagic.Extinguish(c, map);
                            foreach (Filth f in c.GetThingList(map).OfType<Filth>().ToList()) f.Destroy();
                        }
                        foreach (Pawn p in PawnsIn(cells, map))
                        {
                            if (p.HostileTo(caster)) continue;
                            foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => x.def == HediffDefOf.ToxicBuildup || x.def == HediffDefOf.FoodPoisoning).ToList())
                            { h.Severity -= 0.2f * power; if (h.Severity <= 0f) p.health.RemoveHediff(h); }
                        }
                        break;
                }
            }
            foreach (IntVec3 c in cells) if (Rand.Chance(0.2f)) FleckMaker.Static(c, map, FleckDefOf.WaterRipple, 1.2f);
        }

        /// <summary>Soaked: slower, and hard to set alight, for a while.</summary>
        public static void Soak(Pawn p, float power)
        {
            if (!p.RaceProps.IsFlesh) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Soaked) ?? p.health.AddHediff(WaterDefOf.Avir_Soaked);
            h.Severity = 1f;
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, Mathf.RoundToInt(2500f * power));
            p.GetAttachment(ThingDefOf.Fire)?.Destroy();
        }

        /// <summary>Thrown away from a point, as far as the ground lets them.</summary>
        public static void Push(Pawn p, IntVec3 from, int dist, Map map)
        {
            if (p.Dead || !p.Spawned) return;
            Vector3 d = (p.Position - from).ToVector3();
            if (d.sqrMagnitude < 0.01f) d = Rand.InsideUnitCircleVec3;
            d.Normalize();
            IntVec3 cur = p.Position;
            for (int i = 1; i <= dist; i++)
            {
                IntVec3 n = (p.Position.ToVector3Shifted() + d * i).ToIntVec3();
                if (!n.InBounds(map) || !n.Standable(map) || n.GetFirstPawn(map) != null) break;
                cur = n;
            }
            if (cur != p.Position) { p.Position = cur; p.Notify_Teleported(true, false); }
            p.stances?.stunner?.StunFor(60, null);
        }

        private static void Ward(Pawn caster, IntVec3 at, Map map, float radius, float power)
        {
            foreach (Pawn p in FireMagic.Point(at, map, radius).SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().ToList())
            {
                if (p.Dead || p.HostileTo(caster)) continue;
                Hediff w = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_WaterWard) ?? p.health.AddHediff(WaterDefOf.Avir_WaterWard);
                HediffComp_Disappears d = w.TryGetComp<HediffComp_Disappears>();
                if (d != null) d.ticksToDisappear = Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power);
                p.GetAttachment(ThingDefOf.Fire)?.Destroy();
                FleckMaker.Static(p.Position, map, FleckDefOf.WaterSplash, 1.5f);
            }
        }

        /// <summary>Wounds of friends washed and bound: bleeding stops; a cleaner tending than fire's.</summary>
        private static void Wash(Pawn caster, IntVec3 at, Map map, float radius, float power)
        {
            float q = Mathf.Clamp01(0.45f + 0.15f * power);
            foreach (Pawn p in FireMagic.Point(at, map, radius).SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().ToList())
            {
                if (p.Dead || p.HostileTo(caster)) continue;
                foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Bleeding || !i.IsTended()).Take(6).ToList())
                    inj.Tended(q, 1f);
                FleckMaker.Static(p.Position, map, FleckDefOf.WaterSplash, 1f);
            }
        }

        private static void Enchant(Pawn caster, IntVec3 at, Map map, float power)
        {
            Pawn p = at.GetFirstPawn(map) ?? caster;
            if (p.HostileTo(caster)) p = caster;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_WaterEnchant) ?? p.health.AddHediff(WaterDefOf.Avir_WaterEnchant);
            h.Severity = Mathf.Clamp(0.5f * power, 0.2f, 1f);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.RoundToInt(GenDate.TicksPerHour * 6f * power);
            FleckMaker.Static(p.Position, map, FleckDefOf.WaterSplash, 1.4f);
        }

        private static void Spirit(Pawn caster, IntVec3 at, Map map, float power, int amplify)
        {
            if (!at.Standable(map)) at = caster.Position;
            var w = (WaterSpirit)ThingMaker.MakeThing(WaterDefOf.Avir_WaterSpirit);
            w.owner = caster; w.power = power; w.left = Mathf.RoundToInt(1800f * power * (1 + amplify));
            GenSpawn.Spawn(w, at, map);
            FleckMaker.Static(at, map, FleckDefOf.WaterSplash, 2f);
        }

        /// <summary>The price of water: the mage is chilled to the bone - slower, and the cold bites harder.</summary>
        public static void Chilled(Pawn caster)
        {
            Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Chilled) ?? caster.health.AddHediff(WaterDefOf.Avir_Chilled);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.RoundToInt(GenDate.TicksPerHour * 4f);
            Messages.Message(caster.LabelShortCap + " lost the water - chilled to the bone.", caster, MessageTypeDefOf.NegativeEvent, false);
        }
    }

    /// <summary>Water put down by a mage and held there: foes in it are soaked and slowed, fire does not take.</summary>
    public class HeldWater : ThingWithComps
    {
        public Pawn owner;
        public float power = 1f;
        public int left = 2500;

        public static void Place(IntVec3 c, Map map, Pawn owner, float power)
        {
            if (!c.InBounds(map) || c.Impassable(map) || c.Filled(map)) return;
            HeldWater old = c.GetThingList(map).OfType<HeldWater>().FirstOrDefault();
            int life = Mathf.RoundToInt(2500f * power);
            if (old != null) { old.left = Mathf.Max(old.left, life); return; }
            var w = (HeldWater)ThingMaker.MakeThing(WaterDefOf.Avir_HeldWater);
            w.owner = owner; w.power = power; w.left = life;
            GenSpawn.Spawn(w, c, map);
            FireMagic.Extinguish(c, map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner");
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_Values.Look(ref left, "left", 2500);
        }

        protected override void Tick()
        {
            base.Tick();
            if (--left <= 0) { Destroy(); return; }
            if (!this.IsHashIntervalTick(60)) return;
            FireMagic.Extinguish(Position, Map);
            foreach (Pawn p in Position.GetThingList(Map).OfType<Pawn>().ToList())
                if (owner == null || p.HostileTo(owner)) WaterMagic.Soak(p, power * 0.5f);
            if (this.IsHashIntervalTick(240)) FleckMaker.Static(Position, Map, FleckDefOf.WaterRipple, 1f);
        }
    }

    /// <summary>A water spirit: it throws water at the mage's foes, knocking them back, and puts out fires near friends.</summary>
    public class WaterSpirit : ThingWithComps
    {
        public Pawn owner;
        public float power = 1f;
        public int left = 1800;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner");
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_Values.Look(ref left, "left", 1800);
        }

        protected override void Tick()
        {
            base.Tick();
            if (--left <= 0) { FleckMaker.Static(Position, Map, FleckDefOf.WaterSplash, 1.5f); Destroy(); return; }
            if (!this.IsHashIntervalTick(90)) return;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(Position, 4f, true)) if (c.InBounds(Map)) FireMagic.Extinguish(c, Map);
            Pawn foe = Map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && !p.Downed && owner != null && p.HostileTo(owner) && p.Position.DistanceTo(Position) <= 7f && GenSight.LineOfSight(Position, p.Position, Map))
                .OrderBy(p => p.Position.DistanceToSquared(Position)).FirstOrDefault();
            if (foe == null) return;
            foe.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 4f * power, 0f, -1f, owner));
            WaterMagic.Soak(foe, power);
            WaterMagic.Push(foe, Position, 1, Map);
            FleckMaker.ConnectingLine(DrawPos, foe.DrawPos, FleckDefOf.LineEMP, Map);
            FleckMaker.Static(foe.Position, Map, FleckDefOf.WaterSplash, 1.2f);
        }
    }

    /// <summary>Wards of the elements drawn over whoever wears them - a bubble in the element's colour; the dome art of
    /// the element (Things/Mote/Avir/Magic/Dome_Fire...) once it is drawn.</summary>
    public class MapComponent_WardsDrawn : MapComponent
    {
        public MapComponent_WardsDrawn(Map map) : base(map) { }

        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        private static Material Mat(RuneDef el)
        {
            string key = el?.defName ?? "none";
            if (mats.TryGetValue(key, out Material m)) return m;
            string own = el != null ? "Things/Mote/Avir/Magic/Dome_" + el.kind : null;
            Color col = el != null ? new Color(el.color.r, el.color.g, el.color.b, 0.35f) : new Color(0.9f, 0.85f, 1f, 0.35f);
            m = own != null && ContentFinder<Texture2D>.Get(own, false) != null ? MaterialPool.MatFrom(own, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, 0.6f))
                : MaterialPool.MatFrom(FlowTex.Dome ?? RitualTex.Ring, ShaderDatabase.Transparent, col);
            mats[key] = m;
            return m;
        }

        public override void MapComponentUpdate()
        {
            if (!AvirMagic.On) return;
            RuneDef fire = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Fire"), water = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Water");
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.health?.hediffSet == null) continue;
                RuneDef el = null;
                bool any = false;
                foreach (Hediff h in p.health.hediffSet.hediffs)
                {
                    if (h.def == FireDefOf.Avir_FireWard) { el = fire; any = true; break; }
                    if (h.def == WaterDefOf.Avir_WaterWard) { el = water; any = true; break; }
                    if (h.def == MagicSpellDefOf.Avir_ArcaneWard) { el = WardElement.Get(p); any = true; break; }
                }
                if (!any) continue;
                Vector3 at = p.DrawPos; at.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                float pulse = 1.6f + 0.06f * Mathf.Sin(Time.realtimeSinceStartup * 2.5f + p.thingIDNumber);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(pulse, 1f, pulse)), Mat(el), 0);
            }
        }
    }

    /// <summary>The weapon of water: melee blows knock the foe back a step.</summary>
    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), "ApplyMeleeDamageToTarget")]
    public static class Patch_WaterEnchantMelee
    {
        public static void Postfix(Verb_MeleeAttackDamage __instance, LocalTargetInfo target)
        {
            if (!AvirMagic.On || WaterDefOf.Avir_WaterEnchant == null) return;
            Pawn p = __instance.CasterPawn;
            if (p?.health?.hediffSet?.GetFirstHediffOfDef(WaterDefOf.Avir_WaterEnchant) == null) return;
            if (target.Thing is Pawn foe && !foe.Dead && Rand.Chance(0.6f)) { WaterMagic.Soak(foe, 0.6f); WaterMagic.Push(foe, p.Position, 1, foe.Map); }
        }
    }

    [DefOf]
    public static class WaterDefOf
    {
        public static HediffDef Avir_Soaked;
        public static HediffDef Avir_Parched;
        public static HediffDef Avir_StoredWater;
        public static HediffDef Avir_WaterWard;
        public static HediffDef Avir_WaterEnchant;
        public static HediffDef Avir_Chilled;
        public static ThingDef Avir_HeldWater;
        public static ThingDef Avir_WaterSpirit;
        static WaterDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(WaterDefOf)); }
    }
}
