using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Fishing by magic (the author, Fix162: "casters of water, the winds and so on can be trusted with Odyssey's
    /// fishing"): the game's own fishing - its zones, its fish, its yields - done by a caster of water (the fish are drawn
    /// to the line) or of the wind (the water is stirred where they gather): quicker and more, paid in Arcana per catch,
    /// teaching the element. Only with Odyssey.</summary>
    public class WorkGiver_ArcaneFish : WorkGiver_Fish
    {
        public const float Cost = 3f, Xp = 4f;

        public static RuneKind? Kind(Pawn p)
        {
            if (MagicWork.Able(p, RuneKind.Water, Cost) != null) return RuneKind.Water;
            if (MagicWork.Able(p, RuneKind.Air, Cost) != null) return RuneKind.Air;
            return null;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !ModsConfig.OdysseyActive || Kind(pawn) == null || base.ShouldSkip(pawn, forced);

        private static Job Mine(Job j)
        {
            JobDef d = DefDatabase<JobDef>.GetNamedSilentFail("Avir_ArcaneFish");
            if (j != null && d != null && j.def == JobDefOf.Fish) j.def = d;
            return j;
        }

        public override Job NonScanJob(Pawn pawn) => Kind(pawn) == null ? null : Mine(base.NonScanJob(pawn));
        public override Job JobOnCell(Pawn pawn, IntVec3 c, bool forced = false) => Kind(pawn) == null ? null : Mine(base.JobOnCell(pawn, c, forced));
    }

    public class JobDriver_ArcaneFish : JobDriver_Fish
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            RuneKind k = WorkGiver_ArcaneFish.Kind(pawn) ?? RuneKind.Water;
            HediffDef aid = DefDatabase<HediffDef>.GetNamedSilentFail(k == RuneKind.Water ? "Avir_ArcaneFishingWater" : "Avir_ArcaneFishingAir");
            if (aid != null && pawn.health.hediffSet.GetFirstHediffOfDef(aid) == null) pawn.health.AddHediff(aid);
            AddFinishAction(cond =>
            {
                Hediff h = aid != null ? pawn.health.hediffSet.GetFirstHediffOfDef(aid) : null;
                if (h != null) pawn.health.RemoveHediff(h);
                if (cond == JobCondition.Succeeded) { Hediff_ArcaneCore c = MagicWork.Able(pawn, k, WorkGiver_ArcaneFish.Cost); if (c != null) MagicWork.Done(c, k, WorkGiver_ArcaneFish.Cost, WorkGiver_ArcaneFish.Xp); }
            });
            foreach (Toil t in base.MakeNewToils())
            {
                Toil tt = t;
                tt.AddPreTickIntervalAction(delta =>
                {
                    if (pawn.IsHashIntervalTick(90, delta) && pawn.Spawned && job.targetA.IsValid)
                    {
                        if (k == RuneKind.Water) FleckMaker.Static(job.targetA.Cell, pawn.Map, FleckDefOf.WaterRipple, 1.2f);
                        else FleckMaker.ThrowDustPuffThick(job.targetA.Cell.ToVector3Shifted(), pawn.Map, 0.8f, MagicFx.AirDust);
                    }
                });
                yield return tt;
            }
        }
    }
}
