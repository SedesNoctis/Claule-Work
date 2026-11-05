using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using LudeonTK;

namespace Avir.Magic
{
    /// <summary>Life, the first great circle (Магия-правила.md 0.28). Its law - Symbiosis - is that it makes nothing: it
    /// only moves. Withering takes from one thing into the mage's store of life; the other verbs give that store away.
    /// With an empty store nothing is given at all: the key rune of Life is Withering. A store filled by one spell can be
    /// spent by another, whenever the player likes. The great circle's own sections are its pure effect, cast where the
    /// spell ends; round small circles it mends the friends the spell touches - from the store too.</summary>
    public static class LifeMagic
    {
        public static readonly Color Necrotic = new Color(0.66f, 0.82f, 0.55f), NecroticDark = new Color(0.22f, 0.3f, 0.2f, 0.7f);
        private static HediffDef H(string n) => DefDatabase<HediffDef>.GetNamedSilentFail(n);
        public static HediffDef Regen => H("Avir_LifeRegen");
        public static DamageDef WitherDmg => DefDatabase<DamageDef>.GetNamedSilentFail("Avir_Withering") ?? DamageDefOf.Burn;
        public static TerrainDef DeadGround => DefDatabase<TerrainDef>.GetNamedSilentFail("Avir_DeadGround");

        public static bool Is(RuneDef e) => e != null && e.kind == RuneKind.Life;
        public static IEnumerable<SpellCircle> Circles(SpellCircle root) => WeaveSpell.Greats(root).Where(g => Is(g.element));
        /// <summary>Is this circle of the queue one of Life: a great circle of Life (its own runes), or a small circle with
        /// no element inside one (a small circle of Life - the author's rule).</summary>
        public static bool IsLink(SpellCircle root, SpellCircle c)
        {
            if (c == null) return false;
            if (c.Great) return Is(c.element);
            if (c.element != null) return false;
            SpellCircle box = root?.ParentOf(c);
            return box != null && box.Great && Is(box.element);
        }
        /// <summary>Every circle of Life in a spell that acts on its own: the great ones' own runes and the small ones inside them.</summary>
        public static IEnumerable<SpellCircle> Links(SpellCircle root) => root == null ? Enumerable.Empty<SpellCircle>() : root.Working.Where(c => IsLink(root, c) && c.OwnRunes.Any());
        private static bool Friend(Pawn p, Pawn caster) => p != null && !p.Dead && !p.HostileTo(caster) && (p.Faction == caster.Faction || p.HostFaction == caster.Faction || p == caster);
        private static bool Living(Pawn p) => p != null && p.RaceProps != null && !p.RaceProps.IsMechanoid && p.RaceProps.IsFlesh;
        public static float Radius(SpellCircle g) => 2.5f + 1.5f * g.OwnRunes.Count(x => x.kind == RuneKind.Breadth);

        /// <summary>The two sides of Life (the author): Withering (death - takes) and Restoration (life - gives). Every
        /// verb and every beam of Life is tied to one of them - "Release + Withering", "Beam + Restoration"; a verb left
        /// untied does nothing, and Arlie says so. A bare Withering or Restoration acts at the point itself.</summary>
        public static bool Side(RuneKind k) => k == RuneKind.Drain || k == RuneKind.Restore;
        public static bool Verb(RuneKind k) => k == RuneKind.Release || k == RuneKind.Beam || k == RuneKind.Hold || k == RuneKind.Growth || k == RuneKind.Purge;
        public static bool Knows(RuneKind k) => Side(k) || Verb(k);

        /// <summary>One act of a Life circle: a verb (or none) tied to its side; `hop` - the energy goes on at once
        /// along a Reach and a Thread written before it (the leap rule), to the nearest wounded one of the struck.</summary>
        public class Act { public RuneKind verb = (RuneKind)(-1); public RuneKind side; public bool hop; public int reach; }

        public static List<Act> Read(SpellCircle g, out List<RuneKind> untied)
        {
            var acts = new List<Act>(); untied = new List<RuneKind>();
            RuneKind? pending = null; bool thread = false; int reach = 0;
            var runes = g.sections.Select((sec, i) => new { sec, i }).SelectMany(x => (x.sec.runes ?? new List<RuneDef>()).Where(r => r != null)
                .Select(r => new { r, outer = x.i == g.sections.Count - 1 })).ToList();
            bool shaped = SpellSculpt.Of(g);
            foreach (var x in runes)
            {
                RuneKind k = x.r.kind;
                if (shaped && x.outer && (k == RuneKind.Barrier || k == RuneKind.Thread)) continue;   // the shaping, not runes of its own
                if (k == RuneKind.Reach) { reach++; continue; }
                if (k == RuneKind.Thread) { thread = true; continue; }
                if (Verb(k)) { if (pending != null) untied.Add(pending.Value); pending = k; continue; }
                if (Side(k))
                {
                    acts.Add(new Act { verb = pending ?? (RuneKind)(-1), side = k, hop = thread && reach > 0 && acts.Count > 0, reach = reach });
                    pending = null; thread = false; reach = 0;
                }
            }
            if (pending != null) untied.Add(pending.Value);
            return acts;
        }

