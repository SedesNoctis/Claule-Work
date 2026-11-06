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
    /// place"). A partial is narrow: strong at its one thing, and it does not do the rest. The first: Thunder.</summary>
    public enum PartialKind { None, Thunder }

    public static class Partials
    {
        public static readonly Color ThunderColor = new Color(0.62f, 0.42f, 1f);

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

        public static string Text(PartialKind pk) => pk == PartialKind.Thunder
            ? "Thunder (dust with a Focus beside Release): not a blast of grit but a blast of sound. It deafens and stuns all within reach, tears at the ears (its own wound), shatters glass, batters doors and light buildings, and sends animals fleeing. Among many walls it echoes - every solid wall near makes it stronger; in the open it is weaker. It does not grind, blind or bury - a partial meeting does its one thing only."
            : null;

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
                        if (p == caster || p.Dead) continue;
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
}
