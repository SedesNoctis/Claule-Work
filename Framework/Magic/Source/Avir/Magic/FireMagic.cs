using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    // =====================================================================================
    // FIRE - the first branch of the rune grammar: a spell is "verb (element) through a form".
    // The form says where (around the mage, a line, a wall, a step, on a friend, a weapon, a wisp, a sense);
    // the verb says what the fire does there:
    //   release - fire is made (with amplification it explodes); hold - fire that stays put, does not spread,
    //   smoulders and warms friends; drain - the heat is taken and kept for the next fire; lift - the flame is
    //   raised into a pillar or a wall; growth - fire spreads; purge - fire is put out, infection burnt away.
    // The barrier rune makes a ward of flame: fire does not pass it. Purge around the mage (or anchored)
    // leaves ground that will not take fire. The price of fire is the raging flame: a lost hold lets the fire
    // around the mage slip out of control for a while.
    // =====================================================================================
    public static class FireMagic
    {
        public static bool Is(RuneDef el) => el != null && el.kind == RuneKind.Fire;

        public static void Cast(Pawn caster, RitualSchema s, RitualReading r, LocalTargetInfo target, float power)
        {
            r = SpellMods.Begin(s, r);
            Map map = caster.Map;
            Hediff heat = caster.health.hediffSet.GetFirstHediffOfDef(FireDefOf.Avir_StoredHeat);
            bool drains = r.verbs.Any(v => v.kind == RuneKind.Drain);
            if (heat != null && !drains) { power *= 1f + heat.Severity; caster.health.RemoveHediff(heat); }
            IntVec3 at = target.Cell;
            var verbs = r.verbs.Where(v => v.role == RuneRole.Verb).ToList();
            var forms = r.verbs.Where(v => v.role == RuneRole.Form).ToList();
            float radius = PointRadius(r);
            if (r.barrier > 0) Ward(caster, forms.Any(f => f.kind == RuneKind.Aura) ? caster.Position : at, map, Mathf.Max(0.9f, radius - 1f), power);
            MagicFx.WardMage(caster, power, FireDefOf.Avir_FireWard, "Avir_Rune_Fire");
            if (forms.Count == 0)
            {
                if (verbs.Count > 0) Apply(caster, map, verbs, Point(at, map, radius), at, power, r, false);
                return;
            }
            foreach (RuneDef f in forms)
            {
                RitualReading rf = SpellMods.Verb(r, f); float fradius = FireMagic.PointRadius(rf);
                switch (f.kind)
                {
                    case RuneKind.Aura:
                        Apply(caster, map, verbs, Point(caster.Position, map, fradius + 1.5f), caster.Position, power, rf, true);
                        break;
                    case RuneKind.Beam:
                        Apply(caster, map, verbs, Beam(caster.Position, at, map, rf.breadth), at, power, rf, false);
                        break;
                    case RuneKind.Shape:
                        Apply(caster, map, verbs, WallTo.IsValid ? WallLine(at, WallTo, map, WallMax(rf)) : Wall(caster.Position, at, map, 5 + 2 * rf.breadth), at, power, rf, false);
                        break;
                    case RuneKind.Step:
                        var path = GenSight.BresenhamCellsBetween(caster.Position, at).Where(c => c.InBounds(map) && c != caster.Position).ToList();
                        // the flame carries the mage there - seen all the way
                        if (rf.thread > 0) { SpellDash.Pull(caster, target, RuneKind.Fire, power, fradius); break; }
                        SpellDash.Carry(caster, at, RuneKind.Fire);
                        Apply(caster, map, verbs.Count > 0 ? verbs : new List<RuneDef> { RuneDefOfFire.Hold }, path, at, power, rf, false);
                        break;
                    case RuneKind.Restore:
                        Cauterize(caster, at, map, fradius, power, verbs);
                        break;
                    case RuneKind.Enchant:
                        Enchant(caster, at, map, power);
                        break;
                    case RuneKind.Summon:
                        Messages.Message("Spirits are not made yet.", MessageTypeDefOf.RejectInput, false);
                        break;
                    case RuneKind.Sense:
                        Sense(caster, map, 25f + 10f * rf.reach, power);
                        break;
                    default:
                        if (verbs.Count > 0) Apply(caster, map, verbs, Point(at, map, fradius), at, power, rf, false);
                        break;
                }
            }
        }

        // ---------------------------------------------------------------- where
        /// <summary>Where a wall ordered with two clicks ends (Invalid: across the target, as before).</summary>
        public static IntVec3 WallTo = IntVec3.Invalid;

        /// <summary>The radius of a spell striking a point: round enough to read as a ball of fire.</summary>
        public static float PointRadius(RitualReading r) => 2.4f + 1.5f * r.breadth;

        public static int WallMax(RitualReading r) => 7 + 3 * r.breadth;

        /// <summary>A wall from one chosen cell to the other - straight or slanting - no longer than it may be.</summary>
        public static List<IntVec3> WallLine(IntVec3 from, IntVec3 to, Map map, int max)
        {
            var cells = new List<IntVec3>();
            foreach (IntVec3 c in GenSight.BresenhamCellsBetween(from, to))
            {
                if (cells.Count >= max) break;
                if (c.InBounds(map)) cells.Add(c);
            }
            return cells;
        }

        public static List<IntVec3> Point(IntVec3 at, Map map, float radius) =>
            GenRadial.RadialCellsAround(at, radius, true).Where(c => c.InBounds(map)).ToList();

        public static List<IntVec3> Beam(IntVec3 from, IntVec3 to, Map map, int breadth)
        {
            var cells = new HashSet<IntVec3>();
            foreach (IntVec3 c in GenSight.BresenhamCellsBetween(from, to))
            {
                if (c == from || !c.InBounds(map)) continue;
                cells.Add(c);
                if (breadth > 0) foreach (IntVec3 n in GenAdj.CellsAdjacent8Way(new TargetInfo(c, map))) if (n.InBounds(map) && n != from) cells.Add(n);
                if (c.GetEdifice(map) is Building b && b.def.Fillage == FillCategory.Full) break;   // a wall stops the line
            }
            return cells.ToList();
        }

        public static List<IntVec3> Wall(IntVec3 from, IntVec3 at, Map map, int length)
        {
            Vector3 d = (at - from).ToVector3();
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            Vector3 side = new Vector3(-d.z, 0f, d.x).normalized;
            var cells = new HashSet<IntVec3>();
            for (int i = -length / 2; i <= length / 2; i++)
            {
                IntVec3 c = (at.ToVector3Shifted() + side * i).ToIntVec3();
                if (c.InBounds(map)) cells.Add(c);
            }
            return cells.ToList();
        }

        // ---------------------------------------------------------------- what
        private static void Apply(Pawn caster, Map map, List<RuneDef> verbs, List<IntVec3> cells, IntVec3 at, float power, RitualReading r, bool aroundSelf)
        {
            if (cells.Count == 0) return;
            foreach (RuneDef v in verbs)
            {
                RitualReading rv = SpellMods.Verb(r, v);
                switch (v.kind)
                {
                    case RuneKind.Release:
                        Burn(caster, map, cells, 8f * power);
                        // a ball bursts round: a flash and a ring of flame at the point
                        if (!aroundSelf && cells.Count > 6)
                        {
                            FleckMaker.Static(at.ToVector3Shifted(), map, FleckDefOf.ExplosionFlash, Mathf.Sqrt(cells.Count) * 1.6f);
                            foreach (IntVec3 c in cells) FleckMaker.ThrowFireGlow(c.ToVector3Shifted(), map, 1.1f);
                        }
                        foreach (IntVec3 c in cells) if (Rand.Chance(0.5f)) FireUtility.TryStartFireIn(c, map, 0.3f + 0.2f * power, caster);
                        if (rv.amplify > 0)
                            GenExplosion.DoExplosion(aroundSelf ? at : cells[cells.Count - 1], map, 1.9f + rv.amplify, DamageDefOf.Flame, caster, Mathf.RoundToInt(12f * power), -1f,
                                null, null, null, null, null, 0f, 1, null, null, 255, false, null, 0f, 1, 0.5f, false, null, new List<Thing> { caster });
                        break;
                    case RuneKind.Hold:
                        foreach (IntVec3 c in cells) HeldFlame.Place(c, map, caster, power, false);
                        Warm(caster, map, cells, power);
                        break;
                    case RuneKind.Drain:
                        float took = 0f;
                        foreach (IntVec3 c in cells) took += Extinguish(c, map);
                        if (took > 0f) FleckMaker.ThrowHeatGlow(caster.Position, map, 1.5f);
                        power = MagicFx.Drained(caster, verbs, v, FireDefOf.Avir_StoredHeat, 0.15f * took, power);
                        break;
                    case RuneKind.Lift:
                        foreach (IntVec3 c in cells)
                        {
                            foreach (Fire f in c.GetThingList(map).OfType<Fire>().ToList()) f.fireSize = Fire.MaxFireSize;
                            HeldFlame.Place(c, map, caster, power, true);
                        }
                        Burn(caster, map, cells, 5f * power);
                        break;
                    case RuneKind.Growth:
                        var near = cells.SelectMany(c => GenAdj.CellsAdjacent8Way(new TargetInfo(c, map))).Concat(cells).Where(c => c.InBounds(map)).Distinct().ToList();
                        foreach (IntVec3 c in near)
                        {
                            foreach (Fire f in c.GetThingList(map).OfType<Fire>().ToList()) f.fireSize = Mathf.Min(Fire.MaxFireSize, f.fireSize + 0.5f);
                            if (Rand.Chance(0.8f)) FireUtility.TryStartFireIn(c, map, 0.4f + 0.2f * power, caster);
                        }
                        break;
                    case RuneKind.Purge:
                        foreach (IntVec3 c in cells) Extinguish(c, map);
                        foreach (Pawn p in PawnsIn(cells, map)) if (!p.HostileTo(caster)) BurnAwayInfection(p, power);
                        if (aroundSelf || rv.anchor > 0) map.GetComponent<MapComponent_FireMagic>()?.AddNoFire(cells, Mathf.RoundToInt(GenDate.TicksPerHour * 6f * power * (1 + rv.anchor)));
                        break;
                }
            }
            foreach (IntVec3 c in cells) if (Rand.Chance(0.25f)) FleckMaker.ThrowFireGlow(c.ToVector3Shifted(), map, 0.8f);
        }

        private static IEnumerable<Pawn> PawnsIn(List<IntVec3> cells, Map map) =>
            cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Where(p => !p.Dead).Distinct().ToList();

        private static void Burn(Pawn caster, Map map, List<IntVec3> cells, float dmg)
        {
            foreach (Pawn p in PawnsIn(cells, map))
            {
                if (p == caster || !p.HostileTo(caster)) continue;
                p.TakeDamage(new DamageInfo(DamageDefOf.Flame, dmg, 0f, -1f, caster));
                if (Rand.Chance(0.3f)) p.TryAttachFire(0.4f, caster);
            }
        }

        internal static float Extinguish(IntVec3 c, Map map)
        {
            float took = 0f;
            foreach (Thing t in c.GetThingList(map).ToList())
            {
                if (t is Fire f) { took += f.fireSize; f.Destroy(); }
                else if (t.GetAttachment(ThingDefOf.Fire) is Fire af) { took += af.fireSize; af.Destroy(); }
                else if (t is HeldFlame hf) { took += 0.5f; hf.Destroy(); }
            }
            return took;
        }

        private static void Warm(Pawn caster, Map map, List<IntVec3> cells, float power)
        {
            var near = cells.SelectMany(c => GenRadial.RadialCellsAround(c, 1.5f, true)).Where(c => c.InBounds(map)).Distinct().ToList();
            foreach (Pawn p in PawnsIn(near, map))
            {
                if (p.HostileTo(caster)) continue;
                Hediff h = p.health.hediffSet.GetFirstHediffOfDef(FireDefOf.Avir_FireWarmth) ?? p.health.AddHediff(FireDefOf.Avir_FireWarmth);
                h.Severity = Mathf.Clamp01(Mathf.Max(h.Severity, 0.5f * power));
            }
        }

        private static void BurnAwayInfection(Pawn p, float power)
        {
            foreach (Hediff h in p.health.hediffSet.hediffs.Where(x => x.def == HediffDefOf.WoundInfection || x.def.defName.Contains("Infection") || x.def.defName.Contains("Parasite")).ToList())
            {
                h.Severity -= 0.25f * power;
                if (h.Severity <= 0f) p.health.RemoveHediff(h);
            }
        }

        private static void Ward(Pawn caster, IntVec3 at, Map map, float radius, float power)
        {
            foreach (Pawn p in PawnsIn(Point(at, map, radius), map))
            {
                if (p.HostileTo(caster)) continue;
                Hediff w = p.health.hediffSet.GetFirstHediffOfDef(FireDefOf.Avir_FireWard) ?? p.health.AddHediff(FireDefOf.Avir_FireWard);
                w.Severity = 1f;
                HediffComp_Disappears d = w.TryGetComp<HediffComp_Disappears>();
                if (d != null) d.ticksToDisappear = Mathf.RoundToInt(GenDate.TicksPerHour * 6f * power);
                if (p.GetAttachment(ThingDefOf.Fire) is Fire f) f.Destroy();
                FleckMaker.ThrowFireGlow(p.DrawPos, map, 1f);
            }
            MapComponent_WardsDrawn.Raise(map, at, radius, DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Fire"), caster, Mathf.RoundToInt(GenDate.TicksPerHour * 6f * power));
        }

        private static void Cauterize(Pawn caster, IntVec3 at, Map map, float radius, float power, List<RuneDef> verbs)
        {
            float q = Mathf.Clamp(0.35f + 0.25f * power + (verbs.Any(v => v.kind == RuneKind.Growth) ? 0.2f : 0f), 0.2f, 0.95f);
            foreach (Pawn p in PawnsIn(Point(at, map, Mathf.Max(0.9f, radius - 1f)), map))
            {
                if (p.HostileTo(caster)) continue;
                foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.Bleeding).ToList())
                    inj.Tended(q, 1f);
                if (verbs.Any(v => v.kind == RuneKind.Purge)) BurnAwayInfection(p, power);
                FleckMaker.ThrowFireGlow(p.DrawPos, map, 0.7f);
            }
        }

        private static void Enchant(Pawn caster, IntVec3 at, Map map, float power)
        {
            Pawn p = at.GetFirstPawn(map) ?? caster;
            if (p.HostileTo(caster)) p = caster;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(FireDefOf.Avir_FireEnchant) ?? p.health.AddHediff(FireDefOf.Avir_FireEnchant);
            h.Severity = Mathf.Clamp(0.5f * power, 0.2f, 1f);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.RoundToInt(GenDate.TicksPerHour * 6f * power);
            FleckMaker.ThrowFireGlow(p.DrawPos, map, 1.2f);
        }

        private static void Wisp(Pawn caster, IntVec3 at, Map map, float power, int amplify)
        {
            if (!at.Standable(map)) at = caster.Position;
            var w = (FireWisp)ThingMaker.MakeThing(FireDefOf.Avir_FireWisp);
            w.owner = caster; w.power = power; w.left = Mathf.RoundToInt(1800f * power * (1 + amplify));
            GenSpawn.Spawn(w, at, map);
            FleckMaker.ThrowFireGlow(at.ToVector3Shifted(), map, 2f);
        }

        internal static void Sense(Pawn caster, Map map, float radius, float power)
        {
            var found = map.mapPawns.AllPawnsSpawned.Where(p => p != caster && !p.Dead && p.Position.DistanceTo(caster.Position) <= radius && p.RaceProps.IsFlesh).ToList();
            map.GetComponent<MapComponent_FireMagic>()?.AddSense(found, Mathf.RoundToInt(1800f * power));
            int foes = found.Count(p => p.HostileTo(caster));
            Messages.Message(caster.LabelShortCap + " feels " + found.Count + " warm bodies around" + (foes > 0 ? " - " + foes + " of them hostile." : "."), caster, MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>The price of fire: the hold is lost and the fire around the mage slips out of control.</summary>
        public static void Raging(Pawn caster)
        {
            Map map = caster.Map;
            if (map == null) return;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(caster.Position, 3.5f, false).InRandomOrder().Take(8))
                if (c.InBounds(map)) FireUtility.TryStartFireIn(c, map, 1f, caster);
            Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(FireDefOf.Avir_RagingFlame) ?? caster.health.AddHediff(FireDefOf.Avir_RagingFlame);
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = 1200;
            Messages.Message("Raging flame: " + caster.LabelShortCap + " can no longer hold back the fire around them.", caster, MessageTypeDefOf.NegativeEvent, false);
        }

        public static string Describe(RitualReading r)
        {
            var parts = r.verbs.Select(v => v.LabelCap.ToString()).ToList();
            if (r.barrier > 0) parts.Add("ward of flame");
            return "Fire - " + (parts.Count > 0 ? string.Join(", ", parts) : "nothing to do");
        }
    }

    /// <summary>Holds runes by name for the code (a step with no verb leaves a held trail).</summary>
    public static class RuneDefOfFire
    {
        private static RuneDef hold;
        public static RuneDef Hold => hold ?? (hold = DefDatabase<RuneDef>.GetNamed("Avir_Rune_Hold"));
    }

    [DefOf]
    public static class FireDefOf
    {
        public static HediffDef Avir_FireWard;
        public static HediffDef Avir_FireWarmth;
        public static HediffDef Avir_StoredHeat;
        public static HediffDef Avir_FireEnchant;
        public static HediffDef Avir_RagingFlame;
        public static ThingDef Avir_HeldFlame;
        public static ThingDef Avir_FireWisp;
        static FireDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(FireDefOf)); }
    }

    /// <summary>Held fire: burns in its place, never spreads, smoulders, warms; hurts only the caster's foes.</summary>
    public class HeldFlame : ThingWithComps
    {
        public Pawn owner;
        public float power = 1f;
        public bool tall;
        public int left = 1200;

        public static void Place(IntVec3 c, Map map, Pawn owner, float power, bool tall)
        {
            if (!c.InBounds(map) || c.Impassable(map) || c.Filled(map)) return;
            HeldFlame old = c.GetThingList(map).OfType<HeldFlame>().FirstOrDefault();
            if (old != null) { old.left = Mathf.Max(old.left, Life(power)); old.tall |= tall; return; }
            var f = (HeldFlame)ThingMaker.MakeThing(FireDefOf.Avir_HeldFlame);
            f.owner = owner; f.power = power; f.tall = tall; f.left = Life(power);
            GenSpawn.Spawn(f, c, map);
        }

        private static int Life(float power) => Mathf.RoundToInt(1200f * power);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "owner");
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_Values.Look(ref tall, "tall");
            Scribe_Values.Look(ref left, "left", 1200);
        }

        protected override void Tick()
        {
            base.Tick();
            if (--left <= 0 || Map.GetComponent<MapComponent_FireMagic>()?.NoFire(Position) == true) { Destroy(); return; }
            if (this.IsHashIntervalTick(60))
            {
                foreach (Pawn p in Position.GetThingList(Map).OfType<Pawn>().ToList())
                    if (owner == null || p.HostileTo(owner)) p.TakeDamage(new DamageInfo(DamageDefOf.Flame, (tall ? 6f : 3f) * power, 0f, -1f, owner));
                GenTemperature.PushHeat(Position, Map, tall ? 12f : 6f);
            }
            if (this.IsHashIntervalTick(90)) FleckMaker.ThrowSmoke(DrawPos, Map, tall ? 1.2f : 0.6f);
        }

        public override Vector2 DrawSize => tall ? new Vector2(1.6f, 1.6f) : new Vector2(1f, 1f);
    }

    /// <summary>A fire wisp: a small spirit of fire that throws flame at the caster's foes until it burns out.</summary>
    public class FireWisp : ThingWithComps
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
            if (--left <= 0) { FleckMaker.ThrowSmoke(DrawPos, Map, 1f); Destroy(); return; }
            if (!this.IsHashIntervalTick(90)) return;
            Pawn foe = Map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && !p.Downed && owner != null && p.HostileTo(owner) && p.Position.DistanceTo(Position) <= 7f && GenSight.LineOfSight(Position, p.Position, Map))
                .OrderBy(p => p.Position.DistanceToSquared(Position)).FirstOrDefault();
            if (foe == null) return;
            foe.TakeDamage(new DamageInfo(DamageDefOf.Flame, 6f * power, 0f, -1f, owner));
            if (Rand.Chance(0.25f)) foe.TryAttachFire(0.3f, owner);
            FleckMaker.ConnectingLine(DrawPos, foe.DrawPos, FleckDefOf.LineEMP, Map);
            FleckMaker.ThrowFireGlow(foe.DrawPos, Map, 0.8f);
        }
    }

    /// <summary>Ground that will not take fire, and the warm bodies a mage has sensed.</summary>
    public class MapComponent_FireMagic : MapComponent
    {
        private readonly Dictionary<IntVec3, int> noFire = new Dictionary<IntVec3, int>();
        private readonly Dictionary<Pawn, int> sensed = new Dictionary<Pawn, int>();
        private Material mark;

        public MapComponent_FireMagic(Map map) : base(map) { }

        public void AddNoFire(List<IntVec3> cells, int ticks)
        {
            int until = Find.TickManager.TicksGame + ticks;
            foreach (IntVec3 c in cells) noFire[c] = Mathf.Max(until, noFire.TryGetValue(c, out int u) ? u : 0);
        }

        public bool NoFire(IntVec3 c) => noFire.TryGetValue(c, out int u) && u > Find.TickManager.TicksGame;

        public void AddSense(List<Pawn> pawns, int ticks)
        {
            int until = Find.TickManager.TicksGame + ticks;
            foreach (Pawn p in pawns) sensed[p] = until;
        }

        public override void MapComponentTick()
        {
            if (noFire.Count == 0 || Find.TickManager.TicksGame % 60 != 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (var kv in noFire.ToList())
            {
                if (kv.Value <= now) { noFire.Remove(kv.Key); continue; }
                foreach (Fire f in kv.Key.GetThingList(map).OfType<Fire>().ToList()) f.Destroy();
            }
        }

        public override void MapComponentUpdate()
        {
            if (sensed.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            if (mark == null)
            {
                RuneDef fire = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Fire");
                if (fire?.Glow == null) return;
                mark = MaterialPool.MatFrom(fire.Glow, ShaderDatabase.MoteGlow, new Color(1f, 0.45f, 0.15f, 0.9f));
            }
            foreach (var kv in sensed.ToList())
            {
                Pawn p = kv.Key;
                if (kv.Value <= now || p.Dead || !p.Spawned || p.Map != map) { sensed.Remove(p); continue; }
                Vector3 at = p.DrawPos; at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(1.2f, 1f, 1.2f)), mark, 0);
            }
        }

        public override void MapComponentOnGUI()
        {
            if (Find.CurrentMap != map) return;
            foreach (Pawn p in sensed.Keys)
                if (p.Spawned && p.HostileTo(Faction.OfPlayer) && p.Map == map && map.fogGrid.IsFogged(p.Position))
                {
                    Vector2 s = p.DrawPos.MapToUIPosition();
                    Widgets.DrawBoxSolid(new Rect(s.x - 3f, s.y - 3f, 6f, 6f), new Color(1f, 0.4f, 0.1f, 0.9f));
                }
        }
    }

    /// <summary>Ward of flame: fire on the body goes out at once.</summary>
    public class HediffCompProperties_Fireproof : HediffCompProperties
    {
        public HediffCompProperties_Fireproof() { compClass = typeof(HediffComp_Fireproof); }
    }

    public class HediffComp_Fireproof : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Pawn.IsHashIntervalTick(30) || !Pawn.Spawned) return;
            if (Pawn.GetAttachment(ThingDefOf.Fire) is Fire f) f.Destroy();
        }
    }

    /// <summary>Raging flame: for a while fire keeps breaking out around the mage.</summary>
    public class HediffCompProperties_RagingFlame : HediffCompProperties
    {
        public HediffCompProperties_RagingFlame() { compClass = typeof(HediffComp_RagingFlame); }
    }

    public class HediffComp_RagingFlame : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Pawn.Spawned || !Pawn.IsHashIntervalTick(120)) return;
            IntVec3 c = Pawn.Position + GenRadial.RadialPattern[Rand.Range(2, GenRadial.NumCellsInRadius(3.5f))];
            if (c.InBounds(Pawn.Map)) FireUtility.TryStartFireIn(c, Pawn.Map, 0.6f, Pawn);
        }
    }

    /// <summary>The burning weapon: melee blows add flame and may set the foe alight.</summary>
    [HarmonyPatch(typeof(Verb_MeleeAttackDamage), "ApplyMeleeDamageToTarget")]
    public static class Patch_FireEnchantMelee
    {
        public static void Postfix(Verb_MeleeAttackDamage __instance, LocalTargetInfo target)
        {
            if (!AvirMagic.On || FireDefOf.Avir_FireEnchant == null) return;
            Pawn p = __instance.CasterPawn;
            Hediff h = p?.health?.hediffSet?.GetFirstHediffOfDef(FireDefOf.Avir_FireEnchant);
            if (h == null || !(target.Thing is Thing t) || t.Destroyed) return;
            t.TakeDamage(new DamageInfo(DamageDefOf.Flame, 4f + 8f * h.Severity, 0f, -1f, p));
            if (!t.Destroyed && Rand.Chance(0.3f)) t.TryAttachFire(0.4f, p);
        }
    }

    /// <summary>The burning weapon: bullets of an enchanted shooter may set what they hit alight.</summary>
    [HarmonyPatch(typeof(Bullet), "Impact")]
    public static class Patch_FireEnchantBullet
    {
        public static void Prefix(Bullet __instance, Thing hitThing)
        {
            if (!AvirMagic.On || FireDefOf.Avir_FireEnchant == null || hitThing == null || hitThing.Destroyed) return;
            if (!(__instance.Launcher is Pawn p) || p.health?.hediffSet?.GetFirstHediffOfDef(FireDefOf.Avir_FireEnchant) == null) return;
            if (Rand.Chance(0.25f)) hitThing.TryAttachFire(0.3f, p);
        }
    }
}
