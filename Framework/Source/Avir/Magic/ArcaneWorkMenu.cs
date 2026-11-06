using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>Right-click: make a caster do an arcane work here now (the author, Fix155 - needed for testing; the work's
    /// own checks decide, as for any prioritized work).</summary>
    public class FloatMenuOptionProvider_ArcaneWork : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            Pawn p = context?.FirstSelectedPawn;
            WorkTypeDef magic = MagicWork.Type;
            if (p == null || magic == null || !AvirMagic.On || Mages.Core(p) == null) yield break;
            IntVec3 cell = context.ClickedCell;
            var things = cell.InBounds(p.Map) ? cell.GetThingList(p.Map).ToList() : new List<Thing>();
            foreach (WorkGiverDef d in magic.workGiversByPriority)
            {
                if (!(d.Worker is WorkGiver_Scanner sc)) continue;
                Job job = null;
                try
                {
                    if (sc.ShouldSkip(p, true)) continue;
                    // only where the work itself would look (Fix160: room works offered themselves on every cell)
                    var potThings = sc.PotentialWorkThingsGlobal(p);
                    var tset = potThings != null ? new HashSet<Thing>(potThings) : null;
                    foreach (Thing t in things)
                    {
                        if (tset != null ? !tset.Contains(t) : !sc.PotentialWorkThingRequest.Accepts(t)) continue;
                        if (sc.HasJobOnThing(p, t, true)) { job = sc.JobOnThing(p, t, true); if (job != null) break; }
                    }
                    var potCells = job == null ? sc.PotentialWorkCellsGlobal(p) : null;
                    if (job == null && potCells != null && potCells.Contains(cell) && sc.HasJobOnCell(p, cell, true)) job = sc.JobOnCell(p, cell, true);
                }
                catch (Exception) { job = null; }
                if (job == null) continue;
                Job j = job;
                yield return new FloatMenuOption("Prioritize " + d.label + " (by magic)", () => p.jobs.TryTakeOrderedJob(j, JobTag.Misc));
            }
        }
    }
}
