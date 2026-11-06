using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Fishing by magic (the author, Fix163: "isn't it simpler to gather fish by bubbles out of the water with
    /// Holding, or to yank them out by the wind?"). No line and no waiting: from the shore of a fishing zone a caster of
    /// water closes a bubble round a few fish under the surface and lifts it out to their feet; a caster of the wind
    /// yanks one fish out from farther off. The fish are the water's own (Odyssey's fishing: its zones, its kinds, its
    /// stock) - the water body's stock goes down as for any catch. Only with Odyssey.</summary>
    public class WorkGiver_ArcaneFish : WorkGiver_Scanner
    {
        public const float Cost = 4f, Xp = 5f;
        public static float Reach(RuneKind k) => k == RuneKind.Water ? 6f : 10f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public static RuneKind? Kind(Pawn p)
        {
            if (MagicWork.Able(p, RuneKind.Water, Cost) != null) return RuneKind.Water;
            if (MagicWork.Able(p, RuneKind.Air, Cost) != null) return RuneKind.Air;
            return null;
        }

        private static IEnumerable<Zone_Fishing> Zones(Map map) => map.zoneManager.AllZones.OfType<Zone_Fishing>().Where(z => z.ShouldFishNow && !z.PausedDueToResourceCount);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !ModsConfig.OdysseyActive || Kind(pawn) == null || !Zones(pawn.Map).Any();

        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            foreach (Zone_Fishing z in Zones(pawn.Map).ToList())
            {
                IntVec3 c = z.RandomFishableCell;
                if (c.IsValid) yield return c;
            }
        }

        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            if (!(pawn.Map.zoneManager.ZoneAt(c) is Zone_Fishing z) || !z.ShouldFishNow || !z.IsFishable(c)) return false;
            WaterBody wb = FishingUtility.GetWaterBody(c, pawn.Map);
            if (wb == null || !wb.HasFish || wb.Population < 1f) return false;
            return Kind(pawn) != null && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced);
        }

        public override Job JobOnCell(Pawn pawn, IntVec3 c, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_ArcaneFish"), c);
    }

    public class JobDriver_ArcaneFish : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            RuneKind k = WorkGiver_ArcaneFish.Kind(pawn) ?? RuneKind.Water;
            var kinds = new[] { k };
            WorkCast wc = WorkSpells.Make(Mages.Core(pawn), "Avir_ArcaneFish", kinds, WorkGiver_ArcaneFish.Cost);
            yield return WorkSpells.GoTo(this, true, WorkGiver_ArcaneFish.Reach(k));
            Color tint = k == RuneKind.Water ? new Color(0.5f, 0.75f, 1f, 0.5f) : MagicFx.AirDust;
            yield return ArcaneChannel.Channel(this, wc.FromBook ? 120 : 180, wc.spell ?? WorkSpells.Charm("Avir_ArcaneFish", kinds), true, tint);
            Toil take = ToilMaker.MakeToil("ArcaneFish");
            take.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, k, wc.cost);
                Map map = pawn.Map;
                IntVec3 at = TargetA.Cell;
                if (c == null || map == null) return;
                WaterBody wb = FishingUtility.GetWaterBody(at, map);
                if (wb == null || !wb.HasFish) return;
                // water: a bubble scoops a few at once (more with Breadth); the wind: one, yanked out from farther off
                int tries = k == RuneKind.Water ? 2 + wc.breadth : 1 + (wc.amplify > 0 ? 1 : 0);
                int got = 0;
                for (int i = 0; i < tries && wb.Population >= 1f; i++)
                {
                    foreach (Thing t in FishingUtility.GetCatchesFor(pawn, at, false, out bool _).ToList())
                    {
                        if (t == null) continue;
                        GenPlace.TryPlaceThing(t, pawn.Position, map, ThingPlaceMode.Near);
                        got += t.stackCount;
                    }
                    wb.Population = Mathf.Max(0f, wb.Population - 1f);
                }
                (map.zoneManager.ZoneAt(at) as Zone_Fishing)?.Notify_Fished();
                if (k == RuneKind.Water)
                {
                    MapComponent_SpellSprites.Put(map, "Water/Geyser", at.ToVector3Shifted(), 1.4f, 60);
                    FleckMaker.Static(at, map, FleckDefOf.WaterSplash, 1.6f);
                    FleckMaker.Static(pawn.Position, map, FleckDefOf.WaterSplash, 1f);
                }
                else
                {
                    FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 1.4f, MagicFx.AirDust);
                    FleckMaker.Static(at, map, FleckDefOf.WaterSplash, 1.1f);
                }
                MagicWork.Done(c, k, wc.cost, WorkGiver_ArcaneFish.Xp * (wc.FromBook ? 1.5f : 1f));
                if (got == 0) MoteMaker.ThrowText(at.ToVector3Shifted(), map, "nothing caught", 2.5f);
            };
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return take;
        }
    }
}
