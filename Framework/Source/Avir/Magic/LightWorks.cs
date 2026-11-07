using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The works of a mage of the Crystal (Light meeting Earth - the author, Fix217): on a chosen cell the mage raises
    /// a light crystal - it shines at all hours, Light feeding on itself, for about five days - or a sun crystal - it burns
    /// by day like a sun lamp, plants grow under it, and goes out at night; no power either way. Both can be recoloured (Light
    /// is any colour). Both are reflectors too: every spell of Light answered at a steady share of its strength, never losing
    /// focus and never weighing on the hold. A crystal running low is renewed by the mage.</summary>
    public static class LightWorks
    {
        public const float Arcana = 4f, Xp = 8f;
        public static bool Crystal(Hediff_ArcaneCore c) => c != null && MagicWork.Knows(c, RuneKind.Light) && MagicWork.Knows(c, RuneKind.Earth);
        public static Hediff_ArcaneCore Able(Pawn p) { Hediff_ArcaneCore c = MagicWork.Able(p, RuneKind.Light, Arcana); return Crystal(c) ? c : null; }
        public static bool Low(Building_LightReflector r) => r != null && r.Work && r.dies > 0 && r.dies - Find.TickManager.TicksGame < GenDate.TicksPerDay;
    }

    public abstract class Designator_LightCrystal : Designator_Arcane
    {
        protected abstract string Key { get; }
        protected override DesignationDef Designation => DefDatabase<DesignationDef>.GetNamedSilentFail(Key);
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map) || Designation == null || Map.designationManager.DesignationAt(c, Designation) != null) return false;
            if (!c.Standable(Map) || c.GetEdifice(Map) != null) return false;
            return true;
        }
    }

    public class Designator_LightCrystalStore : Designator_LightCrystal
    {
        protected override string Key => "Avir_LightCrystalStore";
        public Designator_LightCrystalStore()
        {
            defaultLabel = "Light crystal (Crystal)";
            defaultDesc = "Mark a cell for a mage of the Crystal (Light and Earth) to raise a light crystal: it shines at all hours for about five days, no power needed; its colour can be changed. A reflector too - it answers every spell of Light at a steady share.";
            icon = ArcaneIcons.For("Avir_LightCrystalStore", "UI/Designators/SmoothSurface");
        }
    }

    public class Designator_LightCrystalSun : Designator_LightCrystal
    {
        protected override string Key => "Avir_LightCrystalSun";
        public Designator_LightCrystalSun()
        {
            defaultLabel = "Sun crystal (Crystal)";
            defaultDesc = "Mark a cell for a mage of the Crystal (Light and Earth) to raise a sun crystal: it burns by day like a sun lamp - plants grow under it - and goes out at night, for about five days, no power needed; its colour can be changed. A reflector too.";
            icon = ArcaneIcons.For("Avir_LightCrystalSun", "UI/Designators/SmoothSurface");
        }
    }

    /// <summary>Raising the crystals where marked, and renewing those running low.</summary>
    public class WorkGiver_LightCrystal : WorkGiver_Scanner
    {
        private static readonly string[] Keys = { "Avir_LightCrystalStore", "Avir_LightCrystalSun" };
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => LightWorks.Able(pawn) == null;
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            var cells = new List<IntVec3>();
            foreach (string k in Keys) { DesignationDef d = DefDatabase<DesignationDef>.GetNamedSilentFail(k); if (d != null) cells.AddRange(pawn.Map.designationManager.SpawnedDesignationsOfDef(d).Select(x => x.target.Cell)); }
            cells.AddRange(Building_LightReflector.All(pawn.Map).Where(r => LightWorks.Low(r) && r.Faction == pawn.Faction).Select(r => r.Position));
            return cells;
        }
        public static string KeyAt(Map map, IntVec3 c)
        {
            foreach (string k in Keys) { DesignationDef d = DefDatabase<DesignationDef>.GetNamedSilentFail(k); if (d != null && map.designationManager.DesignationAt(c, d) != null) return k; }
            return null;
        }
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            bool marked = KeyAt(pawn.Map, c) != null;
            bool low = c.GetFirstThing<Building_LightReflector>(pawn.Map) is Building_LightReflector r && LightWorks.Low(r);
            return (marked || low) && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced) && LightWorks.Able(pawn) != null;
        }
        public override Job JobOnCell(Pawn pawn, IntVec3 c, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LightCrystal"), c);
    }

    public class JobDriver_LightCrystal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, 360, WorkSpells.Charm("Avir_LightCrystal", new[] { RuneKind.Light, RuneKind.Earth }), false, new Color(0.85f, 0.95f, 1f, 0.5f));
            Toil done = ToilMaker.MakeToil("LightCrystal");
            done.initAction = () =>
            {
                Map map = pawn.Map; IntVec3 c = TargetA.Cell;
                Hediff_ArcaneCore core = LightWorks.Able(pawn);
                if (core == null) return;
                if (c.GetFirstThing<Building_LightReflector>(map) is Building_LightReflector old && old.Work) old.dies = Find.TickManager.TicksGame + GenDate.TicksPerDay * 5;   // renewed
                else
                {
                    string key = WorkGiver_LightCrystal.KeyAt(map, c);
                    if (key == null) return;
                    map.designationManager.TryRemoveDesignation(c, DefDatabase<DesignationDef>.GetNamedSilentFail(key));
                    if (Building_LightReflector.RaiseAt(pawn, map, c, DefDatabase<ThingDef>.GetNamedSilentFail(key)) == null) return;
                }
                LightMagic.Glint(map, c.ToVector3Shifted(), 1.6f, false);
                MagicWork.Done(core, RuneKind.Light, LightWorks.Arcana, LightWorks.Xp);
                MageClass.Gain(core, RuneKind.Earth, LightWorks.Xp * 0.5f);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }
}
