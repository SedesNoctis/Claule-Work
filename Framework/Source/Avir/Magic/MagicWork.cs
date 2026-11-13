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
            return c != null && Knows(c, k) && c.arcana >= arcana && ArcaneWork.StageOk(c, new[] { k }) ? c : null;
        }

        /// <summary>The quickening of ordinary work: +15%, and +10% for each stage past Novice (Grandmaster +65%).</summary>
        public static float Quickening(Hediff_ArcaneCore c, RuneKind k) => 0.15f + 0.1f * MageClass.Stage(MageClass.XpIn(c, k));

        public const float QuickenArcanaPerHour = 2f, QuickenXpPerHour = 4f;

        /// <summary>What a building job is made of (the frame's or blueprint's stuff, or the thing's own).</summary>
        public static ThingDef StuffOf(Job j)
        {
            Thing t = j?.targetA.Thing;
            if (t == null) return null;
            if (t is Frame f) return f.Stuff;
            if (t is Blueprint b) return b.EntityToBuildStuff();
            return t.Stuff;
        }

        /// <summary>The elements that quicken building with this stuff (the author): stone - earth; metal - magma (fire
        /// and earth); anything else - none.</summary>
        public static RuneKind[] BuildKinds(ThingDef stuff)
        {
            var cats = stuff?.stuffProps?.categories;
            if (cats == null) return null;
            if (cats.Contains(StuffCategoryDefOf.Stony)) return new[] { RuneKind.Earth };
            if (cats.Contains(StuffCategoryDefOf.Metallic)) return new[] { RuneKind.Fire, RuneKind.Earth };
            return null;
        }

        /// <summary>Which elements quicken the work the pawn is at now: fire for cooking; for building - by what is
        /// built (stone - earth, metal - magma).</summary>
        public static RuneKind[] Quickens(Pawn p)
        {
            Job j = p?.CurJob;
            if (j == null) return null;
            if (j.bill?.recipe?.workSkill == SkillDefOf.Cooking) return new[] { RuneKind.Fire };
            if (j.workGiverDef?.workType == WorkTypeDefOf.Construction) return BuildKinds(StuffOf(j));
            return null;
        }

        public static float Quickening(Hediff_ArcaneCore c, RuneKind[] ks) => (float)ks.Average(k => Quickening(c, k));

        /// <summary>The core's tick: the quickened work is paid for, and teaches (shared between the elements).</summary>
        public static void Tick(Hediff_ArcaneCore c, float hours)
        {
            RuneKind[] ks = Quickens(c.pawn);
            if (ks == null || Arcane.Able(c.pawn, 1f, ks) == null) return;
            c.Add(-QuickenArcanaPerHour * hours);
            foreach (RuneKind k in ks) MageClass.Gain(c, k, QuickenXpPerHour * hours / ks.Length);
        }

        public static void Done(Hediff_ArcaneCore c, RuneKind k, float arcana, float xp)
        {
            c.Add(-arcana);
            MageClass.Gain(c, k, xp);
        }
    }

    /// <summary>Cooking with fire; building with stone by earth, with metal by magma: faster for a caster who knows it
    /// and has Arcana to spend.</summary>
    public class StatPart_MagicWork : StatPart
    {
        public bool building;

        private RuneKind[] Kinds(Pawn p) => building ? MagicWork.BuildKinds(MagicWork.StuffOf(p.CurJob)) : new[] { RuneKind.Fire };

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is Pawn p && Kinds(p) is RuneKind[] ks && Arcane.Able(p, 1f, ks) is Hediff_ArcaneCore c) val *= 1f + MagicWork.Quickening(c, ks);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (req.Thing is Pawn p && Kinds(p) is RuneKind[] ks && Arcane.Able(p, 1f, ks) is Hediff_ArcaneCore c)
                return "Magic of " + (ks.Length > 1 ? "magma" : MageClass.Label(ks[0])) + ": x" + (1f + MagicWork.Quickening(c, ks)).ToString("0.00") + " (pays " + MagicWork.QuickenArcanaPerHour.ToString("0") + " Arcana an hour at it)";
            return null;
        }
    }

    [StaticConstructorOnStartup]
    public static class MagicWorkStatInjector
    {
        static MagicWorkStatInjector()
        {
            Add(DefDatabase<StatDef>.GetNamedSilentFail("CookSpeed"), false);
            Add(StatDefOf.ConstructionSpeed, true);
        }

        private static void Add(StatDef stat, bool building)
        {
            if (stat == null) return;
            if (stat.parts == null) stat.parts = new List<StatPart>();
            if (stat.parts.Any(x => x is StatPart_MagicWork m && m.building == building)) return;
            stat.parts.Add(new StatPart_MagicWork { parentStat = stat, building = building });
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
            WorkCast wc = WorkSpells.Make(Mages.Core(pawn), "Avir_MagicClean", new[] { RuneKind.Water }, WorkGiver_MagicClean.Cost);
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, wc.FromBook ? 70 : 90, wc.spell ?? WorkSpells.Charm("Avir_MagicClean", new[] { RuneKind.Water }), false, new Color(0.5f, 0.7f, 1f, 0.5f));
            Toil wash = ToilMaker.MakeToil("MagicClean");
            wash.initAction = () =>
            {
                Hediff_ArcaneCore c = MagicWork.Able(pawn, RuneKind.Water, wc.cost);
                if (c == null) return;
                Map map = pawn.Map;
                IntVec3 at = TargetA.Cell;
                int n = 0;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(at, wc.R(WorkGiver_MagicClean.Radius), true))
                {
                    if (!cell.InBounds(map) || !map.areaManager.Home[cell]) continue;
                    foreach (Filth f in cell.GetThingList(map).OfType<Filth>().ToList()) { f.Destroy(); n++; }
                    if (Rand.Chance(0.3f)) FleckMaker.Static(cell, map, FleckDefOf.WaterSplash, 0.8f);
                }
                MagicWork.Done(c, RuneKind.Water, wc.cost, WorkGiver_MagicClean.Xp * (wc.FromBook ? 1.5f : 1f));
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
            WorkCast wc = WorkSpells.Make(Mages.Core(pawn), "Avir_MagicHaul", new[] { RuneKind.Air }, WorkGiver_MagicHaul.CostPer);
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, wc.FromBook ? 45 : 60, wc.spell ?? WorkSpells.Charm("Avir_MagicHaul", new[] { RuneKind.Air }), false, MagicFx.AirDust);
            Toil carry = ToilMaker.MakeToil("MagicHaul");
            carry.initAction = () =>
            {
                Hediff_ArcaneCore c = Mages.Core(pawn);
                if (c == null) return;
                Map map = pawn.Map;
                IntVec3 at = TargetA.Cell;
                int max = wc.N(4 + MageClass.Stage(MageClass.XpIn(c, RuneKind.Air)) * 2), n = 0;
                // a household spell pays its own Arcana once; a plain charm, one for each thing carried
                if (wc.FromBook) { if (c.arcana < wc.cost) return; c.Add(-wc.cost); }
                foreach (Thing t in GenRadial.RadialDistinctThingsAround(at, map, wc.R(WorkGiver_MagicHaul.Radius), true).Where(x => x.def.EverHaulable).ToList())
                {
                    if (n >= max || (!wc.FromBook && c.arcana < WorkGiver_MagicHaul.CostPer)) break;
                    if (!WorkGiver_MagicHaul.Store(t, pawn, out IntVec3 cell)) continue;
                    Vector3 from = t.DrawPos;
                    t.DeSpawn();
                    if (!GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Direct) && !GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near)) continue;
                    FleckMaker.ThrowDustPuffThick(from, map, 0.9f, MagicFx.AirDust);
                    FleckMaker.ThrowDustPuffThick(cell.ToVector3Shifted(), map, 0.9f, MagicFx.AirDust);
                    MagicWork.Done(c, RuneKind.Air, wc.FromBook ? 0f : WorkGiver_MagicHaul.CostPer, WorkGiver_MagicHaul.XpPer * (wc.FromBook ? 1.5f : 1f));
                    n++;
                }
            };
            carry.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return carry;
        }
    }

    // ------------------------------------------------------------------ tending the wounded by any magic that heals

    /// <summary>Who tends by magic (the author, Fix266: "every school that can heal gets the healers' work, whatever the
    /// meeting"): water, Life and Light - the one the caster knows best. A caster of Light who knows Fire too is a healer of
    /// the White Flame: the tending lays a seal that mends the wounds slowly through the day (and flares once if hurt).</summary>
    public static class MagicHealers
    {
        public static readonly RuneKind[] Schools = { RuneKind.Water, RuneKind.Life, RuneKind.Light };

        public static RuneKind? School(Hediff_ArcaneCore c)
        {
            if (c == null) return null;
            RuneKind? best = null; int stage = -1;
            foreach (RuneKind k in Schools)
            {
                if (!MagicWork.Knows(c, k)) continue;
                int st = MageClass.Stage(MageClass.XpIn(c, k));
                if (st > stage) { stage = st; best = k; }
            }
            return best;
        }

        public static Hediff_ArcaneCore Able(Pawn p, float cost, out RuneKind school)
        {
            school = RuneKind.Water;
            Hediff_ArcaneCore c = Mages.Core(p);
            RuneKind? k = School(c);
            if (k == null) return null;
            school = k.Value;
            return MagicWork.Able(p, k.Value, cost);
        }

        public static bool WhiteFlame(Hediff_ArcaneCore c, RuneKind school) => school == RuneKind.Light && MagicWork.Knows(c, RuneKind.Fire);

        public static Color Tint(RuneKind k, bool white) => white ? new Color(1f, 0.97f, 0.82f, 0.55f) : k == RuneKind.Life ? new Color(0.45f, 0.85f, 0.45f, 0.5f) : k == RuneKind.Light ? new Color(1f, 0.92f, 0.6f, 0.5f) : new Color(0.5f, 0.7f, 1f, 0.5f);

        public static HediffDef Mend => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WhiteFlameMend");

        [LudeonTK.DebugAction("Magic", "Healers: which school tends (log)", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevSchool(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p); RuneKind? k = School(c);
            Messages.Message(p.LabelShort + ": " + (k == null ? "cannot tend by magic (no water, Life or Light)" : "tends by " + k + (WhiteFlame(c, k.Value) ? " - a healer of the White Flame" : "") + ", quality " + WorkGiver_MagicTend.Quality(c, k.Value).ToStringPercent()), MessageTypeDefOf.NeutralEvent, false);
        }

        [LudeonTK.DebugAction("Magic", "Healers: White Flame seal on pawn (full quality)", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevMend(Pawn p)
        {
            if (Mend == null) return;
            Hediff m = p.health.hediffSet.GetFirstHediffOfDef(Mend) ?? p.health.AddHediff(Mend);
            m.Severity = 0.95f;
        }
    }

    public class WorkGiver_MagicTend : WorkGiver_Scanner
    {
        public const float Cost = 4f, Xp = 8f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => MagicHealers.Able(pawn, Cost, out _) == null;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t is Pawn patient && patient != pawn && !patient.Dead && patient.RaceProps.IsFlesh && HealthAIUtility.ShouldBeTendedNowByPlayer(patient)
            && WorkGiver_Tend.GoodLayingStatusForTend(patient, pawn) && !patient.IsForbidden(pawn) && pawn.CanReserve(patient, 1, -1, null, forced)
            && MagicHealers.Able(pawn, Cost, out _) != null;

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_MagicTend"), t);

        /// <summary>How well magic tends: by the caster's mastery in the healing school - a novice 30%, each stage +12% (a grandmaster 90%).</summary>
        public static float Quality(Hediff_ArcaneCore c, RuneKind k = RuneKind.Water) => Mathf.Clamp(0.3f + 0.12f * MageClass.Stage(MageClass.XpIn(c, k)), 0.2f, 0.95f);
    }

    public class JobDriver_MagicTend : JobDriver
    {
        private static readonly List<Hediff> tend = new List<Hediff>(), order = new List<Hediff>();
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            Hediff_ArcaneCore core = Mages.Core(pawn);
            RuneKind school = MagicHealers.School(core) ?? RuneKind.Water;
            bool white = MagicHealers.WhiteFlame(core, school);
            RuneKind[] kinds = white ? new[] { RuneKind.Light, RuneKind.Fire } : new[] { school };
            WorkCast wc = WorkSpells.Make(core, "Avir_MagicTend", kinds, WorkGiver_MagicTend.Cost);
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, wc.FromBook ? 225 : 300, wc.spell ?? WorkSpells.Charm("Avir_MagicTend", kinds), false, MagicHealers.Tint(school, white));
            Toil done = ToilMaker.MakeToil("MagicTend");
            done.initAction = () =>
            {
                Pawn patient = TargetA.Thing as Pawn;
                Hediff_ArcaneCore c = MagicWork.Able(pawn, school, wc.cost);
                if (patient == null || c == null) return;
                tend.Clear(); order.Clear();
                TendUtility.GetOptimalHediffsToTendWithSingleTreatment(patient, false, tend, null);
                // a household spell of mending tends better: +10% for each Amplify (never past 98%)
                float q = Mathf.Min(0.98f, WorkGiver_MagicTend.Quality(c, school) * (1f + 0.1f * wc.amplify));
                for (int i = 0; i < tend.Count; i++) tend[i].Tended(Mathf.Clamp01(q * Rand.Range(0.9f, 1.1f)), 1f, i);
                Desiccation.Undo(patient, 0.15f * q);   // magic healing of any kind turns the drying back (Fix200)
                if (white && MagicHealers.Mend != null)
                {
                    // the White Flame: not burning the sickness out but a seal laid on the wounds - they mend slowly through the day
                    Hediff m = patient.health.hediffSet.GetFirstHediffOfDef(MagicHealers.Mend) ?? patient.health.AddHediff(MagicHealers.Mend);
                    m.Severity = Mathf.Max(m.Severity, q);
                    if (m.TryGetComp<HediffComp_Disappears>() is HediffComp_Disappears d) d.ticksToDisappear = GenDate.TicksPerDay;
                    if (LightZenith.Seal != null)
                    {
                        Hediff s = patient.health.hediffSet.GetFirstHediffOfDef(LightZenith.Seal) ?? patient.health.AddHediff(LightZenith.Seal);
                        s.Severity = Mathf.Min(LightZenith.MaxSeals, Mathf.Max(1f, Mathf.Floor(s.Severity)));
                    }
                    if (patient.Spawned) MapComponent_SpellSprites.PutTinted(patient.Map, "Light/Flare_Night", LightZenith.ZenithColor, patient.DrawPos, 1.4f, 40);
                }
                FleckMaker.ThrowMetaIcon(patient.Position, patient.Map, FleckDefOf.HealingCross);
                if (white) { MagicWork.Done(c, RuneKind.Light, wc.cost, WorkGiver_MagicTend.Xp * 0.6f * (wc.FromBook ? 1.5f : 1f)); MageClass.Gain(c, RuneKind.Fire, WorkGiver_MagicTend.Xp * 0.4f); }
                else MagicWork.Done(c, school, wc.cost, WorkGiver_MagicTend.Xp * (wc.FromBook ? 1.5f : 1f));
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    /// <summary>The White Flame's seal on tended wounds: they mend slowly through the day, as a good doctor's care would
    /// hasten them - by the healer's quality.</summary>
    public class HediffCompProperties_WhiteFlameMend : HediffCompProperties
    {
        public HediffCompProperties_WhiteFlameMend() { compClass = typeof(HediffComp_WhiteFlameMend); }
    }

    public class HediffComp_WhiteFlameMend : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Pawn.IsHashIntervalTick(250) || Pawn.Dead) return;
            LifeMagic.HealInjuries(Pawn, 0.18f * parent.Severity);
        }
    }

}
