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
    /// <summary>Magic work (the author's choice - a mix): some work magic only quickens - a caster of fire cooks faster, one
    /// of earth builds faster, paying Arcana while at it; some is magic's own, in its own column of the work tab, for
    /// casters only - water washes the floor clean round a spot, the wind carries a heap of things to the stockpile at
    /// once, earth draws ore up from a vein the caster has sensed. Each pays Arcana and teaches its element.</summary>
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
            if (j.workGiverDef?.workType == WorkTypeDefOf.Construction) return RuneKind.Earth;
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

    /// <summary>Cooking with fire, building with earth: faster for a caster of it who has Arcana to spend.</summary>
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
            Add(StatDefOf.ConstructionSpeed, RuneKind.Earth);
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
}
