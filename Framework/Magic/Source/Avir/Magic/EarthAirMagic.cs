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
    // EARTH: weight, stone, the ground itself. It strikes blunt and stuns, roots, raises stone, and moves the mage
    // under the ground to any place of the map. Its price: the stone takes hold of the mage.
    // AIR: wind and breath. It pushes, takes the breath away, feeds or snuffs fire, carries the mage over walls, and its
    // jet is lightning that jumps from foe to foe. Its price: the breath is stolen.
    // =====================================================================================
    public static class EarthMagic
    {
        public static bool Is(RuneDef el) => el != null && el.kind == RuneKind.Earth;

        public static void Cast(Pawn caster, RitualSchema s, RitualReading r, LocalTargetInfo target, float power)
        {
            r = SpellMods.Begin(s, r);
            Map map = caster.Map;
            Hediff kept = caster.health.hediffSet.GetFirstHediffOfDef(EarthAirDefOf.Avir_StoredStone);
            if (kept != null && !r.verbs.Any(v => v.kind == RuneKind.Drain)) { power *= 1f + kept.Severity; caster.health.RemoveHediff(kept); }
            IntVec3 at = target.Cell;
            // stone remains: what lies at hand round the target is taken up and flung
            power *= EarthStone.Gather(map, at, ComboKind.None);
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            float radius = FireMagic.PointRadius(r);
            if (r.barrier > 0) MagicFx.Ward(caster, forms.Any(f => f.kind == RuneKind.Aura) ? caster.Position : at, map, Mathf.Max(0.9f, radius - 1f), power, EarthAirDefOf.Avir_EarthWard, "Avir_Rune_Earth", r.amplify);
            MagicFx.WardMage(caster, power, EarthAirDefOf.Avir_EarthWard, "Avir_Rune_Earth", r.amplify);
            if (forms.Count == 0) { if (verbs.Count > 0) Apply(caster, map, verbs, FireMagic.Point(at, map, radius), at, power, r); return; }
            foreach (RuneDef f in forms)
            {
                RitualReading rf = SpellMods.Verb(r, f); float fradius = FireMagic.PointRadius(rf);
                switch (f.kind)
                {
                    case RuneKind.Aura:
                        Apply(caster, map, verbs, FireMagic.Point(caster.Position, map, fradius + 1.5f), caster.Position, power, rf);
                        MapComponent_Tides.Raise(caster, fradius + 1.5f, power, verbs, RuneKind.Earth);
                        break;
                    case RuneKind.Beam:
                        // a fissure runs along the line: the ground cracks under whoever stands on it
                        var line = FireMagic.Beam(caster.Position, at, map, rf.breadth);
                        foreach (IntVec3 c in line) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.4f, MagicFx.EarthDust);
                        foreach (Pawn p in MagicFx.PawnsIn(line, map)) if (MagicFx.Foe(p, caster)) MagicFx.Fell(p, p.Position, 0, map, caster, power, Mathf.RoundToInt(80f * power));
                        Apply(caster, map, verbs, line, caster.Position, power, rf);
                        break;
                    case RuneKind.Shape:
                        // turned round by the Thread: the stone does not stand - a ring rises round the target and closes on it
                        if (rf.thread > 0)
                        {
                            float sr = MapComponent_Squeeze.StartRadius(rf.breadth);
                            MapComponent_Squeeze.Begin(caster, map, at, sr, power, false, rf.anchor + (verbs.Any(v => v.kind == RuneKind.Hold) ? 1 : 0));
                            Apply(caster, map, verbs.Where(v => v.kind != RuneKind.Hold).ToList(), FireMagic.Ring(at, map, sr), at, power, rf);
                            break;
                        }
                        // a wall of stone rises between the two chosen points; with holding or an anchor it stands longer
                        var wall = FireMagic.ShapeCells(caster, at, map, rf, forms);
                        int life = Mathf.RoundToInt(GenDate.TicksPerHour * 3f * power * (1 + rf.anchor + (verbs.Any(v => v.kind == RuneKind.Hold) ? 1 : 0)));
                        foreach (IntVec3 c in wall) EarthWall.Raise(c, map, life, caster.Faction);
                        Apply(caster, map, verbs.Where(v => v.kind != RuneKind.Hold).ToList(), wall, at, power, rf);
                        break;
                    case RuneKind.Step:
                        // the mage sinks into the ground and comes up wherever they chose - the whole map is in reach
                        if (rf.thread > 0) SpellDash.Pull(caster, target, RuneKind.Earth, power, fradius); else SpellDash.Carry(caster, at, RuneKind.Earth, power, verbs, rf);
                        break;
                    case RuneKind.Restore:
                        foreach (Pawn p in MagicFx.PawnsIn(FireMagic.Point(at, map, Mathf.Max(0.9f, fradius - 1f)), map))
                        {
                            if (p.HostileTo(caster)) continue;
                            foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => !i.IsTended()).Take(6).ToList()) inj.Tended(Mathf.Clamp01(0.4f + 0.15f * power), 1f);
                            MagicFx.Give(p, EarthAirDefOf.Avir_StoneSkin, Mathf.RoundToInt(GenDate.TicksPerHour * power));
                            FleckMaker.ThrowDustPuffThick(p.DrawPos, map, 1.2f, MagicFx.EarthDust);
                        }
                        break;
                    case RuneKind.Enchant: MagicFx.Enchant(caster, at, map, power, EarthAirDefOf.Avir_EarthEnchant); break;
                    case RuneKind.Summon: Messages.Message("Spirits are not made yet.", MessageTypeDefOf.RejectInput, false); break;
                    case RuneKind.Sense: FireMagic.Sense(caster, map, 35f + 10f * rf.reach, power); break;
                    default: if (verbs.Count > 0) Apply(caster, map, verbs, FireMagic.Point(at, map, fradius), at, power, rf); break;
                }
            }
        }

        internal static void Apply(Pawn caster, Map map, List<RuneDef> verbs, List<IntVec3> cells, IntVec3 from, float power, RitualReading r)
        {
            if (cells.Count == 0) return;
            foreach (RuneDef v in verbs)
            {
                RitualReading rv = SpellMods.Verb(r, v);
                switch (v.kind)
                {
                    case RuneKind.Release:
                        // a tremor: blunt blows, the ground knocks them off their feet
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, (10f + 3f * rv.amplify) * power, 0f, -1f, caster));
                            if (!p.Dead) MagicFx.Fell(p, from, 1, map, caster, power, Mathf.RoundToInt(90f * power));
                        }
                        foreach (IntVec3 c in cells) { if (Rand.Chance(0.5f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.8f, MagicFx.EarthDust); if (Rand.Chance(0.25f)) MagicFx.Rubble(c, map); }
                        break;
                    case RuneKind.Hold:
                        // the stone grips their feet
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            MagicFx.Give(p, EarthAirDefOf.Avir_Rooted, Mathf.RoundToInt(480f * power * (1 + rv.anchor)));
                            MagicFx.Give(p, EarthAirDefOf.Avir_Heavy, Mathf.RoundToInt(GenDate.TicksPerHour * power));
                            // with an anchor the ground does not only grip - it draws the body down into itself, by its
                            // weight: knee-deep, waist-deep; not to death, but held there, hands half-buried too
                            if (rv.anchor > 0) MagicFx.Bury(p, power * (1 + 0.5f * rv.anchor));
                        }
                        foreach (IntVec3 c in cells) if (Rand.Chance(0.3f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.2f, MagicFx.EarthDust);
                        break;
                    case RuneKind.Drain:
                        // weight drawn out of the ground into their limbs; the mage keeps some of it
                        float took = 0f;
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            MagicFx.Give(p, EarthAirDefOf.Avir_Heavy, Mathf.RoundToInt(GenDate.TicksPerHour * 2f * power));
                            took += 0.3f;
                        }
                        power = MagicFx.Drained(caster, verbs, v, EarthAirDefOf.Avir_StoredStone, took, power);
                        break;
                    case RuneKind.Lift:
                        // stone spikes burst up under them
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            p.TakeDamage(new DamageInfo(DamageDefOf.Stab, (8f + 2f * rv.amplify) * power, 0.2f, -1f, caster));
                            if (!p.Dead) { if (rv.thread > 0) SpellHurl.Press(p, caster, power, true); else MagicFx.Lift(p, caster, power * 0.7f); }
                        }
                        foreach (IntVec3 c in cells) { if (Rand.Chance(0.4f)) MagicFx.Rubble(c, map); FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1f, MagicFx.EarthDust); }
                        break;
                    case RuneKind.Growth:
                        // the soil grows rich: plants drink it in
                        foreach (IntVec3 c in cells.SelectMany(c => GenAdj.CellsAdjacent8Way(new TargetInfo(c, map))).Concat(cells).Where(c => c.InBounds(map)).Distinct())
                        {
                            Plant plant = c.GetPlant(map);
                            if (plant != null && plant.LifeStage == PlantLifeStage.Growing) plant.Growth = Mathf.Min(1f, plant.Growth + 0.12f * power);
                        }
                        break;
                    case RuneKind.Purge:
                        // earth smothers: fires buried, filth and rubble swallowed
                        foreach (IntVec3 c in cells)
                        {
                            FireMagic.Extinguish(c, map);
                            foreach (Filth f in c.GetThingList(map).OfType<Filth>().ToList()) f.Destroy();
                        }
                        break;
                }
            }
        }

        /// <summary>The ground gives up what lies deep in it: the deposits a deep drill would reach come up, a share at a
        /// time (up to 25 for each power), without a drill. Kept for the mage's work (not cast in battle).</summary>
        internal static void DrawUp(Pawn caster, Map map, List<IntVec3> cells, float power)
        {
            int budget = Mathf.RoundToInt(25f * power), got = 0;
            ThingDef what = null;
            foreach (IntVec3 c in cells)
            {
                if (budget <= 0) break;
                ThingDef d = map.deepResourceGrid.ThingDefAt(c);
                int n = d == null ? 0 : map.deepResourceGrid.CountAt(c);
                if (n <= 0) continue;
                int take = Mathf.Min(n, budget, Mathf.Max(1, d.deepCountPerPortion));
                map.deepResourceGrid.SetAt(c, d, n - take);
                Thing t = ThingMaker.MakeThing(d); t.stackCount = take;
                GenPlace.TryPlaceThing(t, c, map, ThingPlaceMode.Near);
                budget -= take; got += take; what = d;
            }
            if (got > 0) Messages.Message("The earth gives up " + got + " " + what.label + " from deep below.", new TargetInfo(cells[0], map), MessageTypeDefOf.PositiveEvent, false);
        }

        public static void Petrified(Pawn caster)
        {
            MagicFx.Give(caster, EarthAirDefOf.Avir_Petrified, Mathf.RoundToInt(GenDate.TicksPerHour));
            caster.stances?.stunner?.StunFor(180, caster);
            SpellSlip.Earth(caster);
            Messages.Message(caster.LabelShortCap + " lost the earth - the ground shuddered under everyone round, and the stone takes hold of their limbs.", caster, MessageTypeDefOf.NegativeEvent, false);
        }
    }

    public static class AirMagic
    {
        public static bool Is(RuneDef el) => el != null && el.kind == RuneKind.Air;

        /// <summary>Water and air in one spell: a storm - the chained strike of the wind becomes lightning.</summary>
        public static bool Storm;

        public static void Cast(Pawn caster, RitualSchema s, RitualReading r, LocalTargetInfo target, float power)
        {
            r = SpellMods.Begin(s, r);
            Map map = caster.Map;
            Hediff kept = caster.health.hediffSet.GetFirstHediffOfDef(EarthAirDefOf.Avir_StoredAir);
            if (kept != null && !r.verbs.Any(v => v.kind == RuneKind.Drain)) { power *= 1f + kept.Severity; caster.health.RemoveHediff(kept); }
            IntVec3 at = target.Cell;
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            float radius = FireMagic.PointRadius(r);
            if (r.barrier > 0) MagicFx.Ward(caster, forms.Any(f => f.kind == RuneKind.Aura) ? caster.Position : at, map, Mathf.Max(0.9f, radius - 1f), power, EarthAirDefOf.Avir_AirWard, "Avir_Rune_Air", r.amplify);
            MagicFx.WardMage(caster, power, EarthAirDefOf.Avir_AirWard, "Avir_Rune_Air", r.amplify);
            if (forms.Count == 0) { if (verbs.Count > 0) Apply(caster, map, verbs, FireMagic.Point(at, map, radius), at, power, r); return; }
            foreach (RuneDef f in forms)
            {
                RitualReading rf = SpellMods.Verb(r, f); float fradius = FireMagic.PointRadius(rf);
                switch (f.kind)
                {
                    case RuneKind.Aura:
                        Apply(caster, map, verbs, FireMagic.Point(caster.Position, map, fradius + 1.5f), caster.Position, power, rf);
                        MapComponent_Tides.Raise(caster, fradius + 1.5f, power, verbs, RuneKind.Air);
                        break;
                    case RuneKind.Beam:
                        // a chained strike: it hits the first foe on the line and leaps on to the next, and the next -
                        // the wind's blow; in a storm (water with it) a bolt of lightning
                        var line = FireMagic.Beam(caster.Position, at, map, rf.breadth);
                        Chain(caster, line, map, 1 + rf.breadth + rf.amplify, power, Storm);
                        Apply(caster, map, verbs, line, caster.Position, power, rf);
                        break;
                    case RuneKind.Shape:
                        // a wall of wind between the two chosen points: foes who try to cross are thrown back
                        var wall = FireMagic.ShapeCells(caster, at, map, rf, forms);
                        int life = Mathf.RoundToInt(GenDate.TicksPerHour * 2f * power * (1 + rf.anchor + (verbs.Any(v => v.kind == RuneKind.Hold) ? 1 : 0)));
                        foreach (IntVec3 c in wall) AirWall.Place(c, map, caster, life);
                        Apply(caster, map, verbs.Where(v => v.kind != RuneKind.Hold).ToList(), wall, at, power, rf);
                        break;
                    case RuneKind.Step:
                        // the wind lifts the mage over whatever stands between - walls, rivers, foes
                        if (rf.thread > 0) SpellDash.Pull(caster, target, RuneKind.Air, power, fradius); else SpellDash.Carry(caster, at, RuneKind.Air, power, verbs, rf);
                        break;
                    case RuneKind.Restore:
                        // a second wind: breath back, tiredness lifted
                        foreach (Pawn p in MagicFx.PawnsIn(FireMagic.Point(at, map, Mathf.Max(0.9f, fradius - 1f)), map))
                        {
                            if (p.HostileTo(caster)) continue;
                            if (p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Min(1f, p.needs.rest.CurLevel + 0.25f * power);
                            foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => x.def == EarthAirDefOf.Avir_Breathless || x.def == EarthAirDefOf.Avir_Winded).ToList()) p.health.RemoveHediff(h);
                            Hediff tox = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup);
                            if (tox != null) { tox.Severity -= 0.1f * power; if (tox.Severity <= 0f) p.health.RemoveHediff(tox); }
                            FleckMaker.ThrowDustPuffThick(p.DrawPos, map, 1.2f, MagicFx.AirDust);
                        }
                        break;
                    case RuneKind.Enchant: MagicFx.Enchant(caster, at, map, power, EarthAirDefOf.Avir_AirEnchant); break;
                    case RuneKind.Summon: Messages.Message("Spirits are not made yet.", MessageTypeDefOf.RejectInput, false); break;
                    case RuneKind.Sense: FireMagic.Sense(caster, map, 30f + 10f * rf.reach, power); break;
                    default: if (verbs.Count > 0) Apply(caster, map, verbs, FireMagic.Point(at, map, fradius), at, power, rf); break;
                }
            }
        }

        internal static void Chain(Pawn caster, List<IntVec3> line, Map map, int jumps, float power, bool storm)
        {
            Pawn first = MagicFx.PawnsIn(line, map).Where(p => p != caster && MagicFx.Foe(p, caster)).OrderBy(p => p.Position.DistanceToSquared(caster.Position)).FirstOrDefault();
            Vector3 prev = caster.DrawPos;
            IntVec3 prevCell = caster.Position;
            var hit = new HashSet<Pawn>();
            Pawn cur = first;
            for (int i = 0; cur != null && i <= jumps; i++)
            {
                hit.Add(cur);
                IntVec3 from = cur.Position;
                Vector3 at = cur.DrawPos;
                if (storm)
                {
                    // the discharge: burns and a jolt; machines are struck harder, the soaked take more
                    MapComponent_SpellSprites.Bolt(map, prev, at);
                    MapComponent_SpellSprites.Put(map, "Storm/Strike", at, 1.8f, 20);
                    FleckMaker.ThrowLightningGlow(at, map, 1.4f);
                    float wet = cur.health.hediffSet.GetFirstHediffOfDef(WaterDefOf.Avir_Soaked) != null ? 1.5f : 1f;
                    cur.TakeDamage(new DamageInfo(DamageDefOf.Burn, 8f * power * wet, 0.3f, -1f, caster));
                    if (!cur.Dead && cur.RaceProps.IsMechanoid) cur.TakeDamage(new DamageInfo(DamageDefOf.EMP, 10f * power, 0f, -1f, caster));
                    if (!cur.Dead) MagicFx.Stun(cur, 50, caster);
                    // standing in water: the bolt runs on through it
                    Reactions.Conduct(from, map, caster, power);
                }
                else
                {
                    // the wind's blow, passed from one to the next: each is struck and thrown on
                    Vector3 d = at - prev;
                    int n = Mathf.Clamp(Mathf.RoundToInt(d.MagnitudeHorizontal()), 2, 8);
                    for (int k = 1; k <= n; k++) FleckMaker.ThrowDustPuffThick(prev + d * (k / (float)n), map, 1.1f, MagicFx.AirDust);
                    cur.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 7f * power, 0f, -1f, caster));
                    if (!cur.Dead) MagicFx.Push(cur, prevCell, 2, map);
                }
                prev = at; prevCell = from;
                cur = GenRadial.RadialDistinctThingsAround(from, map, 5.5f, true).OfType<Pawn>()
                    .Where(p => p != caster && MagicFx.Foe(p, caster) && !hit.Contains(p) && GenSight.LineOfSight(from, p.Position, map))
                    .OrderBy(p => p.Position.DistanceToSquared(from)).FirstOrDefault();
            }
        }

        internal static void Apply(Pawn caster, Map map, List<RuneDef> verbs, List<IntVec3> cells, IntVec3 from, float power, RitualReading r)
        {
            if (cells.Count == 0) return;
            foreach (RuneDef v in verbs)
            {
                RitualReading rv = SpellMods.Verb(r, v);
                switch (v.kind)
                {
                    case RuneKind.Release:
                        // a gust: foes thrown back hard; and the wind feeds whatever fire it meets
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, (5f + 2f * rv.amplify) * power, 0f, -1f, caster));
                            // the heavy (body and gear) are not thrown far - the gust takes their feet instead, and the
                            // ground strikes them through any plate
                            if (!p.Dead && SpellBody.Heavy(p) >= 1.3f) { SpellHurl.Topple(p, caster, power); if (!p.Dead) MagicFx.Fell(p, from, 1, map, caster, power, Mathf.RoundToInt(90f * power)); }
                            else MagicFx.Push(p, from, 4 + rv.amplify, map);
                        }
                        foreach (IntVec3 c in cells)
                        {
                            foreach (Fire fire in c.GetThingList(map).OfType<Fire>().ToList()) fire.fireSize = Mathf.Min(Fire.MaxFireSize, fire.fireSize + 0.4f);
                            if (Rand.Chance(0.4f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.6f, MagicFx.AirDust);
                        }
                        break;
                    case RuneKind.Hold:
                        // the air is held still and thin: hard to breathe; fire starves
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            if (p.RaceProps.IsFlesh) MagicFx.Give(p, EarthAirDefOf.Avir_Breathless, Mathf.RoundToInt(GenDate.TicksPerHour * 0.5f * power));
                            // the still air leans on them: they go slowly through it
                            MagicFx.Give(p, EarthAirDefOf.Avir_Buffeted, Mathf.RoundToInt(900f * power));
                        }
                        foreach (IntVec3 c in cells) FireMagic.Extinguish(c, map);
                        break;
                    case RuneKind.Drain:
                        // the breath pulled out of them; the mage keeps the air for the next spell
                        float took = 0f;
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster) || !p.RaceProps.IsFlesh) continue;
                            MagicFx.Give(p, EarthAirDefOf.Avir_Breathless, Mathf.RoundToInt(GenDate.TicksPerHour * power));
                            MagicFx.Fell(p, p.Position, 0, map, caster, power, 70);
                            took += 0.3f;
                        }
                        foreach (IntVec3 c in cells) FireMagic.Extinguish(c, map);
                        power = MagicFx.Drained(caster, verbs, v, EarthAirDefOf.Avir_StoredAir, took, power);
                        break;
                    case RuneKind.Lift:
                        // an updraft: they are thrown up, hang there, and fall
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map))
                        {
                            if (!MagicFx.Foe(p, caster)) continue;
                            if (rv.thread > 0) SpellHurl.Press(p, caster, power * 1.3f, false); else MagicFx.Lift(p, caster, power * 1.3f);
                        }
                        foreach (IntVec3 c in cells) if (Rand.Chance(0.5f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 2f, MagicFx.AirDust);
                        break;
                    case RuneKind.Growth:
                        // the wind rises and widens: a ring two cells wider is swept
                        var wide = cells.SelectMany(c => GenRadial.RadialCellsAround(c, 2f, true)).Where(c => c.InBounds(map)).Distinct().ToList();
                        foreach (Pawn p in MagicFx.PawnsIn(wide, map)) if (MagicFx.Foe(p, caster)) MagicFx.Push(p, from, 2, map);
                        foreach (IntVec3 c in wide) if (Rand.Chance(0.15f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.4f, MagicFx.AirDust);
                        break;
                    case RuneKind.Purge:
                        // clean air: small flames blown out, every gas blown off the ground, and breathed out of friends' bodies
                        foreach (IntVec3 c in cells) foreach (Fire fire in c.GetThingList(map).OfType<Fire>().ToList()) if (fire.fireSize < 0.6f) fire.Destroy();
                        Cleansing.ClearGas(cells, map);
                        foreach (Pawn p in MagicFx.PawnsIn(cells, map)) if (!p.HostileTo(caster)) Cleansing.AirOut(p, 0.2f * power);
                        break;
                }
            }
        }

        public static void Winded(Pawn caster)
        {
            MagicFx.Give(caster, EarthAirDefOf.Avir_Winded, Mathf.RoundToInt(GenDate.TicksPerHour));
            caster.stances?.stunner?.StunFor(120, caster);
            SpellSlip.Air(caster);
            Messages.Message(caster.LabelShortCap + " lost the wind - it burst every way and tore the breath out of them.", caster, MessageTypeDefOf.NegativeEvent, false);
        }
    }

    /// <summary>What the elements share: who is a foe, stuns and pushes (an earth ward roots its wearer against both),
    /// wards, stored power, enchanted weapons, rubble.</summary>
    public static class MagicFx
    {
        public static readonly Color EarthDust = new Color(0.55f, 0.42f, 0.3f, 0.7f);
        public static readonly Color AirDust = new Color(0.85f, 0.92f, 0.95f, 0.5f);

        public static IEnumerable<Pawn> PawnsIn(List<IntVec3> cells, Map map) =>
            cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Where(p => !p.Dead).Distinct().ToList();

        /// <summary>Is there a verb after this one in the circle (outward of it)? What a drain takes then feeds those
        /// at once, instead of being kept for the next spell.</summary>
        public static bool Later(List<RuneDef> verbs, RuneDef v) => verbs.LastIndexOf(v) < verbs.Count - 1;

        /// <summary>What a drain took: fed straight into the verbs after it, or kept by the mage for the next spell.</summary>
        public static float Drained(Pawn caster, List<RuneDef> verbs, RuneDef v, HediffDef keep, float took, float power)
        {
            if (took <= 0f) return power;
            if (Later(verbs, v)) { FleckMaker.ThrowLightningGlow(caster.DrawPos, caster.Map, 0.8f); return power * (1f + Mathf.Min(1f, took)); }
            Store(caster, keep, took);
            return power;
        }

        /// <summary>Struck by the caster's blow: anyone standing in it - friend or foe, and the caster too if they strike at
        /// their own feet; only a ward keeps them. A shaped blow (two Barriers on the outer edge of the circle) spares the
        /// caster's people - and the caster among them.</summary>
        public static bool Foe(Pawn p, Pawn caster) => p != null && !p.Dead && (!SpellSculpt.Active || caster == null || SpellSculpt.Struck(p, caster));

        /// <summary>Held fast in the ground by earth: cannot be thrown, drawn nor moved.</summary>
        public static bool Rooted(Pawn p) => p.health?.hediffSet?.GetFirstHediffOfDef(EarthAirDefOf.Avir_Rooted) != null;

        public static void Stun(Pawn p, int ticks, Pawn by)
        {
            if (Rooted(p) || SpellWards.Stops(p, false)) return;
            p.stances?.stunner?.StunFor(ticks, by);
        }

        public static void Push(Pawn p, IntVec3 from, int dist, Map map) => SpellHurl.Throw(p, from, dist, map);

        /// <summary>Knocked off their feet: thrown a little from a point and left lying.</summary>
        /// <summary>Knocked down: the heavier (body and gear), the longer they lie; bionic legs get them up sooner, a peg or a stump later.</summary>
        /// <summary>Drawn down into the ground by its own weight: the heavier, the deeper and the longer (Магия-правила.md).</summary>
        public static void Bury(Pawn p, float power)
        {
            HediffDef d = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Buried");
            if (d == null || p == null || p.Dead || SpellWards.Stops(p, false)) return;
            float heavy = SpellBody.Heavy(p);
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(d) ?? p.health.AddHediff(d);
            h.Severity = Mathf.Min(1f, h.Severity + 0.35f * power * heavy);
            HediffComp_Disappears dc = h.TryGetComp<HediffComp_Disappears>();
            if (dc != null) dc.ticksToDisappear = Mathf.Max(dc.ticksToDisappear, Mathf.RoundToInt(600f * power * heavy));
            Give(p, EarthAirDefOf.Avir_Rooted, Mathf.RoundToInt(600f * power * heavy));
            if (p.Spawned) FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 1.6f, EarthDust);
        }

        public static void Fell(Pawn p, IntVec3 from, int dist, Map map, Pawn by, float power, int ticks)
        {
            var b = SpellBody.Of(p);
            SpellHurl.Fell(p, from, dist, map, by, power, Mathf.RoundToInt(ticks * SpellBody.Heavy(p) * b.legsLand));
        }

        /// <summary>Thrown up into the air, to fall.</summary>
        public static void Lift(Pawn p, Pawn by, float power) => SpellHurl.Lift(p, by, power);

        public static void Give(Pawn p, HediffDef def, int ticks)
        {
            if (def == null) return;
            // a ward keeps off what magic would leave on the body - harm only, not the good it gives
            if (def.isBad && SpellWards.Stops(p, true)) return;
            ticks = SpellBodyEffects.Ticks(p, def, ticks);
            if (ticks <= 0) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(def) ?? p.health.AddHediff(def);
            h.Severity = Mathf.Max(h.Severity, 1f);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, ticks);
        }

        public static void Store(Pawn caster, HediffDef def, float amount)
        {
            Hediff k = caster.health.hediffSet.GetFirstHediffOfDef(def) ?? caster.health.AddHediff(def);
            k.Severity = Mathf.Min(1f, k.Severity + amount);
        }

        public static void Rubble(IntVec3 c, Map map)
        {
            ThingDef d = EarthStone.Rubble ?? ThingDefOf.Filth_RubbleRock;
            if (d != null && c.InBounds(map) && c.Standable(map)) FilthMaker.TryMakeFilth(c, map, d);
            // now and then a whole chunk of the map's own rock breaks off and stays - a geomancer's marks on the land
            if (c.InBounds(map) && c.Standable(map) && Rand.Chance(0.04f))
            {
                ThingDef rock = Find.World.NaturalRockTypesIn(map.Tile).Select(r => r.building?.mineableThing).Where(x => x != null).RandomElementWithFallback();
                if (rock != null) GenPlace.TryPlaceThing(ThingMaker.MakeThing(rock), c, map, ThingPlaceMode.Near);
            }
        }

        public static void Ward(Pawn caster, IntVec3 at, Map map, float radius, float power, HediffDef ward, string rune, int amplify = 0)
        {
            MapComponent_WardsDrawn.RaiseWard(map, at, radius, DefDatabase<RuneDef>.GetNamedSilentFail(rune), ComboKind.None, caster, Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power), power, amplify);
        }

        /// <summary>A barrier on the very edge of the circle: a ward on the mage, wherever the spell takes them.</summary>
        public static void WardMage(Pawn caster, float power, HediffDef ward, string rune, int amplify = 0)
        {
            SpellWards.Note(caster?.LabelShort + " - " + rune + ": ward on the caster " + SpellMods.WardOnMage);
            if (!SpellMods.WardOnMage || ward == null || caster.Dead) return;
            SpellWards.Lay(caster, ward, power, amplify, Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power));
        }

        public static void Enchant(Pawn caster, IntVec3 at, Map map, float power, HediffDef def)
        {
            Pawn p = at.GetFirstPawn(map) ?? caster;
            if (p.HostileTo(caster)) p = caster;
            Give(p, def, Mathf.RoundToInt(GenDate.TicksPerHour * 6f * power));
            p.health.hediffSet.GetFirstHediffOfDef(def).Severity = Mathf.Clamp(0.5f * power, 0.2f, 1f);
            FleckMaker.ThrowDustPuffThick(p.DrawPos, map, 1.2f, def == EarthAirDefOf.Avir_EarthEnchant ? EarthDust : AirDust);
        }
    }

    /// <summary>A stone wall raised by a mage: it stands for a while, stops shots and feet, then crumbles.</summary>
    public class EarthWall : Building
    {
        public int left = 7500;

        public static void Raise(IntVec3 c, Map map, int life, Faction of = null)
        {
            if (!c.InBounds(map) || c.GetEdifice(map) != null || c.Impassable(map)) return;
            foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList()) WaterMagic.Push(p, c + IntVec3.North, 1, map);
            if (c.GetThingList(map).Any(t => t is Pawn)) return;
            var w = (EarthWall)ThingMaker.MakeThing(EarthAirDefOf.Avir_EarthWall);
            w.left = life;
            GenSpawn.Spawn(w, c, map);
            // the caster's own wall: a foe shut in by it knows whose it is, and tries to break out
            if (of != null) w.SetFaction(of);
            FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 2f, MagicFx.EarthDust);
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref left, "left", 7500); }

        protected override void Tick()
        {
            base.Tick();
            if (--left > 0) return;
            Map m = Map; IntVec3 c = Position;
            Destroy(DestroyMode.Vanish);
            FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), m, 1.8f, MagicFx.EarthDust);
            MagicFx.Rubble(c, m);
        }
    }

    /// <summary>A wall of wind: whoever of the mage's foes steps into it is thrown back the way they came.</summary>
    public class AirWall : ThingWithComps
    {
        public Pawn owner;
        public int left = 5000;

        public static void Place(IntVec3 c, Map map, Pawn owner, int life)
        {
            if (!c.InBounds(map)) return;
            AirWall old = c.GetThingList(map).OfType<AirWall>().FirstOrDefault();
            if (old != null) { old.left = Mathf.Max(old.left, life); return; }
            var w = (AirWall)ThingMaker.MakeThing(EarthAirDefOf.Avir_AirWall);
            w.owner = owner; w.left = life;
            GenSpawn.Spawn(w, c, map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner");
            Scribe_Values.Look(ref left, "left", 5000);
        }

        protected override void Tick()
        {
            base.Tick();
            if (--left <= 0) { Destroy(); return; }
            if (this.IsHashIntervalTick(40)) FleckMaker.ThrowDustPuffThick(DrawPos + Rand.InsideUnitCircleVec3 * 0.4f, Map, 1.1f, MagicFx.AirDust);
            if (!this.IsHashIntervalTick(15)) return;
            foreach (Pawn p in Position.GetThingList(Map).OfType<Pawn>().ToList())
                if (owner != null && MagicFx.Foe(p, owner)) { MagicFx.Push(p, owner.Position, -2, Map); MagicFx.Give(p, EarthAirDefOf.Avir_Buffeted, 300); }
        }
    }

    /// <summary>The weapons of earth and air: earth adds a heavy blow that may stun; air throws the foe back.</summary>
    // on the blow itself, whatever weapon or mod makes it: any melee hit that lands
    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class Patch_EarthAirEnchantMelee
    {
        public static void Postfix(Verb_MeleeAttack __instance, bool __result)
        {
            if (!__result) return;
            LocalTargetInfo target = __instance.CurrentTarget;
            if (!AvirMagic.On || EarthAirDefOf.Avir_EarthEnchant == null) return;
            Pawn p = __instance.CasterPawn;
            if (p?.health?.hediffSet == null || !(target.Thing is Pawn foe) || foe.Dead) return;
            Hediff e = p.health.hediffSet.GetFirstHediffOfDef(EarthAirDefOf.Avir_EarthEnchant);
            if (e != null)
            {
                foe.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5f + 6f * e.Severity, 0f, -1f, p));
                if (!foe.Dead && Rand.Chance(0.25f)) MagicFx.Stun(foe, 70, p);
            }
            if (!foe.Dead && foe.Spawned && p.health.hediffSet.GetFirstHediffOfDef(EarthAirDefOf.Avir_AirEnchant) != null)
            {
                // a gust off the weapon: seen, and the foe thrown back
                for (int i = 0; i < 3; i++) FleckMaker.ThrowDustPuffThick(foe.DrawPos + Rand.InsideUnitCircleVec3 * 0.3f, foe.Map, 1.2f, MagicFx.AirDust);
                MagicFx.Push(foe, p.Position, 2, foe.Map);
            }
        }
    }

    [DefOf]
    public static class EarthAirDefOf
    {
        public static HediffDef Avir_Rooted, Avir_Dizzy, Avir_StoneSkin, Avir_HeavyGround, Avir_Heavy, Avir_StoredStone, Avir_EarthWard, Avir_EarthEnchant, Avir_Petrified;
        public static HediffDef Avir_Breathless, Avir_Buffeted, Avir_StoredAir, Avir_AirWard, Avir_AirEnchant, Avir_Winded;
        public static ThingDef Avir_EarthWall, Avir_AirWall;
        static EarthAirDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(EarthAirDefOf)); }
    }
}
