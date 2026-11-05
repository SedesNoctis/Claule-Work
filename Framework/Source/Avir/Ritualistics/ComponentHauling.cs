using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Ritualistics
{
    /// <summary>
    /// The haulers lay what a drawn ritual's component circles expect on them: crystals, feathers, batteries, seals,
    /// materials, an artifact. What lies on a component circle's own cell is what that circle uses.
    /// </summary>
    public class WorkGiver_FillRitualComponents : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.allBuildingsColonist.Where(b => b.TryGetComp<CompRitualCircle>()?.Drawn == true);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !PotentialWorkThingsGlobal(pawn).Any();

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) => Next(pawn, t, forced, out _, out _, out _);

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!Next(pawn, t, forced, out Thing item, out IntVec3 cell, out int count)) return null;
            Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, item, cell);
            job.count = count;
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }

        private static bool Next(Pawn pawn, Thing circleThing, bool forced, out Thing item, out IntVec3 cell, out int count)
        {
            item = null; cell = IntVec3.Invalid; count = 1;
            CompRitualCircle c = circleThing.TryGetComp<CompRitualCircle>();
            RitualSchema s = c?.drawn;
            if (s == null || c.Running || circleThing.IsForbidden(pawn)) return false;
            Map map = pawn.Map;
            for (int k = 0; k < s.components.Count; k++)
            {
                RitualComponent comp = s.components[k];
                // a seal is drawn, never laid (its "Expected" thing is only the icon)
                if (comp.kind == ComponentKind.None || comp.kind == ComponentKind.Seal) continue;
                Thing held = c.Holding(k);
                // a material circle is filled up to the amount chosen for it; any other kind wants one thing (or one load)
                int lack = 0;
                if (comp.kind == ComponentKind.Material)
                {
                    lack = System.Math.Max(1, comp.amount) - (held?.stackCount ?? 0);
                    if (lack <= 0) continue;
                }
                else if (held != null) continue;
                IntVec3 at = c.ComponentCell(k);
                if (!at.InBounds(map) || !at.Standable(map) || map.reservationManager.IsReservedByAnyoneOf(at, pawn.Faction)) continue;
                ThingDef want = comp.Expected;
                System.Predicate<Thing> ok = t => t.Spawned && !t.IsForbidden(pawn) && t.Position != at && pawn.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Some, 1, -1, null, forced)
                                                  && !Enumerable.Range(0, s.components.Count).Any(j => c.ComponentCell(j) == t.Position);
                Thing found;
                if (comp.kind == ComponentKind.Artifact)
                    found = GenClosest.ClosestThing_Global(pawn.Position, map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                        .Where(t => t.TryGetComp<Artifactorics.CompArtifact>()?.state != null), 9999f, x => ok(x));
                else if (want != null)
                    found = GenClosest.ClosestThing_Global(pawn.Position, map.listerThings.ThingsOfDef(want), 9999f, x => ok(x));
                else continue;
                if (found == null) continue;
                item = found; cell = at;
                count = comp.kind == ComponentKind.Crystal ? System.Math.Min(found.stackCount, 10)
                      : comp.kind == ComponentKind.Feathers ? System.Math.Min(found.stackCount, 50)
                      : comp.kind == ComponentKind.Material
                          ? System.Math.Max(1, System.Math.Min(System.Math.Min(found.stackCount, lack), found.def.stackLimit - (held?.stackCount ?? 0)))
                          : 1;
                return true;
            }
            return false;
        }
    }
}
