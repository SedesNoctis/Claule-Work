using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>Magic work (the author's choice - a mix): some work magic only quickens - a caster of fire cooks faster,
    /// paying Arcana while at it; some is magic's own, in its own column of the work tab, for casters only - water tends
    /// the wounded with no medicine (as a doctor would; how well - by the caster's mastery of water) and washes the floor
    /// clean round a spot, the wind carries a heap of things to the stockpile at once, earth raises plain rock walls where
    /// they are marked, smooths walls and floors, and draws ore up from a vein the caster has sensed. Each pays Arcana and
    /// teaches its element.</summary>
    public static class MagicWork
    {
        public static WorkTypeDef Type => DefDatabase<WorkTypeDef>.GetNamedSilentFail("Avir_MagicWork");

        /// <summary>Does the caster know this element - walks its path, or has cast in it?</summary>
        public static bool Knows(Hediff_ArcaneCore c, RuneKind k) => c != null && (MageClass.Main(c)?.kind == k || MageClass.XpIn(c, k) > 0f);

        public static Hediff_ArcaneCore Able(Pawn p, RuneKind k, float arcana)
        {
            if (!AvirMagic.On) return null;
            Hediff_ArcaneCore c = Mages.Core(p);
            return c != null && Knows(c, k) && c.arcana >= arcana ? c : null;
        }

        /// <summary>The quickening of ordinary work: +15%, and +10% for each stage past Novice (Grandmaster +65%).</summary>
        public static float Quickening(Hediff_ArcaneCore c, RuneKind k) => 0.15f + 0.1f * MageClass.Stage(MageClass.XpIn(c, k));

        public const float QuickenArcanaPerHour = 2f, QuickenXpPerHour = 4f;

        /// <summary>Which element quickens the work the pawn is at now (Fire for cooking, Earth for building), if any.</summary>
        public static RuneKind? Quickens(Pawn p)
        {
            Job j = p?.CurJob;
            if (j == null) return null;
            if (j.bill?.recipe?.workSkill == SkillDefOf.Cooking) return RuneKind.Fire;
            return null;
        }

        /// <summary>The core's tick: the quickened work is paid for, and teaches.</summary>
        public static void Tick(Hediff_ArcaneCore c, float hours)
        {
            RuneKind? k = Quickens(c.pawn);
            if (k == null || !Knows(c, k.Value) || c.arcana < 1f) return;
            c.Add(-QuickenArcanaPerHour * hours);
            MageClass.Gain(c, k.Value, QuickenXpPerHour * hours);
        }

        public static void Done(Hediff_ArcaneCore c, RuneKind k, float arcana, float xp)
        {
            c.Add(-arcana);
            MageClass.Gain(c, k, xp);
        }
    }

    /// <summary>Cooking with fire: faster for a caster of it who has Arcana to spend.</summary>
    public class StatPart_MagicWork : StatPart
    {
        public RuneKind element;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is Pawn p && MagicWork.Able(p, element, 1f) is Hediff_ArcaneCore c) val *= 1f + MagicWork.Quickening(c, element);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (req.Thing is Pawn p && MagicWork.Able(p, element, 1f) is Hediff_ArcaneCore c)
                return "Magic of " + MageClass.Label(element) + ": x" + (1f + MagicWork.Quickening(c, element)).ToString("0.00") + " (pays " + MagicWork.QuickenArcanaPerHour.ToString("0") + " Arcana an hour at it)";
            return null;
        }
    }

    [StaticConstructorOnStartup]
    public static class MagicWorkStatInjector
    {
        static MagicWorkStatInjector()
        {
            Add(DefDatabase<StatDef>.GetNamedSilentFail("CookSpeed"), RuneKind.Fire);
        }

        private static void Add(StatDef stat, RuneKind k)
        {
            if (stat == null) return;
            if (stat.parts == null) stat.parts = new List<StatPart>();
            if (stat.parts.Any(x => x is StatPart_MagicWork m && m.element == k)) return;
            stat.parts.Add(new StatPart_MagicWork { parentStat = stat, element = k });
        }
    }

    /// <summary>The magic column is a caster's: everyone else has it greyed out.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes))]
    public static class Patch_MagicWorkCastersOnly
    {
        public static void Postfix(Pawn __instance, ref List<WorkTypeDef> __result)
        {
            WorkTypeDef w = MagicWork.Type;
            if (w == null || __result == null || __result.Contains(w)) return;
            if (!AvirMagic.On || Mages.Core(__instance) == null) __result.Add(w);
        }
    }

    // ------------------------------------------------------------------ water: washing the floor

    public class WorkGiver_MagicClean : WorkGiver_Scanner
    {
        public const float Cost = 3f, Radius = 3.9f, Xp = 6f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.listerFilthInHomeArea.FilthInHomeArea;
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => MagicWork.Able(pawn, RuneKind.Water, Cost) == null || pawn.Map.listerFilthInHomeArea.FilthInHomeArea.Count == 0;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t is Filth && !t.IsForbidden(pawn) && pawn.Map.areaManager.Home[t.Position] && pawn.CanReserve(t, 1, -1, null, forced)
            && MagicWork.Able(pawn, RuneKind.Water, Cost) != null;

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_MagicClean"), t);
    }

    public class JobDriver_MagicClean : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(90).WithProgressBarToilDelay(TargetIndex.A).FailOnDespawnedOrNull(TargetIndex.A);
            Toil wash = ToilMaker.MakeToil("MagicClean");
            wash.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Water, WorkGiver_MagicClean.Cost);
                if (c == null) return;
                Map map = pawn.Map;
                IntVec3 at = TargetA.Cell;
                int n = 0;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(at, WorkGiver_MagicClean.Radius, true))
                {
                    if (!cell.InBounds(map) || !map.areaManager.Home[cell]) continue;
                    foreach (Filth f in cell.GetThingList(map).OfType<Filth>().ToList()) { f.Destroy(); n++; }
                    if (Rand.Chance(0.3f)) FleckMaker.Static(cell, map, FleckDefOf.WaterSplash, 0.8f);
                }
                MagicWork.Done(c, RuneKind.Water, WorkGiver_MagicClean.Cost, WorkGiver_MagicClean.Xp);
            };
            wash.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return wash;
        }
    }

    // ------------------------------------------------------------------ air: carrying a heap at once

    public class WorkGiver_MagicHaul : WorkGiver_Scanner
    {
        public const float CostPer = 1f, Radius = 3.9f, XpPer = 2f, Reach = 40f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.listerHaulables.ThingsPotentiallyNeedingHauling();
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => MagicWork.Able(pawn, RuneKind.Air, CostPer * 2f) == null;

        public static bool Store(Thing t, Pawn pawn, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (t == null || !t.Spawned || t.IsForbidden(pawn) || !t.def.EverHaulable || t is Pawn) return false;
            if (!StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, t.Map, StoreUtility.CurrentStoragePriorityOf(t, false), pawn.Faction, out cell, true)) return false;
            return cell.DistanceTo(t.Position) <= Reach;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (MagicWork.Able(pawn, RuneKind.Air, CostPer * 2f) == null || !pawn.CanReserve(t, 1, -1, null, forced) || !Store(t, pawn, out _)) return false;
            // worth the wind only for a heap: two or more things to carry round the spot
            return GenRadial.RadialDistinctThingsAround(t.Position, t.Map, Radius, true).Count(x => x != t && x.def.EverHaulable && Store(x, pawn, out _)) >= 1;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_MagicHaul"), t);
    }

    public class JobDriver_MagicHaul : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(60).WithProgressBarToilDelay(TargetIndex.A).FailOnDespawnedOrNull(TargetIndex.A);
            Toil carry = ToilMaker.MakeToil("MagicHaul");
            carry.initAction = () =>
            {
                Hediff_ArcaneCore c = Mages.Core(pawn);
                if (c == null) return;
                Map map = pawn.Map;
                IntVec3 at = TargetA.Cell;
                int max = 4 + MageClass.Stage(MageClass.XpIn(c, RuneKind.Air)) * 2, n = 0;
                foreach (Thing t in GenRadial.RadialDistinctThingsAround(at, map, WorkGiver_MagicHaul.Radius, true).Where(x => x.def.EverHaulable).ToList())
                {
                    if (n >= max || c.arcana < WorkGiver_MagicHaul.CostPer) break;
                    if (!WorkGiver_MagicHaul.Store(t, pawn, out IntVec3 cell)) continue;
                    Vector3 from = t.DrawPos;
                    t.DeSpawn();
                    if (!GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Direct) && !GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near)) continue;
                    FleckMaker.ThrowDustPuffThick(from, map, 0.9f, MagicFx.AirDust);
                    FleckMaker.ThrowDustPuffThick(cell.ToVector3Shifted(), map, 0.9f, MagicFx.AirDust);
                    MagicWork.Done(c, RuneKind.Air, WorkGiver_MagicHaul.CostPer, WorkGiver_MagicHaul.XpPer);
                    n++;
                }
            };
            carry.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return carry;
        }
    }

    // ------------------------------------------------------------------ earth: drawing ore up from a sensed vein

    public class WorkGiver_MagicMine : WorkGiver_Scanner
    {
        public const float Cost = 8f, Xp = 15f, StoneChance = 0.1f;
        public const int Ticks = 600;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) =>
            pawn.Map.GetComponent<MapComponent_DeepSense>()?.KnownBy(pawn).Where(c => pawn.Map.deepResourceGrid.ThingDefAt(c) != null).ToList() ?? new List<IntVec3>();
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => MagicWork.Able(pawn, RuneKind.Earth, Cost) == null;

        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false) =>
            MagicWork.Able(pawn, RuneKind.Earth, Cost) != null && pawn.Map.deepResourceGrid.ThingDefAt(c) != null && c.Standable(pawn.Map)
            && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced);

        public override Job JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_MagicMine"), cell);
    }

    public class JobDriver_MagicMine : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);
            Toil channel = Toils_General.Wait(WorkGiver_MagicMine.Ticks).WithProgressBarToilDelay(TargetIndex.A);
            channel.tickIntervalAction = delta => { if (pawn.IsHashIntervalTick(40, delta)) FleckMaker.ThrowDustPuffThick(TargetA.Cell.ToVector3Shifted(), pawn.Map, 1.1f, MagicFx.EarthDust); };
            yield return channel;
            Toil draw = ToilMaker.MakeToil("MagicMine");
            draw.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Earth, WorkGiver_MagicMine.Cost);
                Map map = pawn.Map;
                IntVec3 at = TargetA.Cell;
                ThingDef ore = map.deepResourceGrid.ThingDefAt(at);
                if (c == null || ore == null) return;
                int left = map.deepResourceGrid.CountAt(at);
                // one portion a cast - a vein is not emptied by one spell, nor by many mages at once
                int portion = Mathf.Min(left, Mathf.Max(1, ore.deepCountPerPortion > 0 ? ore.deepCountPerPortion : 10));
                map.deepResourceGrid.SetAt(at, portion >= left ? null : ore, left - portion);
                ThingDef what = ore;
                if (Rand.Chance(WorkGiver_MagicMine.StoneChance))
                {
                    // the stone came up instead: a chunk of what lies there
                    ThingDef chunk = Find.World.NaturalRockTypesIn(map.Tile).Select(r => r.building.mineableThing).FirstOrDefault(x => x != null);
                    if (chunk != null) { what = chunk; portion = 1; }
                }
                Thing got = ThingMaker.MakeThing(what);
                got.stackCount = Mathf.Min(portion, what.stackLimit);
                GenPlace.TryPlaceThing(got, at, map, ThingPlaceMode.Near);
                FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 2f, MagicFx.EarthDust);
                MagicWork.Done(c, RuneKind.Earth, WorkGiver_MagicMine.Cost, WorkGiver_MagicMine.Xp);
            };
            draw.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return draw;
        }
    }

    // ------------------------------------------------------------------ water: tending the wounded

    public class WorkGiver_MagicTend : WorkGiver_Scanner
    {
        public const float Cost = 4f, Xp = 8f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => MagicWork.Able(pawn, RuneKind.Water, Cost) == null;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t is Pawn patient && patient != pawn && !patient.Dead && patient.RaceProps.IsFlesh && HealthAIUtility.ShouldBeTendedNowByPlayer(patient)
            && WorkGiver_Tend.GoodLayingStatusForTend(patient, pawn) && !patient.IsForbidden(pawn) && pawn.CanReserve(patient, 1, -1, null, forced)
            && MagicWork.Able(pawn, RuneKind.Water, Cost) != null;

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_MagicTend"), t);

        /// <summary>How well water tends: by the caster's mastery - a novice 30%, each stage +12% (a grandmaster 90%).</summary>
        public static float Quality(Hediff_ArcaneCore c) => Mathf.Clamp(0.3f + 0.12f * MageClass.Stage(MageClass.XpIn(c, RuneKind.Water)), 0.2f, 0.95f);
    }

    public class JobDriver_MagicTend : JobDriver
    {
        private static readonly List<Hediff> tend = new List<Hediff>(), order = new List<Hediff>();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil work = Toils_General.Wait(300).WithProgressBarToilDelay(TargetIndex.A).FailOnDespawnedOrNull(TargetIndex.A);
            work.tickIntervalAction = delta => { if (pawn.IsHashIntervalTick(60, delta)) FleckMaker.Static(TargetA.Cell, pawn.Map, FleckDefOf.WaterSplash, 0.7f); };
            yield return work;
            Toil done = ToilMaker.MakeToil("MagicTend");
            done.initAction = () =>
            {
                Pawn patient = TargetA.Thing as Pawn;
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Water, WorkGiver_MagicTend.Cost);
                if (patient == null || c == null) return;
                tend.Clear(); order.Clear();
                TendUtility.GetOptimalHediffsToTendWithSingleTreatment(patient, false, tend, null);
                float q = WorkGiver_MagicTend.Quality(c);
                for (int i = 0; i < tend.Count; i++) tend[i].Tended(Mathf.Clamp01(q * Rand.Range(0.9f, 1.1f)), 1f, i);
                FleckMaker.ThrowMetaIcon(patient.Position, patient.Map, FleckDefOf.HealingCross);
                MagicWork.Done(c, RuneKind.Water, WorkGiver_MagicTend.Cost, WorkGiver_MagicTend.Xp);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    // ------------------------------------------------------------------ earth: rock walls where marked, smoothing

    public static class MagicWorkDefOf
    {
        public static DesignationDef RaiseRock => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_RaiseRock");
        public static ThingDef Rock(Map map) => Find.World.NaturalRockTypesIn(map.Tile).FirstOrDefault() ?? ThingDefOf.Granite;
    }

    /// <summary>Marks where a caster of earth is to raise a plain rock wall (of the stone of this land).</summary>
    public class Designator_RaiseRock : Designator_Cells
    {
        public Designator_RaiseRock()
        {
            defaultLabel = "Raise rock (magic)";
            defaultDesc = "Mark where a caster of earth is to raise plain rock - the stone of this land, as if it had always stood there. It can be mined or smoothed like any rock. Casters of earth do it in the magic column of the work tab, for Arcana.";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/SmoothSurface", false) ?? BaseContent.BadTex;
            useMouseIcon = true;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Mine;
        }

        public override bool Visible => AvirMagic.On;
        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;
        protected override DesignationDef Designation => MagicWorkDefOf.RaiseRock;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map)) return false;
            if (Map.designationManager.DesignationAt(c, Designation) != null) return false;
            if (c.GetEdifice(Map) != null) return "Something stands there.";
            if (!c.Walkable(Map)) return false;
            return true;
        }

        public override void DesignateSingleCell(IntVec3 c) => Map.designationManager.AddDesignation(new Designation(c, Designation));
    }

    public class WorkGiver_MagicRaiseRock : WorkGiver_Scanner
    {
        public const float Cost = 6f, Xp = 10f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) =>
            MagicWorkDefOf.RaiseRock == null ? Enumerable.Empty<IntVec3>() : pawn.Map.designationManager.SpawnedDesignationsOfDef(MagicWorkDefOf.RaiseRock).Select(d => d.target.Cell).ToList();
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => MagicWork.Able(pawn, RuneKind.Earth, Cost) == null || MagicWorkDefOf.RaiseRock == null;

        public static bool Clear(Map map, IntVec3 c) => c.GetEdifice(map) == null && c.GetFirstPawn(map) == null && !c.GetThingList(map).Any(t => t.def.category == ThingCategory.Item);

        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false) =>
            MagicWork.Able(pawn, RuneKind.Earth, Cost) != null && pawn.Map.designationManager.DesignationAt(c, MagicWorkDefOf.RaiseRock) != null
            && Clear(pawn.Map, c) && pawn.CanReserve(c, 1, -1, null, forced);

        public override Job JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_MagicRaiseRock"), cell);
    }

    public class JobDriver_MagicRaiseRock : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);
            Toil channel = Toils_General.Wait(300).WithProgressBarToilDelay(TargetIndex.A);
            channel.tickIntervalAction = delta => { if (pawn.IsHashIntervalTick(40, delta)) FleckMaker.ThrowDustPuffThick(TargetA.Cell.ToVector3Shifted(), pawn.Map, 1.1f, MagicFx.EarthDust); };
            yield return channel;
            Toil raise = ToilMaker.MakeToil("MagicRaiseRock");
            raise.initAction = () =>
            {
                Map map = pawn.Map;
                IntVec3 c = TargetA.Cell;
                Hediff_ArcaneCore core = MagicWork.Able(pawn, RuneKind.Earth, WorkGiver_MagicRaiseRock.Cost);
                if (core == null || !WorkGiver_MagicRaiseRock.Clear(map, c)) return;
                foreach (Thing t in c.GetThingList(map).Where(t => t is Plant || t is Filth).ToList()) t.Destroy();
                GenSpawn.Spawn(MagicWorkDefOf.Rock(map), c, map);
                map.designationManager.TryRemoveDesignation(c, MagicWorkDefOf.RaiseRock);
                FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 2f, MagicFx.EarthDust);
                MagicWork.Done(core, RuneKind.Earth, WorkGiver_MagicRaiseRock.Cost, WorkGiver_MagicRaiseRock.Xp);
            };
            raise.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return raise;
        }
    }

    /// <summary>Smoothing by earth: the designated walls and floors round the spot, several at once.</summary>
    public class WorkGiver_MagicSmooth : WorkGiver_Scanner
    {
        public const float CostPer = 1f, XpPer = 2f, Radius = 3.9f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) =>
            pawn.Map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.SmoothFloor).Concat(pawn.Map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.SmoothWall)).Select(d => d.target.Cell).ToList();
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => MagicWork.Able(pawn, RuneKind.Earth, CostPer * 2f) == null;

        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false) =>
            MagicWork.Able(pawn, RuneKind.Earth, CostPer * 2f) != null && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced)
            && (pawn.Map.designationManager.DesignationAt(c, DesignationDefOf.SmoothFloor) != null || pawn.Map.designationManager.DesignationAt(c, DesignationDefOf.SmoothWall) != null);

        public override Job JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_MagicSmooth"), cell);
    }

    public class JobDriver_MagicSmooth : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(150).WithProgressBarToilDelay(TargetIndex.A);
            Toil smooth = ToilMaker.MakeToil("MagicSmooth");
            smooth.initAction = () =>
            {
                Map map = pawn.Map;
                Hediff_ArcaneCore c = Mages.Core(pawn);
                if (c == null) return;
                int max = 4 + 2 * MageClass.Stage(MageClass.XpIn(c, RuneKind.Earth)), n = 0;
                DesignationManager dm = map.designationManager;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(TargetA.Cell, WorkGiver_MagicSmooth.Radius, true))
                {
                    if (n >= max || c.arcana < WorkGiver_MagicSmooth.CostPer) break;
                    if (!cell.InBounds(map)) continue;
                    if (dm.DesignationAt(cell, DesignationDefOf.SmoothWall) != null && cell.GetEdifice(map) is Thing wall)
                    {
                        dm.TryRemoveDesignation(cell, DesignationDefOf.SmoothWall);
                        SmoothableWallUtility.SmoothWall(wall, pawn);
                    }
                    else if (dm.DesignationAt(cell, DesignationDefOf.SmoothFloor) != null && map.terrainGrid.TerrainAt(cell).smoothedTerrain != null)
                    {
                        dm.TryRemoveDesignation(cell, DesignationDefOf.SmoothFloor);
                        map.terrainGrid.SetTerrain(cell, map.terrainGrid.TerrainAt(cell).smoothedTerrain);
                    }
                    else continue;
                    FleckMaker.ThrowDustPuffThick(cell.ToVector3Shifted(), map, 0.8f, MagicFx.EarthDust);
                    MagicWork.Done(c, RuneKind.Earth, WorkGiver_MagicSmooth.CostPer, WorkGiver_MagicSmooth.XpPer);
                    n++;
                }
            };
            smooth.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return smooth;
        }
    }
}