        /// <summary>Whom a Life circle reaches: as everywhere, by its outer edge - two Barriers spare one's own (it strikes the
        /// foes), two Barriers and a Thread turn it to one's own; unshaped, everyone living in reach, the caster too -
        /// Withering drinks friends as well, Restoration mends the foes as well.</summary>
        private static List<Pawn> Reached(Pawn caster, SpellCircle g, IEnumerable<Pawn> near)
        {
            bool shaped = SpellSculpt.Of(g), aid = SpellSculpt.AidOf(g);
            return near.Where(p => Living(p) && !p.Dead && (!shaped || (aid ? (p == caster || !p.HostileTo(caster)) : (p != caster && p.HostileTo(caster))))).ToList();
        }

        private static IEnumerable<Pawn> Area(IntVec3 at, float r, Map map) => GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>();
        private static IEnumerable<Pawn> Line(Pawn caster, IntVec3 to, Map map, out List<IntVec3> cells)
        {
            cells = GenSight.BresenhamCellsBetween(caster.Position, to).Where(c => c.InBounds(map) && c != caster.Position).ToList();
            var l = cells; return cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().ToList();
        }

        // ------------------------------------------------------------------ the great circle's own runes

        /// <summary>The energy just taken, not yet in the store: what a Restoration further on spends first; what is left
        /// goes into the store at the end. A Restoration that finds too little takes the rest from the store.</summary>
        private static float flow;
        private static readonly Dictionary<int, float> flows = new Dictionary<int, float>();
        /// <summary>One spell's energy lives through all its Life circles: taken in one, it is there for the next; what is
        /// left goes into the store when the spell is over. Kept for each caster, so two spells at once do not mix.</summary>
        public static void BeginSpell(Pawn caster) { if (caster != null) flows[caster.thingIDNumber] = 0f; }
        public static void EndSpell(Pawn caster)
        {
            if (caster == null || !flows.TryGetValue(caster.thingIDNumber, out float f)) return;
            flows.Remove(caster.thingIDNumber);
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core != null && f > 0f) core.AddVita(f);
        }
        /// <summary>A circle of Life in its turn in the queue.</summary>
        public static void CastCircle(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map)
        {
            flows.TryGetValue(caster.thingIDNumber, out flow);
            try { Cast(caster, g, at, power, map); }
            finally { flows[caster.thingIDNumber] = flow; flow = 0f; }
        }

