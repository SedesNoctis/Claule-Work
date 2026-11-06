using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Firefighting by magic (the author, Fix154: "why not - anyone can"): a caster of water douses the fire from a
    /// few cells off, a caster of the wind smothers it; every fire round the target goes out at once. An emergency work.</summary>
    public class WorkGiver_ArcaneFirefight : WorkGiver_Scanner
    {
        public const float Cost = 2f, Radius = 2.9f, Xp = 4f, Reach = 6f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDefOf.Fire);
        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        /// <summary>Water if the caster knows it, else the wind.</summary>
        public static RuneKind? Kind(Pawn p)
        {
            if (MagicWork.Able(p, RuneKind.Water, Cost) != null) return RuneKind.Water;
            if (MagicWork.Able(p, RuneKind.Air, Cost) != null) return RuneKind.Air;
            return null;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => Kind(pawn) == null || pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count == 0;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Fire f) || f.parent is Pawn) return false;
            if (!pawn.Map.areaManager.Home[t.Position] && !forced) return false;
            return Kind(pawn) != null && pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_ArcaneFirefight"), t);
    }

    public class JobDriver_ArcaneFirefight : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            RuneKind k = WorkGiver_ArcaneFirefight.Kind(pawn) ?? RuneKind.Water;
            var kinds = new[] { k };
            WorkCast wc = WorkSpells.Make(Mages.Core(pawn), "Avir_ArcaneFirefight", kinds, WorkGiver_ArcaneFirefight.Cost);
            yield return WorkSpells.GoTo(this, true, WorkGiver_ArcaneFirefight.Reach);
            Color tint = k == RuneKind.Water ? new Color(0.5f, 0.7f, 1f, 0.5f) : MagicFx.AirDust;
            yield return ArcaneChannel.Channel(this, wc.FromBook ? 30 : 40, wc.spell ?? WorkSpells.Charm("Avir_ArcaneFirefight", kinds), true, tint);
            Toil put = ToilMaker.MakeToil("ArcaneFirefight");
            put.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, k, wc.cost);
                if (c == null) return;
                Map map = pawn.Map;
                IntVec3 at = TargetA.Cell;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(at, wc.R(WorkGiver_ArcaneFirefight.Radius), true))
                {
                    if (!cell.InBounds(map)) continue;
                    foreach (Fire f in cell.GetThingList(map).OfType<Fire>().ToList()) if (!f.Destroyed) f.Destroy();
                    if (Rand.Chance(0.3f)) { if (k == RuneKind.Water) FleckMaker.Static(cell, map, FleckDefOf.WaterSplash, 0.8f); else FleckMaker.ThrowDustPuffThick(cell.ToVector3Shifted(), map, 0.8f, MagicFx.AirDust); }
                }
                MagicWork.Done(c, k, wc.cost, WorkGiver_ArcaneFirefight.Xp * (wc.FromBook ? 1.5f : 1f));
            };
            put.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return put;
        }
    }
}
