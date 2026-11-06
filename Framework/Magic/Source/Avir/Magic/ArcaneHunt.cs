using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Hunting by magic (the author, Fix161: "I don't see the hunting setting here"): a caster goes after the
    /// animals marked for hunting with no gun at all - near enough to see the prey and reach it, then strikes it with his
    /// battle charm, again as it comes off cooldown, until it falls. The carcass is left for the haulers.</summary>
    public class WorkGiver_ArcaneHunt : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.OnCell;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Hunt).Select(d => d.target.Thing).Where(t => t != null).ToList();

        public static RitualSchema Charm(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p);
            if (c == null || !AvirMagic.On) return null;
            return c.Book.ready.FirstOrDefault(CombatCharms.IsCharm) ?? CombatCharms.Ensure(c, c.dangerousCharms);
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            Mages.Core(pawn) == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Hunt) || Charm(pawn) == null;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn prey) || prey.Dead || prey.Downed || !prey.AnimalOrWildMan()) return false;
            if (pawn.Map.designationManager.DesignationOn(t, DesignationDefOf.Hunt) == null) return false;
            Hediff_ArcaneCore c = Mages.Core(pawn);
            RitualSchema s = Charm(pawn);
            if (c == null || s == null || c.arcana < Spells.CostFor(c, s)) return false;
            return pawn.CanReserve(t, 1, -1, null, forced) && !t.IsForbidden(pawn);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_ArcaneHunt"), t);
    }

    public class JobDriver_ArcaneHunt : JobDriver
    {
        private Pawn Prey => job.targetA.Thing as Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            Toil stalk = ToilMaker.MakeToil("ArcaneHunt");
            int started = 0;
            stalk.initAction = () => started = Find.TickManager.TicksGame;
            stalk.tickAction = () =>
            {
                Pawn prey = Prey;
                if (prey == null || prey.Dead || prey.Downed) { EndJobWith(JobCondition.Succeeded); return; }
                if (Find.TickManager.TicksGame - started > 6000) { EndJobWith(JobCondition.Incompletable); return; }
                if (!pawn.IsHashIntervalTick(15)) return;
                Hediff_ArcaneCore c = Mages.Core(pawn);
                RitualSchema s = WorkGiver_ArcaneHunt.Charm(pawn);
                if (c == null || s == null) { EndJobWith(JobCondition.Incompletable); return; }
                float range = Mathf.Min(Spells.Range(s), 30f) * 0.85f;
                bool near = prey.Position.InHorDistOf(pawn.Position, range) && GenSight.LineOfSight(pawn.Position, prey.Position, pawn.Map, true);
                if (!near)
                {
                    if (!pawn.pather.Moving || pawn.pather.Destination.Cell.DistanceTo(prey.Position) > 3f) pawn.pather.StartPath(prey, PathEndMode.Touch);
                    return;
                }
                if (pawn.pather.Moving) pawn.pather.StopDead();
                pawn.rotationTracker.FaceTarget(prey);
                int slot = CombatCharms.SlotOf(c.Book, s);
                if (slot < 0 || c.Book.cooldown[slot] > Find.TickManager.TicksGame) return;
                if (c.arcana < Spells.CostFor(c, s)) { EndJobWith(JobCondition.Incompletable); return; }
                SpellCasting.Order(pawn, s, slot, prey, IntVec3.Invalid);   // the cast takes over; the hunt is taken up again after it
            };
            stalk.defaultCompleteMode = ToilCompleteMode.Never;
            yield return stalk;
        }
    }
}