        public static void Cast(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map)
        {
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null || map == null || !at.InBounds(map)) return;
            if (SpellSculpt.Of(g)) power *= SpellSculpt.Keeps;
            float r = Radius(g);
            IntVec3 last = at;
            bool gaveNothing = false;
            foreach (Act act in Read(g, out _))
            {
                IntVec3 where = last;
                if (act.hop)
                {
                    // the leap: the energy goes on at once to the nearest wounded one reached, within the Reach
                    float range = 10f + 10f * act.reach;
                    Pawn to = Reached(caster, g, Area(last, range, map)).Where(Hurt).OrderBy(p => p.Position.DistanceToSquared(last)).FirstOrDefault();
                    if (to != null) { Leap(map, last, to.Position); where = to.Position; }
                }
                if (act.side == RuneKind.Drain) Take(caster, core, g, act, where, r, power, map);
                else gaveNothing |= !Give(caster, core, g, act, where, r, power, map);
                last = where;
            }
            if (gaveNothing) MoteMaker.ThrowText(at.ToVector3Shifted(), map, "No life to give", Necrotic, 3f);
        }

        private static void Leap(Map map, IntVec3 a, IntVec3 b)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt((b - a).LengthHorizontal), 2, 30);
            for (int i = 0; i <= n; i++) Wisp(Vector3.Lerp(a.ToVector3Shifted(), b.ToVector3Shifted(), i / (float)n), map);
        }

        /// <summary>Round small circles: the friends the spell touched are mended - out of the store.</summary>
        public static void Wrap(Pawn caster, SpellCircle g, IntVec3 at, float radius, float power, Map map)
        {
            flows.TryGetValue(caster.thingIDNumber, out flow);
            try { WrapNow(caster, g, at, radius, power, map); }
            finally { if (flows.ContainsKey(caster.thingIDNumber)) flows[caster.thingIDNumber] = flow; flow = 0f; }
        }

        private static void WrapNow(Pawn caster, SpellCircle g, IntVec3 at, float radius, float power, Map map)
        {
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null) return;
            var friends = Area(at, radius, map).Where(p => Living(p) && Friend(p, caster)).ToList();
            float heal = 6f * power;
            float need = friends.Count(Hurt) * heal * 0.6f;
            if (need <= 0f) return;
            float f = Pay(core, need);
            if (f <= 0f) { MoteMaker.ThrowText(at.ToVector3Shifted(), map, "No life to give", Necrotic, 3f); return; }
            foreach (Pawn p in friends) { HealInjuries(p, heal * f); AddRegen(p, 0.1f * f); }
        }

        // ------------------------------------------------------------------ taking: Withering, the attack of Life

        private static void Take(Pawn caster, Hediff_ArcaneCore core, SpellCircle g, Act act, IntVec3 at, float r, float power, Map map)
        {
            float got = 0f;
            switch (act.verb)
            {
                case RuneKind.Release:   // a burst of withering
                    got += Wither(caster, Reached(caster, g, Area(at, r, map)), 6f * power, map) + Plants(at, r, map, true) + Corpses(at, r, map);
                    FleckMaker.Static(at, map, FleckDefOf.PsycastAreaEffect, r);
                    break;
                case RuneKind.Beam:      // a withering beam
                    got += Wither(caster, Reached(caster, g, Line(caster, at, map, out var cells).Where(p => p != caster)), 7f * power, map);
                    foreach (IntVec3 c in cells) if (Rand.Chance(0.5f)) Wisp(c.ToVector3Shifted(), map);
                    break;
                case RuneKind.Hold:      // vines that drink while they hold
                    foreach (Pawn p in Reached(caster, g, Area(at, r, map))) MagicFx.Give(p, H("Avir_Entangled"), Mathf.RoundToInt(360f * power));
                    got += Wither(caster, Reached(caster, g, Area(at, r, map)), 3f * power, map);
                    break;
                case RuneKind.Growth:    // from the green only: no creature is touched
                    got += Plants(at, r, map, false);
                    break;
                case RuneKind.Purge:     // poison and withering drawn out of the reached - into the store
                    foreach (Pawn p in Reached(caster, g, Area(at, r, map)))
                        foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.FoodPoisoning || h.def == HediffDefOf.ToxicBuildup).ToList())
                        { float d = Mathf.Min(h.Severity, 0.3f * power); h.Severity -= d; got += 20f * d; if (h.Severity <= 0.01f && p.health.hediffSet.hediffs.Contains(h)) p.health.RemoveHediff(h); }
                    break;
                default:                 // a bare Withering: the point itself, strong and narrow
                    float pr = Mathf.Max(1.5f, r - 1f);
                    got += Wither(caster, Reached(caster, g, Area(at, pr, map)), 8f * power, map) + Plants(at, pr, map, true) + Corpses(at, pr, map);
                    break;
            }
            flow += got;
        }

        private static float Wither(Pawn caster, IEnumerable<Pawn> who, float dmg, Map map)
        {
            float got = 0f;
            foreach (Pawn p in who.ToList())
            {
                if (!Living(p) || p.Dead) continue;
                p.TakeDamage(new DamageInfo(WitherDmg, dmg, 2f, -1f, caster));
                got += dmg; Wisp(p.DrawPos, map);
            }
            return got;
        }

        private static float Plants(IntVec3 at, float r, Map map, bool deadGround)
        {
            float got = 0f; var temp = MapComponent_TempTerrain.Of(map);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map)).ToList())
            {
                Plant pl = c.GetPlant(map);
                if (pl == null || pl.def.plant == null || map.zoneManager.ZoneAt(c) is Zone_Growing) continue;
                got += (pl.def.plant.IsTree ? 6f : pl.def.plant.harvestYield > 0f ? 2f : 0.6f) * Mathf.Max(0.3f, pl.Growth);
                pl.Destroy();
                if (deadGround && DeadGround != null && map.terrainGrid.TerrainAt(c).fertility > 0f) temp?.Set(c, DeadGround, GenDate.TicksPerDay * 3);
                if (Rand.Chance(0.4f)) Wisp(c.ToVector3Shifted(), map);
            }
            return got;
        }

        private static float Corpses(IntVec3 at, float r, Map map)
        {
            float got = 0f;
            foreach (Corpse k in GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Corpse>().ToList())
            {
                CompRottable rot = k.GetComp<CompRottable>();
                if (k.InnerPawn != null && Living(k.InnerPawn) && rot != null && rot.Stage == RotStage.Fresh) { rot.RotProgress = 1e7f; got += 8f; Wisp(k.DrawPos, map); }
            }
            return got;
        }

        private static void Wisp(Vector3 v, Map map) => FleckMaker.ThrowDustPuffThick(v, map, 1f, new Color(Necrotic.r, Necrotic.g, Necrotic.b, 0.55f));

        // ------------------------------------------------------------------ giving: Restoration, out of the flow, then the store

        /// <summary>Takes what is there of `need` - from the energy just taken first, then from the store; the share paid.</summary>
        private static float Pay(Hediff_ArcaneCore core, float need)
        {
            if (need <= 0f) return 1f;
            float fromFlow = Mathf.Min(flow, need); flow -= fromFlow;
            float rest = need - fromFlow;
            float fromStore = Mathf.Min(core.vita, rest); core.vita -= fromStore;
            return Mathf.Clamp01((fromFlow + fromStore) / need);
        }

        private static bool Give(Pawn caster, Hediff_ArcaneCore core, SpellCircle g, Act act, IntVec3 at, float r, float power, Map map)
        {
            List<Pawn> who; float per = 12f, regen = 0.25f;
            List<IntVec3> cells = null;
            switch (act.verb)
            {
                case RuneKind.Release: who = Reached(caster, g, Area(at, r, map)); per = 9f; regen = 0.15f; break;
                case RuneKind.Beam: who = Reached(caster, g, Line(caster, at, map, out cells)); per = 10f; regen = 0.2f; break;
                case RuneKind.Hold: who = Reached(caster, g, Area(at, r, map)); per = 6f; regen = 0.4f; break;     // a cocoon: held still, mended deep
                case RuneKind.Growth: who = Reached(caster, g, Area(at, r, map)); per = 3f; regen = 0.15f; break;
                case RuneKind.Purge: who = Reached(caster, g, Area(at, r, map)); per = 0f; regen = 0f; break;
                default: who = Reached(caster, g, Area(at, Mathf.Max(1.5f, r - 1f), map)); break;
            }
            var plants = act.verb == RuneKind.Growth ? GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map)).Select(c => c.GetPlant(map)).Where(p => p != null && p.LifeStage == PlantLifeStage.Growing).ToList() : new List<Plant>();
            var bad = act.verb == RuneKind.Purge ? who.SelectMany(p => p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.FoodPoisoning || h.def == HediffDefOf.ToxicBuildup || h.def.defName == "Avir_Withered" && !(h is Hediff_Injury)).Select(h => new { p, h })).ToList() : null;
            float need = who.Sum(p => (Hurt(p) ? per * power * 0.6f : 0f) + regen * 4f) + plants.Count * 0.3f + (bad?.Count ?? 0) * 3f;
            if (need <= 0f) return true;
            float f = Pay(core, need);
            if (f <= 0f) return false;
            foreach (Pawn p in who)
            {
                if (per > 0f) HealInjuries(p, per * power * f);
                AddRegen(p, regen * power * f);
                if (act.verb == RuneKind.Hold) MagicFx.Give(p, H("Avir_Entangled"), Mathf.RoundToInt(300f * f));
                if (per > 0f) FleckMaker.ThrowMetaIcon(p.Position, map, FleckDefOf.HealingCross);
            }
            foreach (Plant pl in plants) pl.Growth = Mathf.Min(1f, pl.Growth + 0.15f * power * f);
            if (bad != null) foreach (var x in bad) { x.h.Severity -= 0.3f * power * f; if (x.h.Severity <= 0.01f && x.p.health.hediffSet.hediffs.Contains(x.h)) x.p.health.RemoveHediff(x.h); }
            if (cells != null) foreach (IntVec3 c in cells) if (Rand.Chance(0.4f)) Wisp(c.ToVector3Shifted(), map);
            return true;
        }

        // ------------------------------------------------------------------ before the cast: is there anything to give from?

        /// <summary>Estimates a spell of Life before it is cast (the author's chain rule): enough from its Witherings for
        /// its Restorations - cast; too little but the store holds the rest - it takes from the store; nothing to wither
        /// here and nothing in the store while it must give - not let go at all (no Arcana spent).</summary>
        public static string Blocked(Pawn caster, RitualSchema s, LocalTargetInfo target)
        {
            if (s?.weave == null || caster?.Map == null) return null;
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null) return null;
            var lifes = Links(s.weave).ToList();
            bool wraps = Circles(s.weave).Any(g => g.inner.Any(k => k.element != null));
            if (lifes.Count == 0 && !wraps) return null;
            bool gives = lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Restore)) || wraps;
            if (!gives) return null;
            bool takes = lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Drain));
            if (core.vita > 0.5f) return null;
            if (takes)
            {
                Map map = caster.Map; IntVec3 at = target.Cell;
                bool any = GenRadial.RadialCellsAround(at, 6f, true).Any(c => c.InBounds(map) && (c.GetPlant(map) != null || c.GetThingList(map).Any(t => (t is Pawn p && Living(p) && p != caster) || t is Corpse)));
                if (any) return null;
            }
            return "the store of life is empty and there is nothing here to wither - Life cannot give what it has not taken.";
        }

        // the author's three lines; one is chosen by the spell, so the same spell always says the same
        private static readonly string[] Quips =
        {
            "So, does it eat or does it get eaten?",
            "Life isn't a vegetarian, you know. Well, it could be, but not here.",
            "Oh, that's some good waste of logic there! Did you forget how life works? Go and read about it before you realise you're too are alive.",
        };
        private static string Quip(SpellCircle root) => Quips[(int)((uint)root.All().Sum(c => c.OwnRunes.Count() * 7 + (c.element?.shortHash ?? 0)) % Quips.Length)];

        /// <summary>Arlie on the grammar of Life: a verb or a beam not tied to Withering or Restoration.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Warnings(SpellCircle root)
        {
            if (root == null) yield break;
            var untied = Links(root).SelectMany(g => { Read(g, out var u); return u; }).Distinct().ToList();
            if (untied.Count > 0)
                yield return new KeyValuePair<string, string>("Untied verb of Life", "Arlie: \"" + Quip(root) + "\"\n"
                    + string.Join(", ", untied.Select(k => k.ToString())) + " here is not tied to Withering or Restoration, and does nothing. In Life every verb and every beam is written with one of the two: Release + Withering, Beam + Restoration.");
        }

        // ------------------------------------------------------------------ a lost hold: death strikes the mage, life overgrows the place

        public static void Slip(Pawn caster, SpellCircle root)
        {
            var lifes = Links(root).ToList();
            bool wraps = Circles(root).Any(g => g.inner.Any(k => k.element != null));
            if ((lifes.Count == 0 && !wraps) || caster == null || !caster.Spawned) return;
            bool death = lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Drain));
            bool life = wraps || lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Restore));
            Map map = caster.Map;
            if (death)
            {
                // the Withering turns on the one who held it: the mage dries, and the store spills away
                caster.TakeDamage(new DamageInfo(WitherDmg, 10f, 2f, -1f, caster));
                Hediff_ArcaneCore core = Mages.Core(caster); if (core != null) core.vita *= 0.5f;
                for (int i = 0; i < 5; i++) Wisp(caster.DrawPos + new Vector3(Rand.Range(-1f, 1f), 0f, Rand.Range(-1f, 1f)), map);
                Messages.Message(caster.LabelShortCap + " lost the hold on a Withering: it drank from them.", caster, MessageTypeDefOf.NegativeEvent, false);
            }
            if (life)
            {
                // the Restoration breaks loose: the place overgrows - thick growth, vines on everyone, bark on the mage
                ThingDef grass = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TallGrass") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Grass");
                foreach (IntVec3 c in GenRadial.RadialCellsAround(caster.Position, 4.5f, true).Where(c => c.InBounds(map)))
                {
                    Plant pl = c.GetPlant(map);
                    if (pl != null) pl.Growth = 1f;
                    else if (grass != null && Rand.Chance(0.5f) && c.GetEdifice(map) == null && map.terrainGrid.TerrainAt(c).fertility > 0f && c.GetFirstItem(map) == null)
                    { Plant np = (Plant)GenSpawn.Spawn(grass, c, map); np.Growth = Rand.Range(0.6f, 1f); }
                }
                foreach (Pawn p in Area(caster.Position, 4.5f, map).Where(Living).ToList()) MagicFx.Give(p, H("Avir_Entangled"), 360);
                HediffDef bark = H("Avir_LifeOverflow");
                if (bark != null) { Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(bark) ?? caster.health.AddHediff(bark); h.Severity = Mathf.Min(1f, h.Severity + 0.3f); }
                Messages.Message(caster.LabelShortCap + " lost the hold on a Restoration: the place overgrows.", caster, MessageTypeDefOf.NegativeEvent, false);
            }
        }

        // ------------------------------------------------------------------ helpers

        public static bool Hurt(Pawn p) => p.health.hediffSet.hediffs.Any(h => h is Hediff_Injury i && !i.IsPermanent()) || p.health.hediffSet.BleedRateTotal > 0f;

        public static void HealInjuries(Pawn p, float amount)
        {
            float left = amount;
            foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => !i.IsPermanent()).OrderByDescending(i => i.Bleeding ? 1 : 0).ThenByDescending(i => i.Severity).ToList())
            {
                if (left <= 0f) break;
                float h = Mathf.Min(left, inj.Severity); inj.Heal(h); left -= h;
            }
        }

        /// <summary>The regrowth deepens with every mending (Avir_LifeRegen; like the Kharassian vapour, only quicker).</summary>
        public static void AddRegen(Pawn p, float amount)
        {
            if (Regen == null || amount <= 0f) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Regen) ?? p.health.AddHediff(Regen);
            h.Severity = Mathf.Min(1.5f, (h.Severity <= 0.011f ? 0f : h.Severity) + amount);
        }

        // ------------------------------------------------------------------ the store spilling over, and its slow fading

        /// <summary>Overflow: what the store cannot hold breaks out round the mage - the grass bursts, every wound near
        /// closes a little (the foes' too), and bark and moss creep over the mage's hands.</summary>
        public static void Overflow(Pawn caster, float excess)
        {
            if (caster == null || !caster.Spawned || excess <= 0.5f) return;
            Map map = caster.Map;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(caster.Position, 3.5f, true).Where(c => c.InBounds(map)))
            {
                Plant pl = c.GetPlant(map);
                if (pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.02f * excess);
            }
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(caster.Position, map, 3.5f, true).OfType<Pawn>().Where(Living).ToList())
                HealInjuries(p, 0.5f * excess);
            HediffDef bark = H("Avir_LifeOverflow");
            if (bark != null)
            {
                Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(bark) ?? caster.health.AddHediff(bark);
                h.Severity = Mathf.Min(1f, h.Severity + 0.02f * excess);
            }
            for (int i = 0; i < 6; i++) Wisp(caster.DrawPos + new Vector3(Rand.Range(-2f, 2f), 0f, Rand.Range(-2f, 2f)), map);
            MoteMaker.ThrowText(caster.DrawPos, map, "Overflow", Necrotic, 3f);
        }

        /// <summary>The store fades slowly, and what fades goes out of the mage: their own wounds close, the plants by
        /// them green, and above half the store their body fights sickness quicker (Avir_VitaFlow).</summary>
        public static void Fade(Hediff_ArcaneCore core, float hours)
        {
            Pawn p = core.pawn;
            HediffDef flowDef = H("Avir_VitaFlow");
            Hediff flow = flowDef != null ? p.health.hediffSet.GetFirstHediffOfDef(flowDef) : null;
            if (core.vita <= 0.01f) { core.vita = 0f; if (flow != null) p.health.RemoveHediff(flow); return; }
            float gone = core.vita * 0.02f * hours;
            core.vita -= gone;
            // life not given anywhere grows on the one who holds it (the druids of the Grove, eating the chimeras): above
            // three quarters of the store, bark and moss creep over the mage
            float fill = core.vita / Mathf.Max(1f, core.VitaMax);
            if (fill > 0.75f)
            {
                HediffDef bark = H("Avir_LifeOverflow");
                if (bark != null) { Hediff h = p.health.hediffSet.GetFirstHediffOfDef(bark) ?? p.health.AddHediff(bark); h.Severity = Mathf.Min(1f, h.Severity + 0.16f * (fill - 0.75f) * hours); }
            }
            if (Living(p)) HealInjuries(p, gone * 0.5f);
            if (p.Spawned)
                foreach (IntVec3 c in GenRadial.RadialCellsAround(p.Position, 2.9f, true).Where(c => c.InBounds(p.Map)).InRandomOrder().Take(3))
                {
                    Plant pl = c.GetPlant(p.Map);
                    if (pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.01f * gone);
                }
            if (flowDef != null)
            {
                if (flow == null) flow = p.health.AddHediff(flowDef);
                flow.Severity = Mathf.Clamp(core.vita / Mathf.Max(1f, core.VitaMax), 0.01f, 1f);
            }
        }
    }

    // ================================================================== the regrowth (after the Kharassian sahrumet, quicker)

    public class HediffCompProperties_LifeRegen : HediffCompProperties
    {
        public float daysPerLayer = 1.5f, daysPerOrgan = 15f, daysPerChronicCure = 5f, scarHealFractionPerDay = 0.35f;
        public float scarsFrom = 0.5f, saturatedFrom = 1f, fadePerDay = 0.25f;
        public HediffCompProperties_LifeRegen() { compClass = typeof(HediffComp_LifeRegen); }
    }

    /// <summary>What Life's mending leaves behind. Its depth grows with every mending and fades a quarter a day. Light, it
    /// quickens healing (its stages); deep (0.5+), it walks scars down to nothing; saturated (1+), it brings back what was
    /// lost - an outer part a layer at a time (the parent before its children), the organs all at once on a long clock,
    /// chronic ailments on a shorter one. The logic follows Kharassian.HediffComp_LimbRegrowth, kept here in Avir (the
    /// Kharassians will lean on Avir, not the other way round).</summary>
    public class HediffComp_LifeRegen : HediffComp
    {
        private float layerProgress, organProgress, chronicProgress;
        private int check = Rand.Range(0, 250);
        public HediffCompProperties_LifeRegen Props => (HediffCompProperties_LifeRegen)props;

        public override string CompTipStringExtra => parent.Severity >= Props.saturatedFrom
            ? "Next part: " + (layerProgress * 100f).ToString("F0") + "%\nOrgans: " + (organProgress * 100f).ToString("F0") + "%\nChronic ailments: " + (chronicProgress * 100f).ToString("F0") + "%"
            : parent.Severity >= Props.scarsFrom ? "Old scars are fading." : null;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            check -= delta;
            if (check > 0) return;
            int dt = 250 - check; check = 250;
            Pawn p = parent.pawn;
            if (p == null || p.Dead) return;
            float days = dt / (float)GenDate.TicksPerDay;
            severityAdjustment -= Props.fadePerDay * days;
            float sev = parent.Severity;
            if (sev >= Props.scarsFrom) MendScars(p, days * (sev >= Props.saturatedFrom ? 1.5f : 1f));
            if (sev < Props.saturatedFrom) return;
            layerProgress += days / Props.daysPerLayer;
            organProgress += days / Props.daysPerOrgan;
            chronicProgress += days / Props.daysPerChronicCure;
            if (layerProgress >= 1f)
            {
                var parts = Missing(p, x => x.depth != BodyPartDepth.Inside || x.def.IsSolid(x, p.health.hediffSet.hediffs));
                if (parts.Count > 0) { layerProgress = 0f; foreach (BodyPartRecord x in parts) RestoreBare(p, x); Note(p, "grown back " + string.Join(", ", parts.Select(x => x.Label))); }
                else layerProgress = 1f;
            }
            if (organProgress >= 1f)
            {
                var organs = Missing(p, x => x.depth == BodyPartDepth.Inside && !x.def.IsSolid(x, p.health.hediffSet.hediffs));
                if (organs.Count > 0) { organProgress = 0f; foreach (BodyPartRecord x in organs) RestoreBare(p, x); Note(p, "grown back the organs"); }
                else organProgress = 1f;
            }
            if (chronicProgress >= 1f)
            {
                var chronic = p.health.hediffSet.hediffs.Where(h => h.def.chronic).ToList();
                if (chronic.Count > 0) { chronicProgress = 0f; foreach (Hediff h in chronic) p.health.RemoveHediff(h); Note(p, "been rid of " + string.Join(", ", chronic.Select(h => h.LabelBase))); }
                else chronicProgress = 1f;
            }
        }

        private void MendScars(Pawn p, float days)
        {
            foreach (Hediff_Injury scar in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.IsPermanent() && i.Part != null).ToList())
            {
                scar.Severity -= scar.Part.def.GetMaxHealth(p) * Props.scarHealFractionPerDay * days;
                if (scar.Severity <= 0.001f && p.health.hediffSet.hediffs.Contains(scar)) p.health.RemoveHediff(scar);
            }
        }

        private static List<BodyPartRecord> Missing(Pawn p, System.Func<BodyPartRecord, bool> accept)
        {
            var res = new List<BodyPartRecord>();
            foreach (Hediff_MissingPart m in p.health.hediffSet.hediffs.OfType<Hediff_MissingPart>())
            {
                if (m.Part == null || !accept(m.Part) || res.Contains(m.Part)) continue;
                bool ancestorGone = false;
                for (BodyPartRecord a = m.Part.parent; a != null; a = a.parent) if (p.health.hediffSet.PartIsMissing(a)) { ancestorGone = true; break; }
                if (!ancestorGone) res.Add(m.Part);
            }
            return res;
        }

        private static void RestoreBare(Pawn p, BodyPartRecord part)
        {
            p.health.RestorePart(part);
            if (part.parts == null) return;
            foreach (BodyPartRecord child in part.parts)
            {
                if (p.health.hediffSet.PartIsMissing(child)) continue;
                var miss = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, p, child);
                miss.IsFresh = false;
                p.health.AddHediff(miss, child);
            }
        }

        private static void Note(Pawn p, string what)
        {
            if (PawnUtility.ShouldSendNotificationAbout(p)) Messages.Message(p.LabelShortCap + " has " + what + ": life's regrowth.", p, MessageTypeDefOf.PositiveEvent, false);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref layerProgress, "lifeLayer");
            Scribe_Values.Look(ref organProgress, "lifeOrgan");
            Scribe_Values.Look(ref chronicProgress, "lifeChronic");
        }
    }

    // ================================================================== the store on the screen

    public class Gizmo_Vita : Gizmo
    {
        public Hediff_ArcaneCore core;
        private static readonly Texture2D Fill = SolidColorMaterials.NewSolidColorTexture(new Color(0.55f, 0.72f, 0.46f));
        private static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(new Color(0.08f, 0.1f, 0.08f));
        private static Texture2D art; private static bool looked;
        public Gizmo_Vita() { Order = -100.5f; }
        public override float GetWidth(float maxWidth) => 150f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            if (!looked) { looked = true; art = ContentFinder<Texture2D>.Get("UI/Magic/Scale_Life", false); }
            Rect r = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect bar;
            if (art != null)
            {
                // the plaque has the Arcana plaque's layout: a title band above, a window for the bar below (13-87% across, 59-81% down)
                bar = new Rect(r.x + r.width * 0.13f, r.y + r.height * 0.59f, r.width * 0.74f, r.height * 0.22f);
                Widgets.FillableBar(bar, Mathf.Clamp01(core.vita / Mathf.Max(1f, core.VitaMax)), Fill, Empty, false);
                GUI.DrawTexture(r, art);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.9f, 0.95f, 0.85f);
                Widgets.Label(new Rect(r.x, r.y + r.height * 0.33f, r.width, r.height * 0.2f), "Life");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                Widgets.DrawWindowBackground(r);
                Rect inner = r.ContractedBy(5f);
                Text.Font = GameFont.Small;
                GUI.color = LifeMagic.Necrotic; Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), "Life"); GUI.color = Color.white;
                bar = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
                Widgets.FillableBar(bar, Mathf.Clamp01(core.vita / Mathf.Max(1f, core.VitaMax)), Fill, Empty, false);
            }
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, Mathf.FloorToInt(core.vita) + " / " + Mathf.FloorToInt(core.VitaMax));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, "The store of life: what Withering took, ready to be given. Life makes nothing - it only moves (Symbiosis): no spell of Life gives anything with the store empty. It fades by 2% an hour, and what fades mends the mage and greens the plants by them. Past the top it spills over (Overflow).");
            return new GizmoResult(GizmoState.Clear);
        }
    }

    // ================================================================== developer mode

    public static class LifeDev
    {
        private const string Cat = "Avir magic";

        [DebugAction(Cat, "Life: store full / empty (toggle)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Store(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c == null) return; c.vita = c.vita > 1f ? 0f : c.VitaMax; Messages.Message(p.LabelShort + ": store of life " + c.vita.ToString("0") + " / " + c.VitaMax.ToString("0"), MessageTypeDefOf.NeutralEvent, false); }

        [DebugAction(Cat, "Life: overflow now (+50 past the top)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Over(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c == null) return; c.vita = c.VitaMax; c.AddVita(50f); }

        [DebugAction(Cat, "Life: regrowth saturated (1.2)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Sat(Pawn p) => LifeMagic.AddRegen(p, 1.2f);

        [DebugAction(Cat, "Life: cut off a hand and scar (regrowth test)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Cut(Pawn p)
        {
            BodyPartRecord hand = p.health.hediffSet.GetNotMissingParts().FirstOrDefault(x => x.def.defName == "Hand");
            if (hand != null) { var m = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, p, hand); m.IsFresh = false; p.health.AddHediff(m, hand); }
        }

        [DebugAction(Cat, "Life: test spells into this caster's book", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Tests(Pawn p)
        {
            Hediff_ArcaneCore core = Mages.Core(p);
            if (core == null) return;
            RuneDef R(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);
            RitualSchema Spell(string name, string life, params string[] smalls)
            {
                var root = new SpellCircle { binding = true };
                var g = new SpellCircle { element = R("Life") };
                foreach (string sec in life.Split('|'))
                {
                    var runes = sec.Split(',').Select(x => R(x.Trim())).Where(x => x != null).ToList();
                    if (runes.Count > 0) g.sections.Add(new RitualRing { runes = runes, slots = Mathf.Max(1, runes.Count) });
                }
                foreach (string c in smalls)
                {
                    string[] parts = c.Split(':');
                    var k = new SpellCircle { element = R(parts[0]) };
                    foreach (string sec in parts[1].Split('|')) { var runes = sec.Split(',').Select(x => R(x.Trim())).Where(x => x != null).ToList(); k.sections.Add(new RitualRing { runes = runes, slots = Mathf.Max(1, runes.Count) }); }
                    g.inner.Add(k);
                }
                root.inner.Add(g);
                return new RitualSchema { name = name, weave = root };
            }
            var list = new List<RitualSchema>
            {
                Spell("TEST Life: Withering at the point", "Drain"),
                Spell("TEST Life: burst of withering, foes only", "Release,Drain,Breadth|Barrier,Barrier"),
                Spell("TEST Life: withering beam (hits everyone on the line)", "Beam,Drain"),
                Spell("TEST Life: drink the foes, mend own (leap)", "Release,Drain,Breadth|Reach,Thread,Release,Restore"),
                Spell("TEST Life: burst of mending, own only", "Release,Restore,Breadth|Barrier,Barrier,Thread"),
                Spell("TEST Life: mending beam", "Beam,Restore"),
                Spell("TEST Life: cocoon", "Hold,Restore"),
                Spell("TEST Life: draw from the green only", "Growth,Drain,Breadth"),
                Spell("TEST Life: untied Release (Arlie)", "Release,Breadth"),
                Spell("TEST Life round fire (mends whom it touches)", "", "Fire:Release"),
                Spell("TEST Life box: beam drinks the foes, then a burst mends own", "", "None:Beam,Drain|Barrier,Barrier", "None:Release,Restore,Breadth|Barrier,Barrier,Thread"),
                Spell("TEST Life box: fire inside first, then Life's own runes", "Release,Drain,Breadth|Barrier,Barrier", "Fire:Release"),
            };
            foreach (RitualSchema s in list) core.Book.spells.Add(s);
            Messages.Message(list.Count + " test spells of Life written into " + p.LabelShort + "'s book.", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
