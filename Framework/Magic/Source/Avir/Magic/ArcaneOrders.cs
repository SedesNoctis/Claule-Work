using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>Arcane orders (the author): a tab of orders of its own, where the player says what magic is to do - where to
    /// raise rock (and of which stone), which sensed vein to draw ore from, what to smooth by magic alone, where to quicken
    /// the growing. Casters take these up in the magic column; they also take up the ordinary orders that magic can do
    /// (smoothing), and some work is theirs alone. The meetings of two elements have their own work: magma smelts slag,
    /// dust cuts stone into blocks, haze warms a cold room and melts the snow, steam washes a whole room clean, a storm
    /// charges the batteries.</summary>
    public static class Arcane
    {
        public static DesignationDef RaiseRock => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_RaiseRock");
        public static DesignationDef Drill => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_ArcaneDrill");
        public static DesignationDef Smooth => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_ArcaneSmooth");
        public static DesignationDef Growth => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_ArcaneGrowth");

        public static Hediff_ArcaneCore Able(Pawn p, float arcana, params RuneKind[] kinds)
        {
            if (!AvirMagic.On || p == null) return null;
            Hediff_ArcaneCore c = Mages.Core(p);
            return c != null && c.arcana >= arcana && kinds.All(k => MagicWork.Knows(c, k)) ? c : null;
        }

        /// <summary>Paid and learnt: the experience shared among the elements the work needed.</summary>
        public static void Done(Hediff_ArcaneCore c, float arcana, float xp, params RuneKind[] kinds)
        {
            c.Add(-arcana);
            foreach (RuneKind k in kinds) MageClass.Gain(c, k, xp / Mathf.Max(1, kinds.Length));
        }

        public static int Stage(Hediff_ArcaneCore c, params RuneKind[] kinds) => kinds.Length == 0 ? 0 : Mathf.RoundToInt((float)kinds.Average(k => MageClass.Stage(MageClass.XpIn(c, k))));

        /// <summary>The stones a caster may raise: every natural rock; basalt is magma's (fire and earth).</summary>
        public static List<ThingDef> Rocks => DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.building != null && d.building.isNaturalRock && !d.building.isResourceRock && d.building.mineableThing != null).ToList();

        public static bool Basalt(ThingDef rock) => rock?.defName == "Avir_Basalt";
        public static RuneKind[] RockNeeds(ThingDef rock) => Basalt(rock) ? new[] { RuneKind.Fire, RuneKind.Earth } : new[] { RuneKind.Earth };
    }

    public class MapComponent_ArcaneOrders : MapComponent
    {
        public Dictionary<IntVec3, ThingDef> rock = new Dictionary<IntVec3, ThingDef>();
        public Dictionary<IntVec3, int> grownAt = new Dictionary<IntVec3, int>();
        public MapComponent_ArcaneOrders(Map map) : base(map) { }
        public static MapComponent_ArcaneOrders Of(Map m) => m?.GetComponent<MapComponent_ArcaneOrders>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref rock, "rock", LookMode.Value, LookMode.Def);
            Scribe_Collections.Look(ref grownAt, "grownAt", LookMode.Value, LookMode.Value);
            if (rock == null) rock = new Dictionary<IntVec3, ThingDef>();
            if (grownAt == null) grownAt = new Dictionary<IntVec3, int>();
        }
    }

    // ================================================================== the orders

    public abstract class Designator_Arcane : Designator_Cells
    {
        protected Designator_Arcane()
        {
            useMouseIcon = true;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Mine;
        }
        public override bool Visible => AvirMagic.On;
        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;
        public override void DesignateSingleCell(IntVec3 c) => Map.designationManager.AddDesignation(new Designation(c, Designation));
    }

    /// <summary>Where a caster is to raise plain rock - of the stone the player chose (a list, as stone furniture's is).</summary>
    public class Designator_RaiseRock : Designator_Arcane
    {
        public static ThingDef chosen;
        private ThingDef Chosen => chosen ?? (chosen = Arcane.Rocks.FirstOrDefault(r => r == ThingDefOf.Granite) ?? Arcane.Rocks.FirstOrDefault());

        public Designator_RaiseRock()
        {
            defaultLabel = "Raise rock";
            defaultDesc = "Mark where a caster of earth is to raise plain rock, as if it had always stood there - of the stone chosen. It can be mined or smoothed like any rock. Basalt is magma's: it needs a caster of fire and earth both.";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/SmoothSurface", false) ?? BaseContent.BadTex;
        }

        public override string Label => "Raise rock: " + (Chosen?.label ?? "-");
        protected override DesignationDef Designation => Arcane.RaiseRock;

        public override void ProcessInput(Event ev)
        {
            var opts = Arcane.Rocks.Select(r => new FloatMenuOption(r.LabelCap + (Arcane.Basalt(r) ? " (magma: fire and earth)" : ""), () => { chosen = r; base.ProcessInput(ev); }, r, (ThingStyleDef)null)).ToList();
            if (!opts.Any()) { base.ProcessInput(ev); return; }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map) || Chosen == null) return false;
            if (Map.designationManager.DesignationAt(c, Designation) != null) return false;
            if (c.GetEdifice(Map) != null) return "Something stands there.";
            return c.Walkable(Map);
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            base.DesignateSingleCell(c);
            MapComponent_ArcaneOrders.Of(Map).rock[c] = Chosen;
        }
    }

    /// <summary>Which sensed vein to draw ore from.</summary>
    public class Designator_ArcaneDrill : Designator_Arcane
    {
        public Designator_ArcaneDrill()
        {
            defaultLabel = "Draw ore up";
            defaultDesc = "Mark a deep vein a caster has sensed: casters of earth draw its ore up, a portion at a time, until it is spent. Only what some caster has sensed can be marked.";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/Mine", false) ?? BaseContent.BadTex;
        }
        protected override DesignationDef Designation => Arcane.Drill;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || Map.designationManager.DesignationAt(c, Designation) != null) return false;
            if (Map.deepResourceGrid.ThingDefAt(c) == null) return false;
            if (Map.GetComponent<MapComponent_DeepSense>()?.KnownByAny(c) != true) return "No caster has sensed what lies here.";
            return true;
        }
    }

    /// <summary>Smoothing by magic alone: ordinary hands do not take it.</summary>
    public class Designator_ArcaneSmooth : Designator_Arcane
    {
        public Designator_ArcaneSmooth()
        {
            defaultLabel = "Arcane smoothing";
            defaultDesc = "Mark walls and floors to be smoothed by a caster of earth - several at once. Ordinary hands leave these to magic (the ordinary smoothing order is taken up by casters too).";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/SmoothSurface", false) ?? BaseContent.BadTex;
        }
        protected override DesignationDef Designation => Arcane.Smooth;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map) || Map.designationManager.DesignationAt(c, Designation) != null) return false;
            Thing e = c.GetEdifice(Map);
            if (e != null) return e.def.building?.smoothedThing != null;
            return Map.terrainGrid.TerrainAt(c).smoothedTerrain != null;
        }
    }

    /// <summary>Where the growing is to be quickened.</summary>
    public class Designator_ArcaneGrowth : Designator_Arcane
    {
        public Designator_ArcaneGrowth()
        {
            defaultLabel = "Quicken growth";
            defaultDesc = "Mark ground where casters of water or earth are to quicken what grows - once a day for each spot. The mark stays until cancelled.";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/PlantsHarvest", false) ?? BaseContent.BadTex;
        }
        protected override DesignationDef Designation => Arcane.Growth;
        public override AcceptanceReport CanDesignateCell(IntVec3 c) => c.InBounds(Map) && !c.Fogged(Map) && Map.designationManager.DesignationAt(c, Designation) == null && c.GetEdifice(Map) == null;
    }

    // ================================================================== the work itself

    /// <summary>One kind of arcane work: what it needs, costs and teaches, how long, and what it does at the end.</summary>
    public class ArcaneSpec
    {
        public RuneKind[] kinds;
        public float cost, xp;
        public int ticks;
        public Func<Pawn, LocalTargetInfo, Hediff_ArcaneCore, bool> effect;
        public Color tint = new Color(0.8f, 0.8f, 0.8f, 0.5f);
    }

    public static class ArcaneWorks
    {
        public static readonly Dictionary<string, ArcaneSpec> Specs = new Dictionary<string, ArcaneSpec>
        {
            ["Avir_ArcaneRaiseRock"] = new ArcaneSpec { kinds = new[] { RuneKind.Earth }, cost = 6f, xp = 10f, ticks = 300, effect = RaiseRock },
            ["Avir_ArcaneDrill"] = new ArcaneSpec { kinds = new[] { RuneKind.Earth }, cost = 8f, xp = 15f, ticks = 600, effect = Drill },
            ["Avir_ArcaneGrowth"] = new ArcaneSpec { kinds = new RuneKind[0], cost = 3f, xp = 6f, ticks = 120, effect = Grow, tint = new Color(0.4f, 0.8f, 0.4f, 0.5f) },
            ["Avir_ArcaneSmelt"] = new ArcaneSpec { kinds = new[] { RuneKind.Fire, RuneKind.Earth }, cost = 5f, xp = 10f, ticks = 240, effect = Smelt, tint = new Color(1f, 0.4f, 0.1f, 0.6f) },
            ["Avir_ArcaneCut"] = new ArcaneSpec { kinds = new[] { RuneKind.Earth, RuneKind.Air }, cost = 4f, xp = 8f, ticks = 200, effect = Cut, tint = new Color(0.8f, 0.7f, 0.5f, 0.5f) },
            ["Avir_ArcaneWarm"] = new ArcaneSpec { kinds = new[] { RuneKind.Fire, RuneKind.Air }, cost = 4f, xp = 8f, ticks = 180, effect = Warm, tint = new Color(1f, 0.85f, 0.4f, 0.5f) },
            ["Avir_ArcaneMelt"] = new ArcaneSpec { kinds = new[] { RuneKind.Fire, RuneKind.Air }, cost = 3f, xp = 6f, ticks = 120, effect = Melt, tint = new Color(1f, 0.85f, 0.4f, 0.5f) },
            ["Avir_ArcaneSteam"] = new ArcaneSpec { kinds = new[] { RuneKind.Fire, RuneKind.Water }, cost = 5f, xp = 10f, ticks = 240, effect = Steam, tint = new Color(1f, 1f, 1f, 0.6f) },
            ["Avir_ArcaneCharge"] = new ArcaneSpec { kinds = new[] { RuneKind.Water, RuneKind.Air }, cost = 5f, xp = 10f, ticks = 240, effect = Charge, tint = new Color(0.5f, 0.5f, 1f, 0.6f) },
        };

        /// <summary>Growth is water's or earth's: whichever the caster knows (water first).</summary>
        public static RuneKind[] Grower(Hediff_ArcaneCore c) => MagicWork.Knows(c, RuneKind.Water) ? new[] { RuneKind.Water } : MagicWork.Knows(c, RuneKind.Earth) ? new[] { RuneKind.Earth } : null;

        public static RuneKind[] Needs(string job, Pawn p)
        {
            if (job == "Avir_ArcaneGrowth") { Hediff_ArcaneCore c = Mages.Core(p); return c == null ? null : Grower(c); }
            if (job == "Avir_ArcaneRaiseRock") return null;   // by the stone chosen - checked on the cell
            return Specs.TryGetValue(job, out ArcaneSpec s) ? s.kinds : null;
        }

        public static bool Clear(Map map, IntVec3 c) => c.GetEdifice(map) == null && c.GetFirstPawn(map) == null && !c.GetThingList(map).Any(t => t.def.category == ThingCategory.Item);

        private static bool RaiseRock(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            Map map = p.Map; IntVec3 at = t.Cell;
            var comp = MapComponent_ArcaneOrders.Of(map);
            ThingDef rock = comp.rock.TryGetValue(at, out ThingDef r) ? r : MagicWorkDefOf.Rock(map);
            if (!Clear(map, at) || !Arcane.RockNeeds(rock).All(k => MagicWork.Knows(c, k))) return false;
            foreach (Thing x in at.GetThingList(map).Where(x => x is Plant || x is Filth).ToList()) x.Destroy();
            GenSpawn.Spawn(rock, at, map);
            map.designationManager.TryRemoveDesignation(at, Arcane.RaiseRock);
            comp.rock.Remove(at);
            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 2f, MagicFx.EarthDust);
            return true;
        }

        private static bool Drill(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            Map map = p.Map; IntVec3 at = t.Cell;
            ThingDef ore = map.deepResourceGrid.ThingDefAt(at);
            if (ore == null) { map.designationManager.TryRemoveDesignation(at, Arcane.Drill); return false; }
            int left = map.deepResourceGrid.CountAt(at);
            // one portion a cast - a vein is not emptied by one spell, nor by many mages at once
            int portion = Mathf.Min(left, Mathf.Max(1, ore.deepCountPerPortion > 0 ? ore.deepCountPerPortion : 10));
            map.deepResourceGrid.SetAt(at, portion >= left ? null : ore, left - portion);
            if (portion >= left) map.designationManager.TryRemoveDesignation(at, Arcane.Drill);
            ThingDef what = ore;
            if (Rand.Chance(0.1f))
            {
                ThingDef chunk = Find.World.NaturalRockTypesIn(map.Tile).Select(rk => rk.building.mineableThing).FirstOrDefault(x => x != null);
                if (chunk != null) { what = chunk; portion = 1; }
            }
            Thing got = ThingMaker.MakeThing(what);
            got.stackCount = Mathf.Min(portion, what.stackLimit);
            GenPlace.TryPlaceThing(got, at, map, ThingPlaceMode.Near);
            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 2f, MagicFx.EarthDust);
            return true;
        }

        public const int GrowEvery = GenDate.TicksPerDay;

        private static bool Grow(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            Map map = p.Map;
            var comp = MapComponent_ArcaneOrders.Of(map);
            RuneKind[] k = Grower(c);
            if (k == null) return false;
            float add = 0.12f + 0.03f * Arcane.Stage(c, k);
            int now = Find.TickManager.TicksGame, n = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(t.Cell, 3.9f, true))
            {
                if (!cell.InBounds(map) || map.designationManager.DesignationAt(cell, Arcane.Growth) == null) continue;
                if (comp.grownAt.TryGetValue(cell, out int last) && now - last < GrowEvery) continue;
                Plant pl = cell.GetPlant(map);
                if (pl == null || pl.LifeStage == PlantLifeStage.Mature) continue;
                pl.Growth = Mathf.Min(1f, pl.Growth + add);
                comp.grownAt[cell] = now;
                if (Rand.Chance(0.4f)) FleckMaker.ThrowDustPuffThick(cell.ToVector3Shifted(), map, 0.6f, new Color(0.4f, 0.8f, 0.4f, 0.5f));
                n++;
            }
            return n > 0;
        }

        private static bool Smelt(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            Thing slag = t.Thing;
            if (slag == null || slag.Destroyed || slag.def.smeltProducts.NullOrEmpty()) return false;
            IntVec3 at = slag.Position; Map map = slag.Map;
            foreach (ThingDefCountClass pr in slag.def.smeltProducts)
            {
                Thing got = ThingMaker.MakeThing(pr.thingDef);
                got.stackCount = pr.count;
                GenPlace.TryPlaceThing(got, at, map, ThingPlaceMode.Near);
            }
            slag.Destroy();
            FleckMaker.ThrowFireGlow(at.ToVector3Shifted(), map, 1.2f);
            return true;
        }

        private static bool Cut(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            Thing chunk = t.Thing;
            if (chunk == null || chunk.Destroyed || chunk.def.butcherProducts.NullOrEmpty()) return false;
            IntVec3 at = chunk.Position; Map map = chunk.Map;
            foreach (ThingDefCountClass pr in chunk.def.butcherProducts)
            {
                Thing got = ThingMaker.MakeThing(pr.thingDef);
                got.stackCount = pr.count;
                GenPlace.TryPlaceThing(got, at, map, ThingPlaceMode.Near);
            }
            chunk.Destroy();
            FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 1.4f, new Color(0.8f, 0.7f, 0.5f, 0.6f));
            return true;
        }

        public const float ColdRoom = 12f;

        private static bool Warm(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            Room room = t.Cell.GetRoom(p.Map);
            if (room == null || room.UsesOutdoorTemperature) return false;
            room.Temperature = Mathf.Min(21f, room.Temperature + 10f + 2f * Arcane.Stage(c, RuneKind.Fire, RuneKind.Air));
            FleckMaker.ThrowHeatGlow(t.Cell, p.Map, 2f);
            return true;
        }

        private static bool Melt(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            Map map = p.Map; int n = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(t.Cell, 5.9f, true))
            {
                if (!cell.InBounds(map) || map.snowGrid.GetDepth(cell) <= 0.01f) continue;
                map.snowGrid.SetDepth(cell, 0f);
                n++;
            }
            if (n > 0) FleckMaker.ThrowHeatGlow(t.Cell, map, 3f);
            return n > 0;
        }

        private static bool Steam(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            Room room = t.Cell.GetRoom(p.Map);
            if (room == null || room.UsesOutdoorTemperature) return false;
            int n = 0;
            foreach (IntVec3 cell in room.Cells)
                foreach (Filth f in cell.GetThingList(p.Map).OfType<Filth>().ToList()) { f.Destroy(); n++; }
            foreach (IntVec3 cell in room.Cells.InRandomOrder().Take(12)) FleckMaker.ThrowSmoke(cell.ToVector3Shifted(), p.Map, 1.2f);
            return n > 0;
        }

        private static bool Charge(Pawn p, LocalTargetInfo t, Hediff_ArcaneCore c)
        {
            CompPowerBattery b = t.Thing?.TryGetComp<CompPowerBattery>();
            if (b == null || b.StoredEnergyPct >= 0.999f) return false;
            b.AddEnergy(200f + 100f * Arcane.Stage(c, RuneKind.Water, RuneKind.Air));
            FleckMaker.ThrowLightningGlow(t.Thing.DrawPos, p.Map, 1.5f);
            FleckMaker.ThrowMicroSparks(t.Thing.DrawPos, p.Map);
            return true;
        }
    }

    /// <summary>Any arcane work: go to it, channel, and do it.</summary>
    public class JobDriver_Arcane : JobDriver
    {
        private ArcaneSpec Spec => ArcaneWorks.Specs.TryGetValue(job.def.defName, out ArcaneSpec s) ? s : null;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            ArcaneSpec spec = Spec;
            if (spec == null) yield break;
            if (job.targetA.HasThing) { this.FailOnDespawnedNullOrForbidden(TargetIndex.A); yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch); }
            else yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);
            Toil channel = Toils_General.Wait(spec.ticks).WithProgressBarToilDelay(TargetIndex.A);
            channel.tickIntervalAction = delta => { if (pawn.IsHashIntervalTick(40, delta)) FleckMaker.ThrowDustPuffThick(job.targetA.Cell.ToVector3Shifted(), pawn.Map, 1f, spec.tint); };
            yield return channel;
            Toil done = ToilMaker.MakeToil("ArcaneWork");
            done.initAction = () =>
            {
                RuneKind[] kinds = ArcaneWorks.Needs(job.def.defName, pawn);
                if (job.def.defName == "Avir_ArcaneRaiseRock")
                {
                    var comp = MapComponent_ArcaneOrders.Of(pawn.Map);
                    kinds = Arcane.RockNeeds(comp.rock.TryGetValue(job.targetA.Cell, out ThingDef r) ? r : null);
                }
                if (kinds == null) return;
                Hediff_ArcaneCore c = Arcane.Able(pawn, spec.cost, kinds);
                if (c != null && spec.effect(pawn, job.targetA, c)) Arcane.Done(c, spec.cost, spec.xp, kinds);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    /// <summary>The scanners of the magic column for the arcane works.</summary>
    public abstract class WorkGiver_Arcane : WorkGiver_Scanner
    {
        protected abstract string Job { get; }
        protected ArcaneSpec Spec => ArcaneWorks.Specs[Job];
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        protected Hediff_ArcaneCore Able(Pawn p)
        {
            RuneKind[] k = ArcaneWorks.Needs(Job, p) ?? (Job == "Avir_ArcaneRaiseRock" ? new[] { RuneKind.Earth } : null);
            return k == null ? null : Arcane.Able(p, Spec.cost, k);
        }
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => Able(pawn) == null;
        protected Job Make(LocalTargetInfo t) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(Job), t);
        public override Job JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) => Make(cell);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => Make(t);
        protected bool Cells(DesignationDef d, Pawn pawn, IntVec3 c, bool forced) =>
            d != null && pawn.Map.designationManager.DesignationAt(c, d) != null && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced) && Able(pawn) != null;
        protected IEnumerable<IntVec3> Marked(DesignationDef d, Pawn pawn) => d == null ? Enumerable.Empty<IntVec3>() : pawn.Map.designationManager.SpawnedDesignationsOfDef(d).Select(x => x.target.Cell).ToList();
    }

    public class WorkGiver_ArcaneRaiseRock : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneRaiseRock";
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) => Marked(Arcane.RaiseRock, pawn);
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            if (!Cells(Arcane.RaiseRock, pawn, c, forced) || !ArcaneWorks.Clear(pawn.Map, c)) return false;
            ThingDef rock = MapComponent_ArcaneOrders.Of(pawn.Map).rock.TryGetValue(c, out ThingDef r) ? r : null;
            return Arcane.Able(pawn, Spec.cost, Arcane.RockNeeds(rock)) != null;
        }
    }

    public class WorkGiver_ArcaneDrill : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneDrill";
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) => Marked(Arcane.Drill, pawn);
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false) => Cells(Arcane.Drill, pawn, c, forced) && pawn.Map.deepResourceGrid.ThingDefAt(c) != null;
    }

    public class WorkGiver_ArcaneGrowth : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneGrowth";
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) => Marked(Arcane.Growth, pawn);
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            if (!Cells(Arcane.Growth, pawn, c, forced)) return false;
            Plant pl = c.GetPlant(pawn.Map);
            if (pl == null || pl.LifeStage == PlantLifeStage.Mature) return false;
            var comp = MapComponent_ArcaneOrders.Of(pawn.Map);
            return !(comp.grownAt.TryGetValue(c, out int last) && Find.TickManager.TicksGame - last < ArcaneWorks.GrowEvery);
        }
    }

    public class WorkGiver_ArcaneSmelt : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneSmelt";
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.listerThings.ThingsOfDef(ThingDefOf.ChunkSlagSteel);
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            !t.IsForbidden(pawn) && pawn.Map.areaManager.Home[t.Position] && pawn.CanReserve(t, 1, -1, null, forced) && Able(pawn) != null;
    }

    public class WorkGiver_ArcaneCut : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneCut";
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Chunk);
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            !t.def.butcherProducts.NullOrEmpty() && !t.IsForbidden(pawn) && pawn.Map.areaManager.Home[t.Position] && pawn.CanReserve(t, 1, -1, null, forced) && Able(pawn) != null;
    }

    public class WorkGiver_ArcaneWarm : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneWarm";
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) =>
            pawn.Map.regionGrid.AllRooms.Where(r => !r.UsesOutdoorTemperature && !r.IsDoorway && r.ProperRoom && r.Temperature < ArcaneWorks.ColdRoom && pawn.Map.areaManager.Home[r.Cells.First()])
                .Select(r => r.Cells.FirstOrDefault(c => c.Standable(pawn.Map))).Where(c => c.IsValid).ToList();
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            Room r = c.GetRoom(pawn.Map);
            return r != null && !r.UsesOutdoorTemperature && r.Temperature < ArcaneWorks.ColdRoom && pawn.CanReserve(c, 1, -1, null, forced) && Able(pawn) != null;
        }
    }

    public class WorkGiver_ArcaneMelt : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneMelt";
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) =>
            pawn.Map.areaManager.SnowOrSandClear?.ActiveCells.Where(c => pawn.Map.snowGrid.GetDepth(c) > 0.2f).ToList() ?? new List<IntVec3>();
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false) =>
            pawn.Map.snowGrid.GetDepth(c) > 0.2f && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced) && Able(pawn) != null;
    }

    public class WorkGiver_ArcaneSteam : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneSteam";
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.listerFilthInHomeArea.FilthInHomeArea;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Room r = t.GetRoom();
            // a whole room at once: worth it where filth has piled up indoors
            return r != null && !r.UsesOutdoorTemperature && r.ProperRoom && r.ContainedAndAdjacentThings.Count(x => x is Filth) >= 5
                && !t.IsForbidden(pawn) && pawn.CanReserve(t, 1, -1, null, forced) && Able(pawn) != null;
        }
    }

    public class WorkGiver_ArcaneCharge : WorkGiver_Arcane
    {
        protected override string Job => "Avir_ArcaneCharge";
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.PowerTrader);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => pawn.Map.listerBuildings.allBuildingsColonist.Where(b => b.TryGetComp<CompPowerBattery>() != null);
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompPowerBattery b = t.TryGetComp<CompPowerBattery>();
            return b != null && b.StoredEnergyPct < 0.9f && !t.IsForbidden(pawn) && pawn.CanReserve(t, 1, -1, null, forced) && Able(pawn) != null;
        }
    }

    /// <summary>The smoothing that casters take up: the ordinary order and the arcane one, several at once.</summary>
    public class WorkGiver_MagicSmooth : WorkGiver_Scanner
    {
        public const float CostPer = 1f, XpPer = 2f, Radius = 3.9f;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        private static IEnumerable<DesignationDef> Defs => new[] { DesignationDefOf.SmoothFloor, DesignationDefOf.SmoothWall, Arcane.Smooth }.Where(d => d != null);
        public static bool Marked(Map map, IntVec3 c) => Defs.Any(d => map.designationManager.DesignationAt(c, d) != null);
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn) => Defs.SelectMany(d => pawn.Map.designationManager.SpawnedDesignationsOfDef(d)).Select(d => d.target.Cell).Distinct().ToList();
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => Arcane.Able(pawn, CostPer * 2f, RuneKind.Earth) == null;
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false) =>
            Arcane.Able(pawn, CostPer * 2f, RuneKind.Earth) != null && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced) && Marked(pawn.Map, c);
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
                    if (!cell.InBounds(map) || !WorkGiver_MagicSmooth.Marked(map, cell)) continue;
                    Thing wall = cell.GetEdifice(map);
                    if (wall != null && wall.def.building?.smoothedThing != null) SmoothableWallUtility.SmoothWall(wall, pawn);
                    else if (wall == null && map.terrainGrid.TerrainAt(cell).smoothedTerrain != null) map.terrainGrid.SetTerrain(cell, map.terrainGrid.TerrainAt(cell).smoothedTerrain);
                    else continue;
                    dm.TryRemoveDesignation(cell, DesignationDefOf.SmoothWall);
                    dm.TryRemoveDesignation(cell, DesignationDefOf.SmoothFloor);
                    if (Arcane.Smooth != null) dm.TryRemoveDesignation(cell, Arcane.Smooth);
                    FleckMaker.ThrowDustPuffThick(cell.ToVector3Shifted(), map, 0.8f, MagicFx.EarthDust);
                    MagicWork.Done(c, RuneKind.Earth, WorkGiver_MagicSmooth.CostPer, WorkGiver_MagicSmooth.XpPer);
                    n++;
                }
            };
            smooth.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return smooth;
        }
    }

    public static class MagicWorkDefOf
    {
        public static ThingDef Rock(Map map) => Find.World.NaturalRockTypesIn(map.Tile).FirstOrDefault() ?? ThingDefOf.Granite;
    }
}
