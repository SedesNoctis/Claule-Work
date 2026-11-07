using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Partial meetings (the author, the plan §98, Fix183): a Focus written beside a verb in one of the two
    /// circles of a meeting narrows the meeting, for that verb, to one of its cases - which, the verb decides ("by its
    /// place"). A partial is narrow: strong at its one thing, and it does not do the rest. Dust's: Thunder (Release), the sand spout (Lift), the dust veil (Hold).</summary>
    public enum PartialKind { None, Thunder, Whirl, Veil }

    public static class Partials
    {
        public static readonly Color ThunderColor = new Color(0.62f, 0.42f, 1f);
        public static readonly Color SandColor = new Color(0.86f, 0.72f, 0.48f);

        /// <summary>Sand spout (Fix184): a walking whirl of sand. The wind is the wind magic's own (a round gust renewed each
        /// second), so wards, lee, trees and the readout all hold for it.</summary>
        public static void Spout(Pawn caster, Map map, IntVec3 at, float spread, float power)
        {
            if (map == null || !at.IsValid) return;
            int ticks = Mathf.Clamp(Mathf.RoundToInt(600f * power), 300, 1800);
            map.GetComponent<MapComponent_Partials>()?.spouts.Add(new MapComponent_Partials.Spout { center = at.ToVector3Shifted(), dir = Quaternion.AngleAxis(Rand.Range(0f, 360f), Vector3.up) * Vector3.forward, radius = Mathf.Clamp(spread, 2f, 4f), owner = caster, power = power, until = Find.TickManager.TicksGame + ticks, sculpt = SpellSculpt.Active ? (SpellSculpt.Allies ? 2 : 1) : 0 });
            for (int i = 0; i < 6; i++) FleckMaker.ThrowDustPuffThick((at.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * 1.5f), map, 2f, SandColor);
        }

        public static float SpoutStrength(float power, Map map) =>
            Mathf.Min(MapComponent_MagicWind.AirMax, 34f * power * (1f + 0.2f * Spells.FlowTierAny(map)) * AtmosphereBridge.Scale);

        /// <summary>Dust veil (Fix184): a standing curtain of dust that fills itself again till its time is over.</summary>
        public static void Veil(Pawn caster, Map map, List<IntVec3> cells, float power, int ticks)
        {
            if (map == null || cells.Count == 0) return;
            var v = new MapComponent_Partials.VeilField { cells = cells.Where(c => c.InBounds(map) && !c.Filled(map)).ToList(), owner = caster, power = power, until = Find.TickManager.TicksGame + Mathf.Max(600, ticks) };
            map.GetComponent<MapComponent_Partials>()?.veils.Add(v);
            foreach (IntVec3 c in v.cells) { FireSmoke.Puff(c, map, 1.5f * power); if (Rand.Chance(0.3f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 2.2f, SandColor); }
        }

        /// <summary>The verbs of this circle that have a Focus right beside them (before or after, in the same section).</summary>
        public static HashSet<RuneKind> FocusedVerbs(RitualSchema s)
        {
            var set = new HashSet<RuneKind>();
            if (s?.rings == null) return set;
            foreach (RitualRing ring in s.rings)
            {
                var seq = ring.runes.Where(x => x != null).ToList();
                for (int i = 0; i < seq.Count; i++)
                {
                    if (seq[i].kind != RuneKind.Focus) continue;
                    if (i > 0 && seq[i - 1].role == RuneRole.Verb) set.Add(seq[i - 1].kind);
                    else if (i + 1 < seq.Count && seq[i + 1].role == RuneRole.Verb) set.Add(seq[i + 1].kind);
                }
            }
            return set;
        }

        public static PartialKind Of(ComboKind k, RuneKind verb)
        {
            if (k == ComboKind.Dust && verb == RuneKind.Release) return PartialKind.Thunder;
            if (k == ComboKind.Dust && verb == RuneKind.Lift) return PartialKind.Whirl;
            if (k == ComboKind.Dust && verb == RuneKind.Hold) return PartialKind.Veil;
            return PartialKind.None;
        }

        /// <summary>The partials of a whole spell, for the codex.</summary>
        public static IEnumerable<PartialKind> InSpell(SpellCircle root)
        {
            if (root == null) yield break;
            var pairs = Combos.Pairs(root, true);
            foreach (var kv in pairs)
                foreach (RuneKind v in FocusedVerbs(new RitualSchema { rings = kv.Key.sections }))
                {
                    PartialKind pk = Of(kv.Value, v);
                    if (pk != PartialKind.None) yield return pk;
                }
        }

        public static string Text(PartialKind pk)
        {
            switch (pk)
            {
                case PartialKind.Thunder: return "Thunder (dust with a Focus beside Release): not a blast of grit but a blast of sound. It deafens and stuns all within reach, tears at the ears (its own wound), shatters glass, batters doors and light buildings, and sends animals fleeing. Among many walls it echoes - every solid wall near makes it stronger; in the open it is weaker. It does not grind, blind or bury - a partial meeting does its one thing only.";
                case PartialKind.Whirl: return "Sand spout (dust with a Focus beside Lift): nobody is lifted by the hand - a pillar of sand rises at the point and walks. It wanders, leaning towards the nearest foe; its wind spins, fells and throws, light things are carried off, and in its heart the sand flays the skin and leaves a trail of grit no one sees through. It lasts some seconds, longer with more power; a ward of the air holds most of it off, a ward of the storm all.";
                case PartialKind.Veil: return "Dust veil (dust with a Focus beside Hold): a heavy curtain of dust stands over the place and keeps itself - what thins, it fills again. No one sees through it: shots across it go astray, turrets lose their mark. It harms no one. A strong wind (20 m/s and more) tears it - while the wind blows, it does not fill; when the wind falls, the veil gathers again, if its time is not over.";
            }
            return null;
        }

        /// <summary>Thunder: a shockwave of sound round a point (Fix183).</summary>
        public static void Thunder(Pawn caster, Map map, List<IntVec3> cells, IntVec3 at, float power)
        {
            if (map == null || cells.Count == 0) return;
            float radius = Mathf.Max(2.5f, cells.Max(c => c.DistanceTo(at)) + 0.5f);
            // the echo: solid walls round the blast (not rooms - walls) give it back, up to +80%
            int walls = GenRadial.RadialCellsAround(at, radius + 2f, true).Count(c => c.InBounds(map) && c.GetEdifice(map) is Building b && b.def.Fillage == FillCategory.Full);
            float echo = 1f + Mathf.Min(0.8f, walls / 30f);
            float pw = power * echo;
            // the sight: violet rings spreading out, a flash
            Vector3 mid = at.ToVector3Shifted();
            for (int i = 0; i < 3; i++) MapComponent_SpellSprites.PutTintedGrowing(map, "Wave/Ripple", ThunderColor, mid, radius * (0.6f + 0.25f * i), 30 + 12 * i, 1.8f);
            FleckMaker.Static(at, map, FleckDefOf.PsycastAreaEffect, radius * 1.4f);
            FleckMaker.ThrowLightningGlow(mid, map, radius);
            if (echo > 1.2f) MoteMaker.ThrowText(mid, map, "echo x" + echo.ToString("0.0"), ThunderColor, 2.5f);
            HediffDef deaf = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Deafened");
            DamageDef sonic = DefDatabase<DamageDef>.GetNamedSilentFail("Avir_Sonic");
            foreach (IntVec3 c in cells)
            {
                if (!c.InBounds(map)) continue;
                float near = 1f - 0.5f * c.DistanceTo(at) / radius;
                foreach (Thing t in c.GetThingList(map).ToList())
                {
                    if (t is Pawn p)
                    {
                        // who is struck - only the spell's shaping decides; unshaped, its own mage too (Fix189)
                        if (p.Dead || !MagicFx.Foe(p, caster)) continue;
                        float q = pw * near;
                        if (!p.RaceProps.IsMechanoid)
                        {
                            MagicFx.Stun(p, Mathf.RoundToInt(90f * q), caster);
                            if (deaf != null) MagicFx.Give(p, deaf, Mathf.RoundToInt(15000f * q));
                            BodyPartRecord ear = p.health.hediffSet.GetNotMissingParts().Where(b => b.def.tags.Contains(BodyPartTagDefOf.HearingSource)).RandomElementWithFallback();
                            if (sonic != null) p.TakeDamage(new DamageInfo(sonic, 4f * q, 0f, -1f, caster, ear));
                        }
                        else MagicFx.Stun(p, Mathf.RoundToInt(40f * q), caster);   // a machine only shudders
                        // animals flee from it
                        if (p.RaceProps.Animal && !p.Dead && !p.Downed && p.Spawned)
                        {
                            IntVec3 dest = CellFinderLoose.GetFleeDest(p, new List<Thing> { caster ?? (Thing)p }, 22f);
                            if (dest.IsValid) p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Flee, dest, caster), JobCondition.InterruptForced);
                        }
                        continue;
                    }
                    if (t is Building b && b.def.useHitPoints && !(b.def.building?.isNaturalRock ?? false))
                    {
                        // glass shatters, doors are battered, light buildings shake apart; stone walls stand
                        bool glass = b.def.defName.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0 || b.def.defName.IndexOf("Window", System.StringComparison.OrdinalIgnoreCase) >= 0 || (b.Stuff?.defName.Contains("Glass") ?? false);
                        float dmg = glass ? 60f : b is Building_Door ? 18f : b.MaxHitPoints <= 120 ? 22f : b.MaxHitPoints <= 300 ? 6f : 0f;
                        if (dmg > 0f) b.TakeDamage(new DamageInfo(DamageDefOf.Blunt, dmg * pw * near, 0f, -1f, caster));
                    }
                }
            }
            // animals farther off take fright too
            foreach (Pawn a in map.mapPawns.AllPawnsSpawned.Where(a => a.RaceProps.Animal && !a.Downed && a.Position.InHorDistOf(at, radius * 2.5f) && !a.Position.InHorDistOf(at, radius)).ToList())
            {
                if (!Rand.Chance(0.6f)) continue;
                IntVec3 dest = CellFinderLoose.GetFleeDest(a, new List<Thing> { caster ?? (Thing)a }, 18f);
                if (dest.IsValid) a.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Flee, dest, caster), JobCondition.InterruptForced);
            }
        }
    }

    /// <summary>The lasting partials (Fix184): sand spouts that walk, dust veils that keep themselves. Short-lived - not saved.</summary>
    public class MapComponent_Partials : MapComponent
    {
        public class Spout { public Vector3 center, dir; public float radius, power; public Pawn owner; public int until, sculpt; }
        public class VeilField { public List<IntVec3> cells; public float power; public Pawn owner; public int until; }
        public readonly List<Spout> spouts = new List<Spout>();
        public readonly List<VeilField> veils = new List<VeilField>();
        public MapComponent_Partials(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (spouts.Count == 0 && veils.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            spouts.RemoveAll(x => x.until <= now);
            veils.RemoveAll(x => x.until <= now || x.cells.Count == 0);
            if (now % 30 == 0) foreach (Spout sp in spouts) Flay(sp, now);
            if (now % 60 != 0) return;
            foreach (Spout sp in spouts) Walk(sp);
            foreach (VeilField v in veils) Keep(v);
        }

        private void Walk(Spout sp)
        {
            // it leans towards the nearest foe in reach, else it wanders
            IntVec3 here = sp.center.ToIntVec3();
            Pawn lure = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && p.Position.InHorDistOf(here, 12f) && MagicFx.Foe(p, sp.owner) && sp.owner != null && p.HostileTo(sp.owner)).OrderBy(p => p.Position.DistanceToSquared(here)).FirstOrDefault();
            SecondFields.Wander(map, ref sp.center, ref sp.dir, 0.9f, lure?.Position ?? IntVec3.Invalid);
            float s = Partials.SpoutStrength(sp.power, map);
            var g = MapComponent_MagicWind.Blow(map, MapComponent_MagicWind.Shape.Round, sp.center, sp.radius, s, 64, sp.owner);
            if (g != null) { g.tint = Partials.SandColor; g.sculpt = sp.sculpt; }   // the spout keeps its spell's shaping
            if (AtmosphereBridge.Throws)
                foreach (IntVec3 x in GenRadial.RadialCellsAround(sp.center.ToIntVec3(), sp.radius, true))
                {
                    if (!x.InBounds(map)) continue;
                    Vector3 o = x.ToVector3Shifted() - sp.center; o.y = 0f;
                    if (o.sqrMagnitude < 0.05f) continue;
                    Vector3 w = new Vector3(-o.z, 0f, o.x).normalized * 0.85f - o.normalized * 0.3f;
                    AtmosphereBridge.Wind(map, x, new Vector2(w.x, w.z) * Mathf.Min(AtmosphereBridge.MaxPush, s / 5f));
                }
        }

        private void Flay(Spout sp, int now)
        {
            IntVec3 mid = sp.center.ToIntVec3();
            if (!mid.InBounds(map)) return;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(mid, sp.radius, true).Where(c => c.InBounds(map)).InRandomOrder().Take(4))
                FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 1.4f, Partials.SandColor);
            // in its heart the sand flays, and a trail of grit hangs behind
            if (now % 60 == 0) foreach (IntVec3 c in GenRadial.RadialCellsAround(mid, Mathf.Max(1.5f, sp.radius * 0.6f), true))
            {
                if (!c.InBounds(map)) continue;
                foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList())
                    if (!p.Dead && (sp.sculpt == 0 || sp.owner == null || (sp.sculpt == 1) != SpellSculpt.Own(p, sp.owner)))
                        p.TakeDamage(new DamageInfo(DamageDefOf.Scratch, 1.5f * sp.power * MapComponent_MagicWind.Held(p, 30f), 0f, -1f, sp.owner));
            }
            if (now % 60 == 0) FireSmoke.Puff(mid, map, 0.8f);
        }

        private void Keep(VeilField v)
        {
            var wind = MapComponent_MagicWind.Of(map);
            int shown = 0;
            foreach (IntVec3 c in v.cells)
            {
                // a strong wind tears the veil: it does not fill while the wind blows
                if (wind != null && wind.At(c, out _) >= 20f) continue;
                if (map.gasGrid.DensityAt(c, GasType.BlindSmoke) < 120) FireSmoke.Puff(c, map, 0.8f * v.power);
                if (shown < 3 && Rand.Chance(0.08f)) { FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 2f, Partials.SandColor); shown++; }
            }
        }
    }

    public static class PartialsDev
    {
        [LudeonTK.DebugAction("Magic", "Partial: Thunder here", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void ThunderHere() { IntVec3 c = Verse.UI.MouseCell(); Partials.Thunder(null, Find.CurrentMap, GenRadial.RadialCellsAround(c, 4f, true).Where(x => x.InBounds(Find.CurrentMap)).ToList(), c, 1f); }

        [LudeonTK.DebugAction("Magic", "Partial: sand spout here", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void SpoutHere() => Partials.Spout(null, Find.CurrentMap, Verse.UI.MouseCell(), 3f, 1f);

        [LudeonTK.DebugAction("Magic", "Partial: dust veil here (r4, 1 min)", actionType = LudeonTK.DebugActionType.ToolMap, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void VeilHere() { IntVec3 c = Verse.UI.MouseCell(); Partials.Veil(null, Find.CurrentMap, GenRadial.RadialCellsAround(c, 4f, true).Where(x => x.InBounds(Find.CurrentMap)).ToList(), 1f, 3600); }
    }
}
