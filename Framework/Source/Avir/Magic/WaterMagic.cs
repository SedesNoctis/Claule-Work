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
            r = SpellMods.Begin(s, r);
            // water is stronger where water already lies: pools it left, a river, a mire (up to +40%)
            if (caster.Map != null && target.Cell.InBounds(caster.Map)) power *= 1f + Reactions.WaterAround(target.Cell, caster.Map);
            Map map = caster.Map;
            Hediff kept = caster.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_StoredWater);
            bool drains = r.verbs.Any(v => v.kind == RuneKind.Drain);
            if (kept != null && !drains) { power *= 1f + kept.Severity; caster.health.RemoveHediff(kept); }
            IntVec3 at = target.Cell;
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            float radius = FireMagic.PointRadius(r);
            if (r.barrier > 0) Ward(caster, forms.Any(f => f.kind == RuneKind.Aura) ? caster.Position : at, map, Mathf.Max(0.9f, radius - 1f), power, r.amplify);
            MagicFx.WardMage(caster, power, WaterDefOf.Avir_WaterWard, "Avir_Rune_Water", r.amplify);
            if (forms.Count == 0) { if (verbs.Count > 0) Apply(caster, map, verbs, FireMagic.Point(at, map, radius), at, power, r); return; }
            foreach (RuneDef f in forms)
            {
                RitualReading rf = SpellMods.Verb(r, f); float fradius = FireMagic.PointRadius(rf);
                switch (f.kind)
                {
                    case RuneKind.Aura:
                        // the tide: a field of mist round the mage for a while; its verbs work at once, then again and again
                        Apply(caster, map, verbs, FireMagic.Point(caster.Position, map, fradius + 1.5f), caster.Position, power, rf);
                        MapComponent_Tides.Raise(caster, fradius + 1.5f, power, verbs);
                        break;
                    case RuneKind.Beam:
                        var line = FireMagic.Beam(caster.Position, at, map, rf.breadth);
                        Apply(caster, map, verbs, line, caster.Position, power, rf);
                        break;
                    case RuneKind.Shape:
                        // the ground itself turns to shallow water for a while: a moat that slows everyone and takes no fire
                        var wall = FireMagic.ShapeCells(caster, at, map, rf, forms);
                        int life = Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power * (1 + rf.anchor + (verbs.Any(v => v.kind == RuneKind.Hold) ? 1 : 0)));
                        foreach (IntVec3 c in wall) { map.GetComponent<MapComponent_RaisedWater>()?.Raise(c, life); FireMagic.Extinguish(c, map); FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1.4f); }
                        Apply(caster, map, verbs.Where(v => v.kind != RuneKind.Hold).ToList(), wall, at, power, rf);
                        break;
                    case RuneKind.Step:
                        if (rf.thread > 0) { SpellDash.Pull(caster, target, RuneKind.Water, power, fradius); break; }
                        // a wave rolls ahead of the mage, as wide as its breadth: whoever stands in it is struck and carried on
                        var path = FireMagic.Beam(caster.Position, at, map, rf.breadth).Where(c => c != caster.Position).ToList();
                        IntVec3 back = caster.Position;
                        foreach (Pawn p in PawnsIn(path, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, (8f + 3f * rf.breadth) * power, 0f, -1f, caster));
                            Soak(p, power);
                            Push(p, back, 2 + rf.breadth, map);
                        }
                        foreach (IntVec3 c in path) { FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1.6f); FireMagic.Extinguish(c, map); if (Rand.Chance(0.3f)) Puddle(c, map); }
                        SpellDash.Carry(caster, at, RuneKind.Water);
                        if (verbs.Count > 0) Apply(caster, map, verbs, path, at, power, rf);
                        break;
                    case RuneKind.Restore: Wash(caster, at, map, Mathf.Max(0.9f, fradius - 1f), power); break;
                    case RuneKind.Enchant: Enchant(caster, at, map, power); break;
                    case RuneKind.Summon: Messages.Message("Spirits are not made yet.", MessageTypeDefOf.RejectInput, false); break;
                    case RuneKind.Sense: FireMagic.Sense(caster, map, 25f + 10f * rf.reach, power); break;
                    default: if (verbs.Count > 0) Apply(caster, map, verbs, FireMagic.Point(at, map, fradius), at, power, rf); break;
                }
            }
        }

        private static IEnumerable<Pawn> PawnsIn(List<IntVec3> cells, Map map) =>
            cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Where(p => !p.Dead).Distinct().ToList();

        internal static void Apply(Pawn caster, Map map, List<RuneDef> verbs, List<IntVec3> cells, IntVec3 from, float power, RitualReading r)
        {
            if (cells.Count == 0) return;
            if (verbs.Count > 0) Reactions.Touch(Brings.Water, cells, map, caster, power);
            foreach (RuneDef v in verbs)
            {
                RitualReading rv = SpellMods.Verb(r, v);
                switch (v.kind)
                {
                    case RuneKind.Release:
                        foreach (IntVec3 c in cells) FireMagic.Extinguish(c, map);
                        foreach (Pawn p in PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, (12f + 3f * rv.amplify) * power, 0f, -1f, caster));
                            Soak(p, power);
                            FleckMaker.Static(p.Position, map, FleckDefOf.WaterSplash, 2.2f);
                            SpellHurl.Fell(p, from, 3 + rv.amplify, map, caster, power, Mathf.RoundToInt(60f * power));
                        }
                        foreach (IntVec3 c in cells) { if (Rand.Chance(0.5f)) FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1.6f); if (c == from || Rand.Chance(0.45f)) Puddle(c, map); }
                        // and every pool lying round about answers: a jet bursts out of it under whoever stands there
                        foreach (IntVec3 c in FireMagic.Point(from, map, FireMagic.PointRadius(rv) + 3f).Where(x => !cells.Contains(x) && Reactions.Pools(x, map).Any(t => !(t is MirePatch))).ToList())
                        {
                            FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 2.4f);
                            MapComponent_SpellSprites.Put(map, "Water/Geyser", c.ToVector3Shifted() + new Vector3(0f, 0f, 0.4f), 2.2f, 70, 0f);
                            foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList()) if (MagicFx.Foe(p, caster)) { Soak(p, power); SpellHurl.Lift(p, caster, power * 0.6f); }
                        }
                        break;
                    case RuneKind.Hold:
                        foreach (IntVec3 c in cells) { HeldWater.Place(c, map, caster, power); if (Rand.Chance(0.5f)) Puddle(c, map); }
                        break;
                    case RuneKind.Drain:
                        float took = 0f;
                        foreach (Pawn p in PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster) || !p.RaceProps.IsFlesh) continue;
                            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Parched) ?? p.health.AddHediff(WaterDefOf.Avir_Parched);
                            h.Severity = Mathf.Min(1f, h.Severity + 0.35f * power * SpellBody.Of(p).flesh);   // only living flesh dries
                            took += 0.3f;
                            FleckMaker.ConnectingLine(p.DrawPos, caster.DrawPos, FleckDefOf.LineEMP, map);
                        }
                        foreach (IntVec3 c in cells) foreach (HeldWater w in c.GetThingList(map).OfType<HeldWater>().ToList()) { took += 0.1f; w.Destroy(); }
                        power = MagicFx.Drained(caster, verbs, v, WaterDefOf.Avir_StoredWater, took, power);
                        break;
                    case RuneKind.Lift:
                        foreach (Pawn p in PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            Soak(p, power);
                            if (rv.thread > 0) SpellHurl.Press(p, caster, power, false); else SpellHurl.Lift(p, caster, power);
                        }
                        foreach (IntVec3 c in cells) FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 2.4f);
                        // the geyser itself, a column bursting from under them
                        foreach (IntVec3 c in cells.Where((x, i) => i % 4 == 0)) MapComponent_SpellSprites.Put(map, "Water/Geyser", c.ToVector3Shifted() + new Vector3(0f, 0f, 0.5f), 2.8f, 80, 0f);
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
                            // water washes out anaesthetic and poisons (gas the body took in is air's to breathe out)
                            Cleansing.WashOut(p, 0.2f * power);
                        }
                        break;
                }
            }
            foreach (IntVec3 c in cells) if (Rand.Chance(0.2f)) FleckMaker.Static(c, map, FleckDefOf.WaterRipple, 1.2f);
        }

        /// <summary>Water left on the ground: the game's own puddle.</summary>
        public static void Puddle(IntVec3 c, Map map)
        {
            // a pool, not a drop: wide and deep enough to be seen
            ThingDef pool = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpellPuddle") ?? ThingDefOf.Filth_Water;
            if (pool == null || !c.InBounds(map) || !c.Standable(map) || c.GetTerrain(map).IsWater) return;
            // the game's own filth check turns water away on some ground; spell water lies wherever it falls
            if (!FilthMaker.TryMakeFilth(c, map, pool, 3) && !c.GetThingList(map).Any(t => t.def == pool)) GenSpawn.Spawn(pool, c, map);
        }

        /// <summary>Water flows on: from where the jet ends to the next foe within four cells, and on again.</summary>
        private static void Chain(Pawn caster, IntVec3 from, Map map, int jumps, float power)
        {
            var hit = new HashSet<Pawn>();
            Vector3 prev = from.ToVector3Shifted();
            IntVec3 cur = from;
            for (int i = 0; i < jumps; i++)
            {
                Pawn next = GenRadial.RadialDistinctThingsAround(cur, map, 4.5f, true).OfType<Pawn>()
                    .Where(p => MagicFx.Foe(p, caster) && !hit.Contains(p) && GenSight.LineOfSight(cur, p.Position, map))
                    .OrderBy(p => p.Position.DistanceToSquared(cur)).FirstOrDefault();
                if (next == null) break;
                hit.Add(next);
                FleckMaker.ConnectingLine(prev, next.DrawPos, FleckDefOf.LineEMP, map);
                next.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 4f * power, 0f, -1f, caster));
                Soak(next, power);
                FleckMaker.Static(next.Position, map, FleckDefOf.WaterSplash, 1.4f);
                prev = next.DrawPos; cur = next.Position;
            }
        }

        /// <summary>The tide's verbs, again, round the mage.</summary>
        public static void ApplyAround(Pawn mage, List<RuneDef> verbs, float radius, float power)
        {
            Map map = mage.Map;
            Apply(mage, map, verbs, FireMagic.Point(mage.Position, map, radius), mage.Position, power * 0.5f, new RitualReading());
        }

        /// <summary>Soaked: slower, and hard to set alight, for a while.</summary>
        public static void Soak(Pawn p, float power)
        {
            if (p == null || p.Dead || p.health?.hediffSet == null || WaterDefOf.Avir_Soaked == null || !p.RaceProps.IsFlesh || SpellWards.Stops(p, true)) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Soaked);
            if (h == null)
            {
                try { h = p.health.AddHediff(WaterDefOf.Avir_Soaked); }
                catch (System.Exception e) { Log.WarningOnce("[Avir] soaking " + p + " failed: " + e.Message, 774411); return; }
            }
            if (h == null) return;
            h.Severity = 1f;
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            // sealed gear sheds the water; open gear drinks it and weighs the more the heavier it is
            var sb = SpellBody.Of(p);
            float soak = sb.sealed_ ? 0.4f : SpellBody.Heavy(p);
            if (d != null) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, Mathf.RoundToInt(2500f * power * soak));
            p.GetAttachment(ThingDefOf.Fire)?.Destroy();
        }

        /// <summary>Thrown away from a point, as far as the ground lets them.</summary>
        public static void Push(Pawn p, IntVec3 from, int dist, Map map) => SpellHurl.Throw(p, from, dist, map);

        private static void Ward(Pawn caster, IntVec3 at, Map map, float radius, float power, int amplify)
        {
            MapComponent_WardsDrawn.RaiseWard(map, at, radius, DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Water"), ComboKind.None, caster, Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power), power, amplify);
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
            SpellSlip.Water(caster);
            Messages.Message(caster.LabelShortCap + " lost the water - a cold wave broke over everyone round.", caster, MessageTypeDefOf.NegativeEvent, false);
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
                if (owner == null || MagicFx.Foe(p, owner)) WaterMagic.Soak(p, power * 0.5f);
            if (this.IsHashIntervalTick(240)) FleckMaker.Static(Position, Map, FleckDefOf.WaterRipple, 1f);
        }
    }

    /// <summary>A water spirit: it throws water at the mage's foes, knocking them back, and puts out fires near friends.</summary>
    public class WaterSpirit : ThingWithComps
    {
        private static readonly HashSet<WaterSpirit> all = new HashSet<WaterSpirit>();
        public static WaterSpirit Guarding(Pawn p) => all.FirstOrDefault(s => s.Spawned && s.owner == p && s.Position.DistanceTo(p.Position) <= 3f);
        public override void SpawnSetup(Map map, bool respawningAfterLoad) { base.SpawnSetup(map, respawningAfterLoad); all.Add(this); }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish) { all.Remove(this); base.DeSpawn(mode); }
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
            // it keeps by its mage
            if (this.IsHashIntervalTick(30) && owner != null && owner.Spawned && owner.Map == Map && Position.DistanceTo(owner.Position) > 2f)
            {
                IntVec3 near = GenAdj.CellsAdjacent8Way(owner).Where(c => c.InBounds(Map) && c.Standable(Map)).RandomElementWithFallback(owner.Position);
                Position = near;
            }
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

        /// <summary>A dome a ward spell raised over its ground: as wide as the spell reached, shimmering while it lasts. A
        /// zone dome is a ward over ground (Магия-правила.md, part 1): it has a charge and the ward's rules for whoever
        /// stands inside, and for any spell that lands inside it. A dome that is only the picture of something else
        /// (zone = false) has none.</summary>
        public class Dome : IExposable, IWard
        {
            public IntVec3 at; public float radius; public RuneDef el; public int until; public Faction owner; public int seed; public Color? tint; public string tex;
            public bool zone; public float energy, energyMax = 1f, ranged = 0.25f, melee = 0.15f; public ComboKind combo;
            public MapComponent_WardsDrawn of;

            public float Energy { get => energy; set => energy = value; }
            public float EnergyMax => energyMax;
            public float Ranged => ranged;
            public float Melee => melee;
            public RuneKind[] Parts => combo != ComboKind.None ? Combos.Parts(combo) : el != null ? new[] { el.kind } : new RuneKind[0];
            public bool Fiery => combo == ComboKind.Magma || combo == ComboKind.Steam || (combo == ComboKind.None && el != null && el.kind == RuneKind.Fire);
            public string Name => (combo != ComboKind.None ? Combos.Label(combo) : el?.label ?? "magic") + " dome";
            public void Spend(float amount) { energy -= amount; if (energy <= 0.01f) Fall("Dome broken"); }
            public void Fall(string why)
            {
                SpellWards.Note(Name + " at " + at + " fell: " + why);
                of?.domes.Remove(this);
                Map m = of?.map;
                if (m != null) { FleckMaker.ThrowLightningGlow(at.ToVector3Shifted(), m, 1.6f); MoteMaker.ThrowText(at.ToVector3Shifted(), m, why, 3f); }
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref at, "at"); Scribe_Values.Look(ref radius, "radius"); Scribe_Defs.Look(ref el, "el"); Scribe_Values.Look(ref until, "until");
                Scribe_References.Look(ref owner, "owner"); Scribe_Values.Look(ref seed, "seed"); Scribe_Values.Look(ref tex, "tex");
                Scribe_Values.Look(ref zone, "zone"); Scribe_Values.Look(ref energy, "energy"); Scribe_Values.Look(ref energyMax, "energyMax", 1f);
                Scribe_Values.Look(ref ranged, "ranged", 0.25f); Scribe_Values.Look(ref melee, "melee", 0.15f); Scribe_Values.Look(ref combo, "combo", ComboKind.None);
                bool hasTint = tint.HasValue; Color tc = tint ?? Color.white;
                Scribe_Values.Look(ref hasTint, "hasTint"); Scribe_Values.Look(ref tc, "tint");
                if (Scribe.mode == LoadSaveMode.LoadingVars) tint = hasTint ? tc : (Color?)null;
            }
        }
        public List<Dome> domes = new List<Dome>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref domes, "domes", LookMode.Deep);
            if (domes == null) domes = new List<Dome>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) foreach (Dome d in domes) d.of = this;
        }

        /// <summary>A zone ward raised over ground: its charge and its hold on plain blows grow with the spell, as a ward's.</summary>
        public static Dome RaiseWard(Map map, IntVec3 at, float radius, RuneDef el, ComboKind combo, Pawn caster, int ticks, float power, int amplify)
        {
            var c = map?.GetComponent<MapComponent_WardsDrawn>();
            if (c == null) return null;
            float s = Mathf.Max(0.3f, power) * (1f + 0.5f * amplify);
            var d = new Dome
            {
                at = at, radius = radius + 0.5f, el = el, combo = combo, until = Find.TickManager.TicksGame + ticks, owner = caster?.Faction, seed = Rand.Int,
                tint = combo != ComboKind.None ? Combos.ColorOf(combo) : (Color?)null, tex = combo != ComboKind.None ? "Dome_" + combo : null,
                zone = true, energyMax = 45f * s, ranged = Mathf.Min(0.75f, 0.25f * s), melee = Mathf.Min(0.5f, 0.15f * s), of = c
            };
            d.energy = d.energyMax;
            // a dome raised where one already stands: of the same nature it thickens it (charge adds up, it lasts the longer);
            // of another, the new one takes its place
            int now = Find.TickManager.TicksGame;
            Dome old = c.domes.LastOrDefault(o => o.zone && o.until > now && o.at.DistanceTo(at) <= Mathf.Max(1.5f, Mathf.Min(o.radius, d.radius) * 0.5f));
            if (old != null)
            {
                if (old.combo == combo && old.el == el)
                {
                    old.energyMax += d.energyMax; old.energy += d.energy; old.until = Mathf.Max(old.until, d.until);
                    old.radius = Mathf.Max(old.radius, d.radius);
                    old.ranged = Mathf.Max(old.ranged, d.ranged); old.melee = Mathf.Max(old.melee, d.melee);
                    SpellWards.Note("dome of " + old.Name + " at " + at + " thickened: charge " + old.energy.ToString("0") + "/" + old.energyMax.ToString("0"));
                    if (map != null) MoteMaker.ThrowText(at.ToVector3Shifted(), map, "Dome thickened", 3f);
                    return old;
                }
                c.domes.Remove(old);
                SpellWards.Note("dome of " + old.Name + " at " + at + " gives way to " + d.Name);
            }
            c.domes.Add(d);
            SpellWards.Note("dome of " + d.Name + " raised at " + at + " (radius " + d.radius.ToString("0.0") + ", charge " + d.energyMax.ToString("0") + ")");
            return d;
        }

        /// <summary>The zone dome over a cell, if any (the newest).</summary>
        public static Dome Over(Map map, IntVec3 c)
        {
            var m = map?.GetComponent<MapComponent_WardsDrawn>();
            if (m == null) return null;
            int now = Find.TickManager.TicksGame;
            for (int i = m.domes.Count - 1; i >= 0; i--)
            {
                Dome d = m.domes[i];
                if (d.zone && d.until > now && (c - d.at).LengthHorizontal <= d.radius) return d;
            }
            return null;
        }
        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        /// <param name="tex">A dome of its own (a meeting's: the element domes recoloured), under Things/Mote/Avir/Magic/.</param>
        public static void Raise(Map map, IntVec3 at, float radius, RuneDef el, Pawn caster, int ticks, Color? tint = null, string tex = null)
        {
            var c = map?.GetComponent<MapComponent_WardsDrawn>();
            if (c == null) return;
            c.domes.Add(new Dome { at = at, radius = radius + 0.5f, el = el, until = Find.TickManager.TicksGame + ticks, owner = caster?.Faction, seed = Rand.Int, tint = tint, tex = tex, of = c });
        }

        /// <summary>A foe's spell flying into a dome meets it by the ward's rules (SpellWards.Meets): put out at the cost
        /// of charge, or through it. A dome that is only a picture stops nothing.</summary>
        public bool Quench(IntVec3 c, Pawn caster, RuneKind[] inc, float power)
        {
            int now = Find.TickManager.TicksGame;
            Dome d = domes.LastOrDefault(x => x.zone && x.until > now && caster != null && x.owner != null && caster.Faction != null && x.owner.HostileTo(caster.Faction) && (c - x.at).LengthHorizontal <= x.radius);
            if (d == null) return false;
            return SpellWards.Meets(d, inc, power, map, c, out _);
        }

        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        private static Material glowMat;
        private static Material GlowMat => glowMat ?? (glowMat = MaterialPool.MatFrom(Avir.Linarian.SongArt.Glow, ShaderDatabase.MoteGlow, Color.white));

        /// <summary>A dome of two elements met wears the colour of what they made.</summary>
        private static Material Mat(RuneDef el, Color? tint, string tex)
        {
            if (tex != null)
            {
                if (mats.TryGetValue(tex, out Material om)) return om;
                string path = "Things/Mote/Avir/Magic/" + tex;
                if (ContentFinder<Texture2D>.Get(path, false) != null) { om = MaterialPool.MatFrom(path, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, 0.75f)); mats[tex] = om; return om; }
            }
            if (tint == null) return Mat(el);
            Color t = tint.Value;
            string key = "tint" + t.r.ToString("0.00") + t.g.ToString("0.00") + t.b.ToString("0.00");
            if (mats.TryGetValue(key, out Material tm)) return tm;
            tm = MaterialPool.MatFrom(FlowTex.Dome ?? RitualTex.Ring, ShaderDatabase.Transparent, new Color(t.r, t.g, t.b, 0.35f));
            mats[key] = tm;
            return tm;
        }

        private static Material Mat(RuneDef el)
        {
            string key = el?.defName ?? "none";
            if (mats.TryGetValue(key, out Material m)) return m;
            string own = el != null ? "Things/Mote/Avir/Magic/Dome_" + el.kind : null;
            Color col = el != null ? new Color(el.color.r, el.color.g, el.color.b, 0.35f) : new Color(0.9f, 0.85f, 1f, 0.35f);
            m = own != null && ContentFinder<Texture2D>.Get(own, false) != null ? MaterialPool.MatFrom(own, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, 0.75f))
                : MaterialPool.MatFrom(FlowTex.Dome ?? RitualTex.Ring, ShaderDatabase.Transparent, col);
            mats[key] = m;
            return m;
        }

        public override void MapComponentUpdate()
        {
            if (!AvirMagic.On) return;
            // the domes: their size the spell's reach, a slow turn and breath, beneath whoever stands in them
            int now = Find.TickManager.TicksGame;
            domes.RemoveAll(x => x.until <= now);
            foreach (Dome d in domes)
            {
                float t = Time.realtimeSinceStartup;
                float breath = 1f + 0.02f * Mathf.Sin(t * 1.7f + d.seed);
                float a = 0.7f;   // steady: no flicker; it lies under the pawns, never over them
                if (d.until - now < 300) a *= (d.until - now) / 300f;   // fading in its last seconds
                Vector3 pos = d.at.ToVector3Shifted(); pos.y = AltitudeLayer.ItemImportant.AltitudeFor();
                float s = d.radius * 2f * breath;
                Material m = Mat(d.el, d.tint, d.tex);
                Color keep = m.color;
                block.SetColor(ShaderPropertyIDs.Color, new Color(keep.r, keep.g, keep.b, Mathf.Clamp01(a)));
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.AngleAxis((t * 14f + d.seed) % 360f, Vector3.up), new Vector3(s, 1f, s)), m, 0, null, 0, block);
            }
            RuneDef fire = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Fire"), water = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Water");
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.health?.hediffSet == null) continue;
                // a mage not of the colony shows a soft glow beneath: gold - a sorcerer, blue - a mage
                if (p.Faction != Faction.OfPlayer && Mages.IsMage(p) && !p.Dead)
                {
                    Hediff_ArcaneCore core = Mages.Core(p);
                    bool sorc = core != null && core.Sorcerer;
                    Color gc = sorc ? Hediff_ArcaneCore.SorcererColor : Hediff_ArcaneCore.MageColor;
                    Vector3 gp = p.DrawPos; gp.y = AltitudeLayer.Pawn.AltitudeFor() - 0.01f;
                    block.SetColor(ShaderPropertyIDs.Color, new Color(gc.r, gc.g, gc.b, 0.28f + 0.1f * Mathf.Sin(Time.realtimeSinceStartup * 2f + p.thingIDNumber)));
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(gp, Quaternion.identity, new Vector3(1.5f, 1f, 1.5f)), GlowMat, 0, null, 0, block);
                }
                RuneDef el = null;
                bool any = false;
                Hediff_SpellWard sw = SpellWards.On(p);
                if (sw != null && sw.Combo != ComboKind.None)
                {
                    // a ward of a meeting: its own dome, drawn round the wearer
                    Vector3 wat = p.DrawPos; wat.y = AltitudeLayer.ItemImportant.AltitudeFor();
                    float wp = 1.6f + 0.06f * Mathf.Sin(Time.realtimeSinceStartup * 2.5f + p.thingIDNumber);
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(wat, Quaternion.AngleAxis((Time.realtimeSinceStartup * -20f + p.thingIDNumber * 37f) % 360f, Vector3.up), new Vector3(wp, 1f, wp)), Mat(null, Combos.ColorOf(sw.Combo), "Dome_" + sw.Combo), 0);
                    continue;
                }
                if (sw != null && sw.Parts.Length == 1 && (sw.Parts[0] == RuneKind.Earth || sw.Parts[0] == RuneKind.Air)) { el = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + sw.Parts[0]); any = true; }
                foreach (Hediff h in p.health.hediffSet.hediffs)
                {
                    if (any) break;
                    if (h.def == FireDefOf.Avir_FireWard) { el = fire; any = true; break; }
                    if (h.def == WaterDefOf.Avir_WaterWard)
                {
                    el = water; any = true;
                    if (Find.TickManager.TicksGame % 60 == 0 && !Find.TickManager.Paused)
                        foreach (IntVec3 c in GenRadial.RadialCellsAround(p.Position, 3f, true)) if (c.InBounds(map)) FireMagic.Extinguish(c, map);
                    break;
                }
                    if (h.def == MagicSpellDefOf.Avir_ArcaneWard) { el = WardElement.Get(p); any = true; break; }
                }
                if (!any) continue;
                Vector3 at = p.DrawPos; at.y = AltitudeLayer.ItemImportant.AltitudeFor();
                float pulse = 1.6f + 0.06f * Mathf.Sin(Time.realtimeSinceStartup * 2.5f + p.thingIDNumber);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.AngleAxis((Time.realtimeSinceStartup * -20f + p.thingIDNumber * 37f) % 360f, Vector3.up), new Vector3(pulse, 1f, pulse)), Mat(el), 0);
            }
        }
    }

    /// <summary>The weapon of water: blows soak the foe; each blow on a soaked one parches them further.</summary>
    // on the blow itself, whatever weapon or mod makes it: any melee hit that lands
    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class Patch_WaterEnchantMelee
    {
        public static void Postfix(Verb_MeleeAttack __instance, bool __result)
        {
            if (!__result) return;
            LocalTargetInfo target = __instance.CurrentTarget;
            if (!AvirMagic.On || WaterDefOf.Avir_WaterEnchant == null) return;
            Pawn p = __instance.CasterPawn;
            if (p?.health?.hediffSet?.GetFirstHediffOfDef(WaterDefOf.Avir_WaterEnchant) == null) return;
            if (!(target.Thing is Pawn foe) || foe.Dead || !foe.RaceProps.IsFlesh) return;
            // a soaked foe is parched further with every blow: the weakness gathers
            if (foe.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Soaked) != null)
            {
                Hediff h = foe.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Parched) ?? foe.health.AddHediff(WaterDefOf.Avir_Parched);
                h.Severity = Mathf.Min(1f, h.Severity + 0.12f);
            }
            WaterMagic.Soak(foe, 0.6f);
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
        public static HediffDef Avir_InMist;
        static WaterDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(WaterDefOf)); }
    }
}
