using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>One cast of arcane work: from a household spell in the caster's book, or the plain charm if there is none.</summary>
    public class WorkCast
    {
        public RitualSchema spell;
        public int breadth, amplify, reach;
        public float cost;
        public bool FromBook => spell != null;
        /// <summary>A radius widened by Breadth: +1.5 cells for each.</summary>
        public float R(float baseR) => Mathf.Min(GenRadial.MaxRadialPatternRadius - 1f, baseR + 1.5f * breadth);
        /// <summary>Strength by Amplify: +30% for each.</summary>
        public float S => 1f + 0.3f * amplify;
        /// <summary>A count (things, cells) widened by Breadth: +50% for each.</summary>
        public int N(int baseN) => Mathf.RoundToInt(baseN * (1f + 0.5f * breadth));
        /// <summary>How far a caster of earth works from: 10 cells, +6 for each Reach.</summary>
        public float Range => 10f + 6f * reach;
    }

    /// <summary>Household spells (the author): the arcane works are done by a plain charm - or, if the caster's book holds a
    /// spell written for that work, by the spell: better and more (its Breadth widens, its Amplify strengthens, its Reach
    /// lets earth work from further off), quicker to channel, for the spell's own Arcana. A spell is "for the work" when it
    /// is of the work's elements and holds the work's runes (Магия-правила.md, "Бытовые заклинания").</summary>
    public static class WorkSpells
    {
        public static WorkCast Now = new WorkCast();

        /// <summary>The runes a household spell for each work holds (besides its elements).</summary>
        public static readonly Dictionary<string, RuneKind[]> Runes = new Dictionary<string, RuneKind[]>
        {
            ["Avir_MagicTend"] = new[] { RuneKind.Restore },
            ["Avir_LifeHeal"] = new[] { RuneKind.Restore },
            ["Avir_LifeCrops"] = new[] { RuneKind.Growth },
            ["Avir_ArcaneFirefight"] = new[] { RuneKind.Release },
            ["Avir_ArcaneFish"] = new[] { RuneKind.Hold, RuneKind.Lift },   // water: a bubble holds the catch; wind: lifts it out
            ["Avir_MagicClean"] = new[] { RuneKind.Purge },
            ["Avir_MagicHaul"] = new[] { RuneKind.Lift },
            ["Avir_ArcaneGrowth"] = new[] { RuneKind.Growth },
            ["Avir_ArcaneRaiseRock"] = new[] { RuneKind.Shape },
            ["Avir_MagicSmooth"] = new[] { RuneKind.Transmute },
            ["Avir_ArcaneDrill"] = new[] { RuneKind.Transmute, RuneKind.Enchant },
            ["Avir_ArcaneDrillMagma"] = new[] { RuneKind.Transmute, RuneKind.Enchant },
            ["Avir_ArcaneEnrich"] = new[] { RuneKind.Growth },
            ["Avir_ArcaneSmelt"] = new[] { RuneKind.Transmute },
            ["Avir_ArcaneCut"] = new[] { RuneKind.Transmute },
            ["Avir_ArcaneWarm"] = new[] { RuneKind.Aura },
            ["Avir_ArcaneMelt"] = new[] { RuneKind.Release },
            ["Avir_ArcaneSteam"] = new[] { RuneKind.Purge },
            ["Avir_ArcaneCharge"] = new[] { RuneKind.Enchant },
        };

        public static IEnumerable<RuneDef> AllRunes(RitualSchema s) =>
            s?.weave != null ? s.weave.All().SelectMany(c => (c.sections ?? new List<RitualRing>()).SelectMany(g => g.runes).Concat(c.element != null ? new[] { c.element } : new RuneDef[0])).Where(x => x != null)
                             : s?.rings?.SelectMany(g => g.runes).Where(x => x != null) ?? Enumerable.Empty<RuneDef>();

        public static HashSet<RuneKind> Elements(RitualSchema s) =>
            new HashSet<RuneKind>(AllRunes(s).Where(x => x.role == RuneRole.Element).Select(x => x.kind));

        private static RitualReading Reading(RitualSchema s)
        {
            if (s.weave == null) return RitualEvaluator.Read(s);
            var r = new RitualReading();
            foreach (SpellCircle c in WeaveSpell.Small(s.weave))
            {
                RitualReading k = RitualEvaluator.Read(c.AsSchema());
                r.breadth = Mathf.Max(r.breadth, k.breadth); r.amplify = Mathf.Max(r.amplify, k.amplify); r.reach = Mathf.Max(r.reach, k.reach);
            }
            return r;
        }

        /// <summary>The household spell in this caster's book for this work, if any (the strongest the caster can pay for;
        /// short of Arcana - the plain charm).</summary>
        public static RitualSchema Find(Hediff_ArcaneCore c, string work, RuneKind[] kinds)
        {
            if (c == null || kinds == null || !Runes.TryGetValue(work, out RuneKind[] need)) return null;
            var want = new HashSet<RuneKind>(kinds);
            return c.Book.spells.Where(s => s != null && Elements(s).SetEquals(want) && need.All(k => AllRunes(s).Any(x => x.kind == k)) && Spells.CostFor(c, s) <= c.arcana)
                .OrderByDescending(s => { RitualReading r = Reading(s); return r.breadth + r.amplify + r.reach; }).FirstOrDefault();
        }

        public static WorkCast Make(Hediff_ArcaneCore c, string work, RuneKind[] kinds, float plainCost)
        {
            ArcaneWork.EnsureCharm(c, work, kinds);   // the caster puts the charm together himself, the first time (and betters it as he grows)
            RitualSchema s = Find(c, work, kinds);
            if (s == null) return new WorkCast { cost = plainCost };
            RitualReading r = Reading(s);
            return new WorkCast { spell = s, breadth = r.breadth, amplify = r.amplify, reach = r.reach, cost = Spells.CostFor(c, s) };
        }

        /// <summary>The circle drawn while a plain charm is worked: its elements in the heart, its runes round them.</summary>
        public static RitualSchema Charm(string work, RuneKind[] kinds)
        {
            var s = new RitualSchema { name = "charm", size = RitualCircleSize.Small };
            s.rings.Add(new RitualRing { runes = kinds.Select(k => DefDatabase<RuneDef>.AllDefsListForReading.FirstOrDefault(d => d.role == RuneRole.Element && d.kind == k)).Where(x => x != null).ToList(), slots = 1 });
            if (Runes.TryGetValue(work, out RuneKind[] need))
                s.rings.Add(new RitualRing { runes = need.Select(k => DefDatabase<RuneDef>.AllDefsListForReading.FirstOrDefault(d => d.role != RuneRole.Element && d.kind == k)).Where(x => x != null).ToList() });
            return s;
        }

        /// <summary>Where a caster of earth stands to work from afar: here, if the target is in reach and in sight; else the
        /// nearest such cell.</summary>
        public static IntVec3 Stand(Pawn p, IntVec3 target, float range)
        {
            Map map = p.Map;
            if (p.Position.DistanceTo(target) <= range && GenSight.LineOfSight(p.Position, target, map, true)) return p.Position;
            IntVec3 best = IntVec3.Invalid; float bd = float.MaxValue;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(target, Mathf.Min(range, GenRadial.MaxRadialPatternRadius - 1f), true))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c == target) continue;
                float d = c.DistanceToSquared(p.Position);
                if (d >= bd || !GenSight.LineOfSight(c, target, map, true) || !p.CanReach(c, PathEndMode.OnCell, Danger.Some)) continue;
                bd = d; best = c;
            }
            return best;
        }

        /// <summary>The way to the work: to the target itself (close work), or to a spot in reach of it (earth's, from afar).</summary>
        public static Toil GoTo(JobDriver d, bool far, float range)
        {
            if (!far) return d.job.targetA.HasThing ? Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch) : Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);
            Toil t = ToilMaker.MakeToil("ArcaneStand");
            t.initAction = () =>
            {
                Pawn p = d.pawn;
                IntVec3 at = Stand(p, d.job.targetA.Cell, range);
                if (!at.IsValid) { d.EndJobWith(JobCondition.Incompletable); return; }
                if (at == p.Position) { d.ReadyForNextToil(); return; }
                p.pather.StartPath(at, PathEndMode.OnCell);
            };
            t.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            return t;
        }
    }

    /// <summary>The arcane works being channelled now, for their circles to be drawn (MapComponent_SpellCircles).</summary>
    public static class ArcaneChannel
    {
        public class Entry { public Pawn pawn; public RitualSchema schema; public LocalTargetInfo target; public bool far; public int start, ticks; }
        public static readonly Dictionary<Pawn, Entry> Active = new Dictionary<Pawn, Entry>();

        /// <summary>The channel toil of any arcane work: standing, its circle lit under the caster (and, from afar, on the
        /// target, with motes running between); it ends the drawing when done.</summary>
        public static Toil Channel(JobDriver d, int ticks, RitualSchema schema, bool far, Color tint)
        {
            // quicker by the stage (x0.7 Novice ... x1.6 Grandmaster), and it may slip at the end
            RuneKind[] kinds = schema != null ? WorkSpells.Elements(schema).ToArray() : new RuneKind[0];
            Hediff_ArcaneCore core = Mages.Core(d.pawn);
            if (kinds.Length > 0 && core != null) ticks = Mathf.Max(20, Mathf.RoundToInt(ticks / ArcaneWork.Speed(core, kinds)));
            Toil t = Toils_General.Wait(ticks).WithProgressBarToilDelay(TargetIndex.A);
            bool rolled = false;   // the slip is weighed once, as the work is let go
            if (kinds.Length > 0) t.AddEndCondition(() => { if (rolled || d.ticksLeftThisToil > 1) return JobCondition.Ongoing; rolled = true; return ArcaneWork.Slip(d.pawn, kinds) ? JobCondition.Incompletable : JobCondition.Ongoing; });
            t.initAction = () =>
            {
                Pawn p = d.pawn;
                p.pather?.StopDead();
                p.rotationTracker.FaceTarget(d.job.targetA);
                Active[p] = new Entry { pawn = p, schema = schema, target = d.job.targetA, far = far, start = Find.TickManager.TicksGame, ticks = ticks };
            };
            t.tickIntervalAction = delta =>
            {
                Pawn p = d.pawn;
                if (!p.IsHashIntervalTick(30, delta) || p.Map == null) return;
                IntVec3 at = d.job.targetA.Cell;
                if (far)
                {
                    // motes running from the caster to the work
                    Vector3 a = p.DrawPos, b = at.ToVector3Shifted();
                    float f = Rand.Value;
                    FleckMaker.ThrowDustPuffThick(a + (b - a) * f, p.Map, 0.6f, tint);
                }
                FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), p.Map, 0.9f, tint);
            };
            t.handlingFacing = true;
            t.AddFinishAction(() => Active.Remove(d.pawn));
            return t;
        }

        public static void Draw(Map map)
        {
            if (Active.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (Entry e in Active.Values.ToList())
            {
                if (e.pawn?.Map != map || e.schema == null) { if (e.pawn == null || e.pawn.Destroyed) Active.Remove(e.pawn); continue; }
                float prog = Mathf.Clamp01((now - e.start) / (float)Mathf.Max(1, e.ticks));
                SpellSigil.Draw(e.pawn.DrawPos, e.schema, 0.35f + 0.5f * prog, 1.3f);
                if (e.far && e.target.IsValid) SpellSigil.Draw(e.target.CenterVector3, e.schema, 0.25f + 0.45f * prog, 0.9f);
            }
        }
    }
}
